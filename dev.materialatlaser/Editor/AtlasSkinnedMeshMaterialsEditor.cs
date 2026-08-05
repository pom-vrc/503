using System.Collections.Generic;
using System.IO;
using System.Linq;
using MaterialAtlaser.Runtime;
using UnityEditor;
using UnityEngine;

namespace MaterialAtlaser.Editors
{
    /// <summary>
    /// Custom inspector for AtlasSkinnedMeshMaterials: a checklist of every SkinnedMeshRenderer the
    /// automatic scan finds (so it's clear what's about to be combined, and any of them can be
    /// dropped from - or added back to - the merge), plus a "bake" button that runs the atlas/merge
    /// right now on a duplicate and saves the result as real, inspectable assets. The live NDMF
    /// build path (Play mode / upload) is unaffected by any of this - it keeps working the same way
    /// whether or not this button has ever been used.
    /// </summary>
    [CustomEditor(typeof(AtlasSkinnedMeshMaterials))]
    public class AtlasSkinnedMeshMaterialsEditor : Editor
    {
        private const string ToolName = "Material Atlaser";
        private const string OutputFolder = "Assets/MaterialAtlaser Output";

        /// <summary>Shorthand for the localization table.</summary>
        private static string T(string english)
        {
            return MaterialAtlaserLocalization.T(english);
        }

        private static string T(string english, params object[] args)
        {
            return MaterialAtlaserLocalization.T(english, args);
        }

        public override void OnInspectorGUI()
        {
            var my = (AtlasSkinnedMeshMaterials)target;
            if (my.excludedRenderers == null) my.excludedRenderers = new List<SkinnedMeshRenderer>();

            MaterialAtlaserLocalization.DrawLanguageBar();

            serializedObject.Update();
            DrawFields();
            serializedObject.ApplyModifiedProperties();

            if (my.mergeSkinnedMeshesAndMaterialSlots)
            {
                DrawMergeTargetPreview(my);
            }

            EditorGUILayout.Space();
            DrawSkinnedMeshList(my);

            EditorGUILayout.Space();
            DrawBakeSection(my);
        }

        // ------------------------------------------------------------------ fields

