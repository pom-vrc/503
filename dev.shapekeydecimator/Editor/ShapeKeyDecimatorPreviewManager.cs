using System.Collections.Generic;
using ShapeKeyDecimator.Runtime;
using UnityEditor;
using UnityEngine;

namespace ShapeKeyDecimator.Editors
{
    /// <summary>
    /// Temporary in-scene preview of the decimated result.
    ///
    /// Design note: preview state is deliberately *not* held in editor-side static fields. Every
    /// preview duplicate carries a <see cref="ShapeKeyDecimatorPreviewMarker"/> holding everything
    /// needed to undo it, so "is this component previewing?" is answered by scanning the scene. That
    /// makes the feature immune to domain reloads, selection changes and recompiles losing track of
    /// what has to be cleaned up.
    ///
    /// Preview objects are flagged <see cref="HideFlags.DontSave"/>, so they can never be written
    /// into a scene file or a build even if the user saves while a preview is live.
    /// </summary>
    [InitializeOnLoad]
    public static class ShapeKeyDecimatorPreviewManager
    {
        private static readonly Color OnColor = new Color(0.36f, 0.82f, 0.42f);

        static ShapeKeyDecimatorPreviewManager()
        {
            // Play mode: previews must be gone before the NDMF build pass runs, otherwise it would
            // decimate the preview duplicates as well.
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) DisableAll();
            };

            // A recompile drops DontSave objects, so tear down cleanly first rather than leaking.
            AssemblyReloadEvents.beforeAssemblyReload += DisableAll;

