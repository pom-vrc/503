using System.Collections.Generic;
using System.Linq;
using ShapeKeyDecimator.Runtime;
using UnityEditor;
using UnityEngine;

namespace ShapeKeyDecimator.Editors
{
    [CustomEditor(typeof(DecimatePolygonsByShapeKey))]
    public class DecimatePolygonsByShapeKeyEditor : UnityEditor.Editor
    {
        private const float CountColumnWidth = 74f;
        private const float SliderColumnWidth = 140f;
        private const float ButtonColumnWidth = 22f;

        /// <summary>Dialog and progress bar title. Not translated: it is the tool's name.</summary>
        private const string ToolName = "Shape Key Decimator";

        /// <summary>How many region triangle counts we are willing to compute per repaint.</summary>
        private const int CountsPerFrame = 8;

        /// <summary>Idle time after a settings change before a live preview is rebuilt.</summary>
        private const double PreviewRebuildDelay = 0.35d;

        /// <summary>When the live preview went stale, or -1 when it is up to date.</summary>
        private double _previewDirtySince = -1d;

        /// <summary>Guards against queueing more than one deferred preview rebuild.</summary>
        private bool _previewRebuildQueued;

        private static readonly Dictionary<int, MeshRegionCache> Caches = new Dictionary<int, MeshRegionCache>();

        private bool _showAddList;
        private bool _showAdvanced;
        private string _search = "";
        private Vector2 _addScroll;

        private static GUIStyle _wrappedMiniLabel;

        /// <summary>Shorthand for the localization table.</summary>
        private static string T(string english)
        {
            return ShapeKeyDecimatorLocalization.T(english);
        }

        private static string T(string english, params object[] args)
        {
            return ShapeKeyDecimatorLocalization.T(english, args);
        }

        /// <summary>
        /// LabelField never grows past one line, which clips long explanations. GUILayout.Label with
        /// a word-wrapping style measures its own height, so the whole note stays visible.
        /// </summary>
        private static GUIStyle WrappedMiniLabel
        {
            get
            {
                if (_wrappedMiniLabel == null)
                {
                    _wrappedMiniLabel = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
                }
                return _wrappedMiniLabel;
            }
        }

        // ------------------------------------------------------------------ region count cache

        private class MeshRegionCache
        {
            public float threshold;
            public int[] vertToGroup;
            public int totalTriangles;
            public readonly Dictionary<string, int> regionTriangles = new Dictionary<string, int>();
        }

        private static MeshRegionCache GetCache(Mesh mesh, float threshold)
        {
            var id = mesh.GetInstanceID();
            if (Caches.TryGetValue(id, out var cache) && Mathf.Approximately(cache.threshold, threshold))
            {
                return cache;
            }

            cache = new MeshRegionCache
            {
                threshold = threshold,
                vertToGroup = ShapeKeyDecimatorUtil.WeldVertices(mesh.vertices, out _),
                totalTriangles = ShapeKeyDecimatorUtil.CountTriangles(mesh)
            };
            Caches[id] = cache;
            return cache;
        }

        /// <summary>
        /// Region triangle count for one shape key on one mesh, or -1 when it has not been computed
        /// yet. Computation is rate limited so a hundred shape keys never stall the inspector.
        /// </summary>
        private static int TryGetRegionTriangles(Mesh mesh, string shapeKey, float threshold, ref int budget)
        {
            if (mesh == null || string.IsNullOrEmpty(shapeKey)) return 0;
            if (mesh.GetBlendShapeIndex(shapeKey) < 0) return 0;

            var cache = GetCache(mesh, threshold);
            if (cache.regionTriangles.TryGetValue(shapeKey, out var cached)) return cached;
            if (budget <= 0) return -1;

            budget--;
            var affected = ShapeKeyDecimatorUtil.ComputeAffectedVertices(mesh, shapeKey, threshold, cache.vertToGroup);
            var count = ShapeKeyDecimatorUtil.CountTrianglesInRegion(mesh, affected);
            cache.regionTriangles[shapeKey] = count;
            return count;
        }