        private void DrawFields()
        {
            EditorGUILayout.LabelField(T("Scan"), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(AtlasSkinnedMeshMaterials.ignoreRegularMeshes)),
                new GUIContent(T("Ignore Regular Meshes"),
                    T("When enabled, plain (non-skinned) MeshRenderers under this GameObject are left untouched and keep their original materials. Disable to fold them into the atlas as well.")));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(T("Atlas"), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(AtlasSkinnedMeshMaterials.atlasSize)),
                new GUIContent(T("Atlas Size"),
                    T("Maximum size (in pixels) of each generated atlas texture. Materials are packed as densely as they fit; if there isn't enough room they're shrunk to fit rather than overflowing.")));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(AtlasSkinnedMeshMaterials.atlasCount)),
                new GUIContent(T("Atlas Count"),
                    T("How many atlas textures/materials to split the scanned materials across. 1 means every material ends up on a single shared material (one material slot), even across different shaders - a transparent/cutout material grouped with an opaque one will render opaque from then on. Raising this spreads materials over more atlases - and more material slots - to keep texture detail (and shader features) higher when there are a lot of very different materials.")));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(T("Merge"), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(AtlasSkinnedMeshMaterials.mergeSkinnedMeshesAndMaterialSlots)),
                new GUIContent(T("Merge Skinned Meshes And Material Slots"),
                    T("After atlasing, combine every affected SkinnedMeshRenderer into a single SkinnedMeshRenderer sharing one merged mesh, instead of leaving the renderers separate for d4rkAvatarOptimizer (or another tool) to merge afterwards. Turn this on if d4rkAvatarOptimizer isn't picking up the merge on its own - it looks for renderers/materials that are already trivially mergeable, and this does that merge directly rather than hoping it recognizes the result. Only SkinnedMeshRenderers are combined; plain MeshRenderers (even if atlased because 'Ignore Regular Meshes' is off) are left separate. Renderers that rely on being toggled independently (e.g. via animated active-state) should not be merged, since they'd lose that independence.")));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(AtlasSkinnedMeshMaterials.mergedMeshName)),
                new GUIContent(T("Merged Mesh Name"),
                    T("When meshes are actually merged (2+ skinned meshes with 'Merge Skinned Meshes And Material Slots' on), this does one of two things. If it matches one of the included skinned meshes by name, merging targets that renderer directly - it survives as-is instead of whichever renderer happened to be scanned first, so anything with a direct reference to it (most importantly VRCAvatarDescriptor's eyelids/viseme 'Body' mesh) keeps working. Otherwise it's just the merged mesh's name (and also renames the surviving renderer's GameObject to match, so it shows up in the Hierarchy, not just the Mesh field in its Inspector). Leave blank to default to the first included skinned mesh's name plus \" (Merged)\", with nothing renamed.")));
        }

        // ------------------------------------------------------------------ merge target preview

        /// <summary>
        /// Live preview of what the merged mesh name field actually does: if it matches one of the
        /// included skinned meshes by name, that one survives the merge as-is (so anything with a
        /// direct reference to it - most importantly VRCAvatarDescriptor's eyelids/viseme "Body"
        /// mesh - keeps working). Otherwise it's just the resulting mesh's name and the first
        /// included renderer survives, same as leaving it blank.
        /// </summary>
        private static void DrawMergeTargetPreview(AtlasSkinnedMeshMaterials my)
        {
            var included = MaterialAtlasProcessor.GetIncludedSkinnedRenderers(my);
            var trimmedName = my.mergedMeshName?.Trim();

            if (string.IsNullOrEmpty(trimmedName))
            {
                EditorGUILayout.HelpBox(
                    T("Merged mesh will be named \"{0}\".", MaterialAtlasProcessor.ComputeDefaultMergedMeshName(my)),
                    MessageType.None);
                return;
            }

            var target = MaterialAtlasProcessor.FindNamedMergeTarget(my, included);
            if (target != null)
            {
                EditorGUILayout.HelpBox(
                    T("Merging into \"{0}\" - it survives as-is, so anything referencing it directly (like the Avatar Descriptor's eyelids/viseme mesh) keeps working.", target.name),
                    MessageType.None);
            }
            else
            {
                var fallback = included.Length > 0 ? included[0].name : my.gameObject.name;
                EditorGUILayout.HelpBox(
                    T("No included skinned mesh named \"{0}\" - will merge into \"{1}\" instead and name the result \"{0}\".", trimmedName, fallback),
                    MessageType.Info);
            }
        }

        // ------------------------------------------------------------------ skinned mesh list

        private static void DrawSkinnedMeshList(AtlasSkinnedMeshMaterials my)
        {
            var scanned = MaterialAtlasProcessor.ScanSkinnedRenderers(my);
            var includedCount = scanned.Count(r => !my.excludedRenderers.Contains(r));

            EditorGUILayout.LabelField(T("Skinned Meshes ({0}/{1} included)", includedCount, scanned.Length), EditorStyles.boldLabel);

            if (scanned.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    T("No SkinnedMeshRenderers with a mesh assigned were found under this GameObject."),
                    MessageType.Info);
                return;
            }

            foreach (var renderer in scanned)
            {
                var excluded = my.excludedRenderers.Contains(renderer);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var included = EditorGUILayout.ToggleLeft(GUIContent.none, !excluded, GUILayout.Width(20f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(my, included ? "Include Skinned Mesh" : "Exclude Skinned Mesh");
                        if (included) my.excludedRenderers.Remove(renderer);
                        else if (!excluded) my.excludedRenderers.Add(renderer);
                        EditorUtility.SetDirty(my);
                    }

                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ObjectField(renderer, typeof(SkinnedMeshRenderer), true);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(T("Include All")))
                {
                    Undo.RecordObject(my, "Include All Skinned Meshes");
                    my.excludedRenderers.Clear();
                    EditorUtility.SetDirty(my);
                }
                if (GUILayout.Button(T("Exclude All")))
                {
                    Undo.RecordObject(my, "Exclude All Skinned Meshes");
                    my.excludedRenderers = scanned.ToList();
                    EditorUtility.SetDirty(my);
                }
            }
        }

        // ------------------------------------------------------------------ bake

        private static void DrawBakeSection(AtlasSkinnedMeshMaterials my)
        {
            GUILayout.Label(
                T("Atlasing/merging normally happens during the build (Play mode or avatar upload) and never touches your original assets. This button does the same work right now instead, on a duplicate, and saves the result as real project assets you can inspect."),
                EditorStyles.wordWrappedMiniLabel);

            var hasWork = MaterialAtlasProcessor.GetIncludedSkinnedRenderers(my).Length > 0;
            using (new EditorGUI.DisabledScope(!hasWork))
            {
                if (GUILayout.Button(T("Create Optimized Copy In Hierarchy"), GUILayout.Height(26f)))
                {
                    CreateOptimizedCopy(my);
                }
            }

            if (!hasWork)
            {
                EditorGUILayout.HelpBox(
                    T("No included skinned meshes to atlas - check the list above."), MessageType.Info);
            }
        }

        private static void CreateOptimizedCopy(AtlasSkinnedMeshMaterials my)
        {
            var sourceGameObject = my.gameObject;
            var sourceTransform = my.transform;

            GameObject copy = null;
            try
            {
                EditorUtility.DisplayProgressBar(ToolName, T("Atlasing and merging…"), 0.5f);

                copy = Instantiate(sourceGameObject, sourceTransform.parent);
                copy.name = sourceGameObject.name + " (Atlased)";
                copy.transform.SetSiblingIndex(sourceTransform.GetSiblingIndex() + 1);

                var copiedComponent = copy.GetComponent<AtlasSkinnedMeshMaterials>();
                MaterialAtlasProcessor.Process(copiedComponent);
                DestroyImmediate(copiedComponent);

                SaveGeneratedAssets(copy);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Undo.RegisterCreatedObjectUndo(copy, "Create Optimized Copy");
            Selection.activeGameObject = copy;

            var renderers = copy.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var meshCount = renderers.Select(r => r.sharedMesh).Where(m => m != null).Distinct().Count();
            var materialCount = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().Count();

            EditorUtility.DisplayDialog(ToolName,
                T("Created '{0}' with {1} mesh(es) and {2} material slot(s) total.\n\nMeshes and materials saved to {3}.",
                    copy.name, meshCount, materialCount, OutputFolder),
                T("OK"));
        }

        /// <summary>
        /// Process() already saves the atlas texture(s) as real, imported assets (that happens
        /// during the live build too, not just here). What's still purely in-memory afterward is the
        /// merged/remapped mesh(es) and the atlas material(s) themselves, so those are what get
        /// saved here. Anything already backed by an asset (an untouched original mesh/material that
        /// passed through unchanged) is left alone rather than re-saved as a duplicate.
        /// </summary>
        private static void SaveGeneratedAssets(GameObject copy)
        {
            if (!AssetDatabase.IsValidFolder(OutputFolder))
            {
                AssetDatabase.CreateFolder("Assets", "MaterialAtlaser Output");
            }

            var savedMeshes = new HashSet<Mesh>();
            var savedMaterials = new HashSet<Material>();

            foreach (var renderer in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh != null)
                {
                    SaveIfGenerated(renderer.sharedMesh, m => m.name, ".asset", savedMeshes);
                }

                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null)
                    {
                        SaveIfGenerated(material, m => m.name, ".mat", savedMaterials);
                    }
                }
            }

            AssetDatabase.SaveAssets();
        }

        private static void SaveIfGenerated<T>(T asset, System.Func<T, string> getName, string extension, HashSet<T> seen)
            where T : Object
        {
            if (!seen.Add(asset)) return;
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset))) return; // already a real asset

            var path = AssetDatabase.GenerateUniqueAssetPath($"{OutputFolder}/{SanitizeFileName(getName(asset))}{extension}");
            AssetDatabase.CreateAsset(asset, path);
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }
}