            // Catch anything a crash or forced reload left behind.
            EditorApplication.delayCall += SweepOrphans;
        }

        // ------------------------------------------------------------------ queries

        /// <summary>
        /// Scanning the scene is not free, so callers that need several answers should fetch the
        /// markers once and use the overloads below rather than asking repeatedly.
        /// </summary>
        public static List<ShapeKeyDecimatorPreviewMarker> GetMarkers(DecimatePolygonsByShapeKey component)
        {
            return FindMarkers(component);
        }

        public static bool IsPreviewing(DecimatePolygonsByShapeKey component)
        {
            return FindMarkers(component).Count > 0;
        }

        /// <summary>The settings hash the live preview was built from, or 0 when not previewing.</summary>
        public static int GetBuiltHash(IList<ShapeKeyDecimatorPreviewMarker> markers)
        {
            return markers.Count > 0 ? markers[0].builtHash : 0;
        }

        public static bool IsStale(DecimatePolygonsByShapeKey component, IList<ShapeKeyDecimatorPreviewMarker> markers)
        {
            return markers.Count > 0 && GetBuiltHash(markers) != ComputeHash(component);
        }

        public static Color OnBackgroundColor => OnColor;

        /// <summary>
        /// Everything that changes the decimated output. Any difference means the live preview no
        /// longer reflects the settings and has to be rebuilt.
        /// </summary>
        public static int ComputeHash(DecimatePolygonsByShapeKey component)
        {
            unchecked
            {
                var hash = 17;

                // Targets and their source meshes: swapping either must invalidate the preview.
                foreach (var target in ShapeKeyDecimatorUtil.FindTargets(component))
                {
                    hash = hash * 31 + target.renderer.GetInstanceID();
                    hash = hash * 31 + (target.SharedMesh != null ? target.SharedMesh.GetInstanceID() : 0);
                }

                // Empty or broken slots do not appear above, so count them too.
                hash = hash * 31 + (component.renderers != null ? component.renderers.Length : 0);
                hash = hash * 31 + (component.meshRenderers != null ? component.meshRenderers.Length : 0);

                if (component.shapeKeys != null)
                {
                    foreach (var entry in component.shapeKeys)
                    {
                        if (entry == null) continue;
                        hash = hash * 31 + (entry.blendShape != null ? entry.blendShape.GetHashCode() : 0);
                        hash = hash * 31 + entry.strength.GetHashCode();
                        hash = hash * 31 + entry.active.GetHashCode();
                    }
                }

                if (component.blacklistedShapeKeys != null)
                {
                    foreach (var name in component.blacklistedShapeKeys)
                    {
                        hash = hash * 31 + (name != null ? name.GetHashCode() : 0);
                    }
                }

                hash = hash * 31 + component.wholeMeshStrength.GetHashCode();
                hash = hash * 31 + component.wholeMeshActive.GetHashCode();
                hash = hash * 31 + component.deltaThreshold.GetHashCode();
                hash = hash * 31 + component.protectRegionBoundary.GetHashCode();
                hash = hash * 31 + component.preserveBorders.GetHashCode();
                hash = hash * 31 + component.preserveSubmeshBoundaries.GetHashCode();
                hash = hash * 31 + component.preserveUvSeams.GetHashCode();
                hash = hash * 31 + component.uvWeight.GetHashCode();
                hash = hash * 31 + component.maxNormalDeviation.GetHashCode();

                // Never let the hash be 0, that is the "not previewing" sentinel.
                return hash == 0 ? 1 : hash;
            }
        }

        // ------------------------------------------------------------------ enable / disable

        /// <summary>
        /// Rebuilds the preview from scratch. Returns false when the current settings produce no
        /// work, in which case preview is left off.
        /// </summary>
        public static bool Enable(DecimatePolygonsByShapeKey component, IList<DecimationTarget> targets)
        {
            Disable(component);

            List<ShapeKeyDecimationProcessor.RendererResult> results;
            try
            {
                EditorUtility.DisplayProgressBar("Shape Key Decimator",
                    ShapeKeyDecimatorLocalization.T("Building preview…"), 0.5f);
                results = ShapeKeyDecimationProcessor.Process(
                    targets, new List<DecimatePolygonsByShapeKey> { component }, logToConsole: false);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            var hash = ComputeHash(component);

            // A preview is a real decimation pass, so the inspector gets exact counts for free.
            ShapeKeyDecimationMeasurements.Record(component, hash, results);

            if (results.Count == 0) return false;

            foreach (var result in results)
            {
                var source = result.target;
                result.mesh.name = source.SharedMesh.name + " (Preview)";
                result.mesh.hideFlags = HideFlags.DontSave;

                var weights = source.CaptureBlendShapeWeights();

                var copy = Object.Instantiate(source.GameObject, source.Transform.parent);
                copy.name = source.GameObject.name + " (Decimated Preview)";
                copy.transform.SetSiblingIndex(source.Transform.GetSiblingIndex() + 1);
                MarkDontSaveRecursively(copy);

                // The duplicate must not carry decimation components of its own, or a later build
                // would try to decimate an already decimated mesh.
                foreach (var stale in copy.GetComponentsInChildren<DecimatePolygonsByShapeKey>(true))
                {
                    Object.DestroyImmediate(stale);
                }
                foreach (var stale in copy.GetComponentsInChildren<ShapeKeyDecimatorPreviewMarker>(true))
                {
                    Object.DestroyImmediate(stale);
                }

                var copiedTarget = source.RebindTo(copy);
                if (copiedTarget == null)
                {
                    // Should not happen, but never leave a half-built preview behind.
                    Object.DestroyImmediate(copy);
                    Object.DestroyImmediate(result.mesh);
                    continue;
                }

                copiedTarget.SharedMesh = result.mesh;
                copiedTarget.RendererEnabled = true;
                copiedTarget.ApplyBlendShapeWeights(weights);

                var marker = copy.AddComponent<ShapeKeyDecimatorPreviewMarker>();
                marker.owner = component;
                marker.original = source.renderer;
                marker.generatedMesh = result.mesh;
                marker.builtHash = hash;

                // Only promise to switch the original back on if we were the one who switched it off.
                marker.reEnableOriginalOnTeardown = source.RendererEnabled;
                if (source.RendererEnabled)
                {
                    source.RendererEnabled = false;
                    if (!component.previewDisabledRenderers.Contains(source.renderer))
                    {
                        component.previewDisabledRenderers.Add(source.renderer);
                    }
                }
            }

            EditorUtility.SetDirty(component);
            return true;
        }

        public static void Disable(DecimatePolygonsByShapeKey component)
        {
            foreach (var marker in FindMarkers(component)) TearDown(marker);

            // Belt and braces: restore anything recorded as disabled whose marker has gone missing.
            if (component != null)
            {
                if (component.previewDisabledRenderers != null)
                {
                    foreach (var renderer in component.previewDisabledRenderers)
                    {
                        if (renderer != null) renderer.enabled = true;
                    }
                    component.previewDisabledRenderers.Clear();
                }
                EditorUtility.SetDirty(component);
            }
        }

        public static void DisableAll()
        {
            foreach (var marker in FindAllMarkers()) TearDown(marker);

            foreach (var component in Resources.FindObjectsOfTypeAll<DecimatePolygonsByShapeKey>())
            {
                if (!IsInLoadedScene(component.gameObject)) continue;
                if (component.previewDisabledRenderers == null || component.previewDisabledRenderers.Count == 0) continue;

                foreach (var renderer in component.previewDisabledRenderers)
                {
                    if (renderer != null) renderer.enabled = true;
                }
                component.previewDisabledRenderers.Clear();
                EditorUtility.SetDirty(component);
            }
        }

        /// <summary>
        /// Cleans up previews whose owning component is gone, and re-enables renderers that were
        /// recorded as preview-disabled but have no live preview to justify it.
        /// </summary>
        public static void SweepOrphans()
        {
            foreach (var marker in FindAllMarkers())
            {
                if (marker.owner == null) TearDown(marker);
            }

            foreach (var component in Resources.FindObjectsOfTypeAll<DecimatePolygonsByShapeKey>())
            {
                if (!IsInLoadedScene(component.gameObject)) continue;
                if (component.previewDisabledRenderers == null || component.previewDisabledRenderers.Count == 0) continue;
                if (IsPreviewing(component)) continue;

                foreach (var renderer in component.previewDisabledRenderers)
                {
                    if (renderer != null) renderer.enabled = true;
                }
                component.previewDisabledRenderers.Clear();
                EditorUtility.SetDirty(component);
            }
        }

        [MenuItem("Tools/Shape Key Decimator/Turn Off All Previews")]
        private static void DisableAllMenu()
        {
            DisableAll();
            SweepOrphans();
        }

        // ------------------------------------------------------------------ internals

        private static void TearDown(ShapeKeyDecimatorPreviewMarker marker)
        {
            if (marker == null) return;

            if (marker.reEnableOriginalOnTeardown && marker.original != null)
            {
                marker.original.enabled = true;
            }

            var gameObject = marker.gameObject;
            var mesh = marker.generatedMesh;

            // Destroy the GameObject first so nothing is still referencing the mesh.
            if (gameObject != null) Object.DestroyImmediate(gameObject);
            if (mesh != null) Object.DestroyImmediate(mesh);
        }

        private static List<ShapeKeyDecimatorPreviewMarker> FindMarkers(DecimatePolygonsByShapeKey component)
        {
            var result = new List<ShapeKeyDecimatorPreviewMarker>();
            if (component == null) return result;

            foreach (var marker in FindAllMarkers())
            {
                if (marker.owner == component) result.Add(marker);
            }
            return result;
        }

        private static List<ShapeKeyDecimatorPreviewMarker> FindAllMarkers()
        {
            var result = new List<ShapeKeyDecimatorPreviewMarker>();

            // Resources.FindObjectsOfTypeAll also reaches inactive and hidden objects, which
            // FindObjectsOfType does not, and is not deprecated across Unity versions.
            foreach (var marker in Resources.FindObjectsOfTypeAll<ShapeKeyDecimatorPreviewMarker>())
            {
                if (marker == null || marker.gameObject == null) continue;
                if (!IsInLoadedScene(marker.gameObject)) continue;
                result.Add(marker);
            }
            return result;
        }

        private static bool IsInLoadedScene(GameObject gameObject)
        {
            // Filters out prefab assets and other objects that merely live in memory.
            return gameObject.scene.IsValid();
        }

        private static void MarkDontSaveRecursively(GameObject root)
        {
            root.hideFlags |= HideFlags.DontSave;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.hideFlags |= HideFlags.DontSave;
            }
        }

    }
}