        public static void InvalidateCaches()
        {
            Caches.Clear();
        }

        [MenuItem("Tools/Shape Key Decimator/Clear Cached Triangle Counts")]
        private static void ClearCachesMenu()
        {
            InvalidateCaches();
            ShapeKeyDecimationMeasurements.ClearAll();
            Debug.Log("[Shape Key Decimator] Cached region triangle counts and measurements cleared.");
        }

        // ------------------------------------------------------------------ inspector

        public override void OnInspectorGUI()
        {
            var my = (DecimatePolygonsByShapeKey)target;
            my.SanitizeInEditor();

            ShapeKeyDecimatorLocalization.DrawLanguageBar();

            serializedObject.Update();
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(DecimatePolygonsByShapeKey.renderers)),
                new GUIContent(T("Skinned Meshes"),
                    T("Skinned meshes to decimate. These can use both shape key regions and the whole mesh slider.")));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(DecimatePolygonsByShapeKey.meshRenderers)),
                new GUIContent(T("Meshes"),
                    T("Plain meshes to decimate. Only the whole mesh slider applies to these, since they have no shape keys.")));

            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, T("Advanced"), true);
            if (_showAdvanced)
            {
                EditorGUI.indentLevel++;
                var thresholdProperty = serializedObject.FindProperty(nameof(DecimatePolygonsByShapeKey.deltaThreshold));
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(thresholdProperty, new GUIContent(T("Delta Threshold")));
                if (EditorGUI.EndChangeCheck()) InvalidateCaches();

                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(DecimatePolygonsByShapeKey.protectRegionBoundary)), new GUIContent(T("Protect Region Boundary")));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(DecimatePolygonsByShapeKey.preserveBorders)), new GUIContent(T("Preserve Open Borders")));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(DecimatePolygonsByShapeKey.preserveSubmeshBoundaries)), new GUIContent(T("Preserve Submesh Boundaries")));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(DecimatePolygonsByShapeKey.preserveUvSeams)), new GUIContent(T("Preserve UV Seams")));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(DecimatePolygonsByShapeKey.uvWeight)), new GUIContent(T("UV Error Weight")));
                EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(DecimatePolygonsByShapeKey.maxNormalDeviation)), new GUIContent(T("Max Normal Deviation")));

                if (!my.preserveUvSeams)
                {
                    EditorGUILayout.HelpBox(
                        T("With UV seams unlocked, collapses can merge two texture-space corners that share a position. That is the main cause of smearing along seams."),
                        MessageType.Warning);
                }

                if (my.maxNormalDeviation < 15f)
                {
                    EditorGUILayout.HelpBox(
                        T("Max Normal Deviation is very low, which rejects almost every collapse — at 0 no triangle may rotate at all, so nothing gets decimated on a curved mesh. Higher values decimate more; lower values are more conservative."),
                        MessageType.Warning);
                }
                EditorGUI.indentLevel--;
            }
            serializedObject.ApplyModifiedProperties();

            var targets = ShapeKeyDecimatorUtil.FindTargets(my);
            if (targets.Count == 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    T("Drag at least one Skinned Mesh Renderer or Mesh Renderer into the lists above. This component only ever touches the meshes you assign."),
                    MessageType.Warning);
                return;
            }

            // Shape key regions can only come from skinned meshes.
            var skinnedTargets = targets.Where(t => t.SupportsShapeKeys).ToList();
            if (skinnedTargets.Count < targets.Count)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    T("Only the whole mesh slider applies to plain meshes. Shape key rows below affect skinned meshes only."),
                    MessageType.Info);
            }

            var budget = CountsPerFrame;
            var pending = false;

            var originalTotal = targets.Sum(t => ShapeKeyDecimatorUtil.CountTriangles(t.SharedMesh));
            var projectedRemoved = 0;

            // Exact counts from a real pass, if one has run for these exact settings.
            var settingsHash = ShapeKeyDecimatorPreviewManager.ComputeHash(my);
            var measured = ShapeKeyDecimationMeasurements.Get(my, settingsHash);

            EditorGUILayout.Space();
            DrawTableHeader();

            var toRemove = -1;
            for (var i = 0; i < my.shapeKeys.Count; i++)
            {
                var entry = my.shapeKeys[i];
                if (entry == null) continue;

                var regionTriangles = 0;
                var incomplete = false;
                foreach (var target in skinnedTargets)
                {
                    var value = TryGetRegionTriangles(target.SharedMesh, entry.blendShape, my.deltaThreshold, ref budget);
                    if (value < 0) incomplete = true;
                    else regionTriangles += value;
                }
                if (incomplete) pending = true;

                var exact = false;
                int removed;
                if (measured != null && entry.blendShape != null &&
                    measured.regions.TryGetValue(entry.blendShape, out var regionMeasurement))
                {
                    // Measured: the region size the pass actually saw, and what it actually removed.
                    regionTriangles = regionMeasurement.trianglesInRegion;
                    removed = regionMeasurement.trianglesRemoved;
                    incomplete = false;
                    exact = true;
                }
                else
                {
                    removed = entry.active ? Mathf.RoundToInt(regionTriangles * Mathf.Clamp01(entry.strength)) : 0;
                }

                projectedRemoved += removed;

                if (DrawEntryRow(my, entry, regionTriangles, regionTriangles - removed, incomplete, exact)) toRemove = i;
            }

            if (my.shapeKeys.Count == 0)
            {
                EditorGUILayout.LabelField(T("No shape keys selected yet. Use the list below to add one."),
                    EditorStyles.miniLabel);
            }

            if (toRemove >= 0)
            {
                Undo.RecordObject(my, "Remove shape key from decimation");
                my.shapeKeys.RemoveAt(toRemove);
                EditorUtility.SetDirty(my);
            }

            projectedRemoved += DrawWholeMeshRow(my, originalTotal - projectedRemoved, pending, measured);

            // Measured removal is authoritative. Apply it to the full scene total rather than using
            // the report's own original count, since renderers that produced no regions are not in
            // the report but still contribute triangles to what the user sees.
            var totalRemoved = measured != null ? measured.TrianglesRemoved : projectedRemoved;
            DrawTotals(my, targets, settingsHash, originalTotal, totalRemoved, pending, measured != null);

            EditorGUILayout.Space();
            DrawAddSection(my, skinnedTargets, ref budget, ref pending);

            EditorGUILayout.Space();
            DrawApplySection(my, targets);

            if (pending) Repaint();
        }

        // ------------------------------------------------------------------ table

        private static void DrawTableHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField(T("Shape Key"), EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(
                    new GUIContent("△", T("Triangles that lie inside the region this shape key moves.")),
                    EditorStyles.miniBoldLabel, GUILayout.Width(CountColumnWidth));
                EditorGUILayout.LabelField(
                    new GUIContent(T("After"), T("Triangles remaining in that region once decimated.")),
                    EditorStyles.miniBoldLabel, GUILayout.Width(CountColumnWidth));
                EditorGUILayout.LabelField(
                    new GUIContent(T("Decimation"), T("0 = untouched, 1 = maximum reduction.")),
                    EditorStyles.miniBoldLabel, GUILayout.Width(SliderColumnWidth));
                GUILayout.Space(ButtonColumnWidth);
            }
        }

        /// <summary>
        /// Formats a triangle count. Estimates get a "~" so they are never mistaken for the exact
        /// numbers a real pass reports.
        /// </summary>
        private static string FormatCell(int value, bool incomplete, bool exact)
        {
            if (incomplete) return "…";
            return (exact ? "" : "~") + ShapeKeyDecimatorUtil.FormatCount(value);
        }

        /// <summary>Returns true when the user asked to remove this row.</summary>
        private bool DrawEntryRow(DecimatePolygonsByShapeKey my, ShapeKeyDecimationTarget entry,
            int regionTriangles, int after, bool incomplete, bool exact)
        {
            var remove = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                // Shape key names are user data, never translated.
                var active = EditorGUILayout.ToggleLeft(
                    new GUIContent(entry.blendShape, entry.blendShape),
                    entry.active,
                    GUILayout.MinWidth(60f));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(my, "Toggle shape key decimation");
                    entry.active = active;
                    EditorUtility.SetDirty(my);
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.LabelField(
                        FormatCell(regionTriangles, incomplete, exact),
                        GUILayout.Width(CountColumnWidth));
                    EditorGUILayout.LabelField(
                        FormatCell(entry.active ? after : regionTriangles, incomplete, exact),
                        GUILayout.Width(CountColumnWidth));
                }

                using (new EditorGUI.DisabledScope(!entry.active))
                {
                    EditorGUI.BeginChangeCheck();
                    var strength = EditorGUILayout.Slider(entry.strength, 0f, 1f, GUILayout.Width(SliderColumnWidth));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(my, "Change decimation strength");
                        entry.strength = Mathf.Clamp01(strength);
                        EditorUtility.SetDirty(my);
                    }
                }

                if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(ButtonColumnWidth)))
                {
                    remove = true;
                }
            }

            if (regionTriangles == 0 && !incomplete && !string.IsNullOrEmpty(entry.blendShape))
            {
                EditorGUILayout.LabelField(
                    "    " + T("No triangles fully inside this shape key's region — nothing to decimate."),
                    EditorStyles.miniLabel);
            }

            return remove;
        }

        /// <summary>
        /// The whole-mesh pass runs after every shape key region, so the triangles it can work on
        /// are whatever those regions left behind. Returns the triangles it is estimated to remove.
        /// </summary>
        private static int DrawWholeMeshRow(DecimatePolygonsByShapeKey my, int remainingTriangles, bool pending,
            MeasuredDecimation measured)
        {
            var exact = false;
            int removed;

            if (measured != null &&
                measured.regions.TryGetValue(ShapeKeyDecimatorUtil.WholeMeshRegionName, out var wholeMeshMeasurement))
            {
                remainingTriangles = wholeMeshMeasurement.trianglesInRegion;
                removed = wholeMeshMeasurement.trianglesRemoved;
                exact = true;
                pending = false;
            }
            else
            {
                removed = my.wholeMeshActive
                    ? Mathf.RoundToInt(remainingTriangles * Mathf.Clamp01(my.wholeMeshStrength))
                    : 0;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                var active = EditorGUILayout.ToggleLeft(
                    new GUIContent(T("Whole mesh"), T("Applied to the entire mesh after every shape key region above.")),
                    my.wholeMeshActive,
                    EditorStyles.boldLabel,
                    GUILayout.MinWidth(60f));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(my, "Toggle whole mesh decimation");
                    my.wholeMeshActive = active;
                    EditorUtility.SetDirty(my);
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.LabelField(
                        FormatCell(remainingTriangles, pending, exact),
                        GUILayout.Width(CountColumnWidth));
                    EditorGUILayout.LabelField(
                        FormatCell(remainingTriangles - removed, pending, exact),
                        GUILayout.Width(CountColumnWidth));
                }

                using (new EditorGUI.DisabledScope(!my.wholeMeshActive))
                {
                    EditorGUI.BeginChangeCheck();
                    var strength = EditorGUILayout.Slider(my.wholeMeshStrength, 0f, 1f, GUILayout.Width(SliderColumnWidth));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(my, "Change whole mesh decimation strength");
                        my.wholeMeshStrength = Mathf.Clamp01(strength);
                        EditorUtility.SetDirty(my);
                    }
                }

                GUILayout.Space(ButtonColumnWidth + 4f);
            }

            return removed;
        }

        private void DrawTotals(DecimatePolygonsByShapeKey my, List<DecimationTarget> targets, int settingsHash,
            int originalTotal, int removed, bool pending, bool exact)
        {
            var newTotal = Mathf.Max(0, originalTotal - removed);
            var percent = originalTotal > 0 ? removed * 100f / originalTotal : 0f;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        exact ? T("Total △ (measured)") : T("Total △ (estimate)"),
                        EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(
                        ShapeKeyDecimatorUtil.FormatCount(originalTotal),
                        EditorStyles.boldLabel,
                        GUILayout.Width(CountColumnWidth));
                    EditorGUILayout.LabelField("→", GUILayout.Width(14f));
                    EditorGUILayout.LabelField(
                        FormatCell(newTotal, pending && !exact, exact),
                        EditorStyles.boldLabel,
                        GUILayout.Width(CountColumnWidth));
                    EditorGUILayout.LabelField(
                        pending && !exact
                            ? ""
                            : $"(-{ShapeKeyDecimatorUtil.FormatCount(removed)}, -{percent:0.#}%)",
                        EditorStyles.miniLabel);
                }

                if (exact)
                {
                    GUILayout.Label(
                        T("Measured from a real decimation pass with the current settings, so these are the numbers you will actually get."),
                        WrappedMiniLabel);
                }
                else
                {
                    GUILayout.Label(
                        T("Estimate: region triangles × slider. It assumes every collapse succeeds, so it is an upper bound on the reduction, not a prediction. Collapses get rejected by Max Normal Deviation, UV seam and border protection and by the topology itself, and overlapping regions compound. Press Measure below, or turn on Preview, for the real figures."),
                        WrappedMiniLabel);

                    if (GUILayout.Button(T("Measure Exact Result")))
                    {
                        MeasureExact(my, targets, settingsHash);
                    }
                }
            }
        }

        /// <summary>
        /// Runs a real decimation pass purely to collect counts, then throws the meshes away. This is
        /// the only way to know how many collapses the validity rules reject.
        /// </summary>
        private static void MeasureExact(DecimatePolygonsByShapeKey my, List<DecimationTarget> targets, int settingsHash)
        {
            List<ShapeKeyDecimationProcessor.RendererResult> results;
            try
            {
                EditorUtility.DisplayProgressBar(ToolName, T("Measuring result…"), 0.5f);
                results = ShapeKeyDecimationProcessor.Process(
                    targets, new List<DecimatePolygonsByShapeKey> { my }, logToConsole: false);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ShapeKeyDecimationMeasurements.Record(my, settingsHash, results);

            // We only wanted the numbers; do not leak the generated meshes.
            foreach (var result in results)
            {
                if (result.mesh != null) Object.DestroyImmediate(result.mesh);
            }
        }

        // ------------------------------------------------------------------ add shape keys

        private void DrawAddSection(DecimatePolygonsByShapeKey my, List<DecimationTarget> skinnedTargets,
            ref int budget, ref bool pending)
        {
            var alreadyAdded = new HashSet<string>(my.shapeKeys.Where(e => e != null).Select(e => e.blendShape));

            var available = new List<string>();
            foreach (var target in skinnedTargets)
            {
                foreach (var name in ShapeKeyDecimatorUtil.GetBlendShapeNames(target.SharedMesh))
                {
                    if (!alreadyAdded.Contains(name) && !available.Contains(name)) available.Add(name);
                }
            }

            _showAddList = EditorGUILayout.Foldout(
                _showAddList, T("Add shape key to decimate ({0} available)", available.Count), true);
            if (!_showAddList) return;

            _search = EditorGUILayout.TextField(T("Search"), _search);

            var filtered = string.IsNullOrWhiteSpace(_search)
                ? available
                : available.Where(n => n.IndexOf(_search.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            var height = Mathf.Clamp(filtered.Count * 20f + 4f, 20f, 240f);
            string pendingAdd = null;

            using (var scope = new EditorGUILayout.ScrollViewScope(_addScroll, GUILayout.Height(height)))
            {
                _addScroll = scope.scrollPosition;
                foreach (var name in filtered)
                {
                    var regionTriangles = 0;
                    var incomplete = false;
                    foreach (var target in skinnedTargets)
                    {
                        var value = TryGetRegionTriangles(target.SharedMesh, name, my.deltaThreshold, ref budget);
                        if (value < 0) incomplete = true;
                        else regionTriangles += value;
                    }
                    if (incomplete) pending = true;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(ButtonColumnWidth)))
                        {
                            pendingAdd = name;
                        }
                        EditorGUILayout.LabelField(name);
                        EditorGUILayout.LabelField(
                            incomplete ? "…" : T("{0} △", ShapeKeyDecimatorUtil.FormatCount(regionTriangles)),
                            EditorStyles.miniLabel,
                            GUILayout.Width(CountColumnWidth + 24f));
                    }
                }
            }

            if (pendingAdd == null) return;

            Undo.RecordObject(my, "Add shape key to decimation");
            my.shapeKeys.Add(new ShapeKeyDecimationTarget { blendShape = pendingAdd, strength = 0.5f, active = true });
            EditorUtility.SetDirty(my);
            pending = true;   // triggers a repaint so the new row appears immediately
        }

        // ------------------------------------------------------------------ apply / bake

        private void DrawApplySection(DecimatePolygonsByShapeKey my, List<DecimationTarget> targets)
        {
            GUILayout.Label(
                T("Decimation happens during the build (Play mode or avatar upload). Your original mesh asset is never modified."),
                WrappedMiniLabel);

            var hasWork =
                (my.wholeMeshActive && my.wholeMeshStrength > 0f) ||
                my.shapeKeys.Any(e => e != null && e.active && e.strength > 0f && !string.IsNullOrEmpty(e.blendShape));

            DrawPreviewButton(my, targets, hasWork);

            using (new EditorGUI.DisabledScope(!hasWork))
            {
                if (GUILayout.Button(T("Create Decimated Copy In Hierarchy")))
                {
                    // Baking from a preview-disabled original would be confusing, so drop it first.
                    ShapeKeyDecimatorPreviewManager.Disable(my);
                    CreateDecimatedCopies(my, targets);
                }
            }

            GUILayout.Label(
                T("Bakes the result now: saves the reduced meshes as assets and adds a decimated duplicate of each renderer next to the original."),
                WrappedMiniLabel);
        }

        /// <summary>
        /// Preview toggle, plus the debounced rebuild when settings change while it is on. Rebuilds
        /// wait for the drag to finish and for a short idle gap, because each one is a full
        /// decimation pass and doing that per slider frame would be unusable.
        /// </summary>
        private void DrawPreviewButton(DecimatePolygonsByShapeKey my, List<DecimationTarget> targets, bool hasWork)
        {
            // One scene scan per repaint, reused for every question below.
            var markers = ShapeKeyDecimatorPreviewManager.GetMarkers(my);
            var previewing = markers.Count > 0;

            if (previewing && !hasWork)
            {
                // Everything got dialled back to zero, so there is nothing left to show.
                ShapeKeyDecimatorPreviewManager.Disable(my);
                previewing = false;
            }

            if (previewing && !_previewRebuildQueued && ShapeKeyDecimatorPreviewManager.IsStale(my, markers))
            {
                if (_previewDirtySince < 0d) _previewDirtySince = EditorApplication.timeSinceStartup;

                // Wait for the drag to end and for a short idle gap, so scrubbing a slider queues
                // exactly one rebuild instead of one per frame.
                var settled = GUIUtility.hotControl == 0 &&
                              EditorApplication.timeSinceStartup - _previewDirtySince > PreviewRebuildDelay;
                if (settled)
                {
                    // Rebuild outside the GUI pass: it destroys and creates GameObjects and shows a
                    // progress bar, none of which belongs inside a layout scope.
                    _previewRebuildQueued = true;
                    var component = my;
                    var previewTargets = targets;
                    EditorApplication.delayCall += () =>
                    {
                        _previewRebuildQueued = false;
                        _previewDirtySince = -1d;
                        if (component == null) return;
                        ShapeKeyDecimatorPreviewManager.Enable(component, previewTargets);
                        // The inspector may have been closed while the rebuild was queued.
                        if (this != null) Repaint();
                    };
                }
                else
                {
                    Repaint();
                }
            }
            else if (!_previewRebuildQueued)
            {
                _previewDirtySince = -1d;
            }

            var previousColor = GUI.backgroundColor;
            if (previewing) GUI.backgroundColor = ShapeKeyDecimatorPreviewManager.OnBackgroundColor;

            var toggled = false;
            using (new EditorGUI.DisabledScope(!hasWork && !previewing))
            {
                var label = previewing
                    ? (_previewDirtySince >= 0d ? T("PREVIEW ON  •  updating…") : T("PREVIEW ON  •  click to turn off"))
                    : T("Preview Decimation");

                toggled = GUILayout.Button(label, GUILayout.Height(26f));
            }

            GUI.backgroundColor = previousColor;

            if (toggled)
            {
                if (previewing)
                {
                    ShapeKeyDecimatorPreviewManager.Disable(my);
                    previewing = false;
                }
                else
                {
                    previewing = ShapeKeyDecimatorPreviewManager.Enable(my, targets);
                    if (!previewing)
                    {
                        EditorUtility.DisplayDialog(ToolName,
                            T("Nothing to preview. Check that the selected shape keys exist on the target meshes and that a strength is above zero."),
                            T("OK"));
                    }
                }
                _previewDirtySince = -1d;
                Repaint();
            }

            GUILayout.Label(previewing
                    ? T("Showing a temporary decimated duplicate; the original renderer is switched off. Turning preview off restores it and deletes the duplicate. Preview also ends automatically when you enter Play mode.")
                    : T("Temporarily swaps in a decimated duplicate so you can judge the result in the scene. Nothing is saved and the duplicate never enters a scene file or a build."),
                WrappedMiniLabel);
        }

        private static void CreateDecimatedCopies(DecimatePolygonsByShapeKey my, List<DecimationTarget> targets)
        {
            var components = new List<DecimatePolygonsByShapeKey> { my };

            List<ShapeKeyDecimationProcessor.RendererResult> results;
            try
            {
                // A whole-mesh pass on a dense mesh takes a few seconds; without this the editor
                // just looks frozen.
                EditorUtility.DisplayProgressBar(ToolName, T("Decimating meshes…"), 0.5f);
                results = ShapeKeyDecimationProcessor.Process(targets, components);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (results.Count == 0)
            {
                EditorUtility.DisplayDialog(ToolName,
                    T("Nothing was decimated. Check that the selected shape keys exist on the target meshes and that their strength is above zero."),
                    T("OK"));
                return;
            }

            var folder = "Assets/ShapeKeyDecimator Output";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets", "ShapeKeyDecimator Output");
            }

            var created = new List<GameObject>();
            foreach (var result in results)
            {
                var source = result.target;

                var assetPath = AssetDatabase.GenerateUniqueAssetPath(
                    $"{folder}/{SanitizeFileName(source.SharedMesh.name)}_Decimated.asset");
                result.mesh.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                AssetDatabase.CreateAsset(result.mesh, assetPath);

                // Blend shape weights are index based and get reset when the mesh is swapped, so
                // capture them by name first. No-op for a plain mesh.
                var weights = source.CaptureBlendShapeWeights();

                var copy = Object.Instantiate(source.GameObject, source.Transform.parent);
                copy.name = source.GameObject.name + " (Decimated)";
                copy.transform.SetSiblingIndex(source.Transform.GetSiblingIndex() + 1);

                foreach (var stale in copy.GetComponentsInChildren<DecimatePolygonsByShapeKey>(true))
                {
                    Object.DestroyImmediate(stale);
                }

                var copiedTarget = source.RebindTo(copy);
                if (copiedTarget == null)
                {
                    Object.DestroyImmediate(copy);
                    continue;
                }

                copiedTarget.SharedMesh = result.mesh;
                copiedTarget.ApplyBlendShapeWeights(weights);

                Undo.RegisterCreatedObjectUndo(copy, "Create decimated copy");
                created.Add(copy);
            }

            AssetDatabase.SaveAssets();
            if (created.Count > 0) Selection.objects = created.Cast<Object>().ToArray();

            var summary = string.Join("\n", results.Select(r => T("{0}: {1} → {2} △",
                r.target.Name,
                r.report.originalTriangles.ToString("N0"),
                r.report.newTriangles.ToString("N0"))));

            EditorUtility.DisplayDialog(ToolName,
                summary + "\n\n" + T("Meshes saved to {0}.", folder),
                T("OK"));
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }
}
