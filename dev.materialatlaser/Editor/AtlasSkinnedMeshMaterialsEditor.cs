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

        public override void OnInspectorGUI()
        {
            var my = (AtlasSkinnedMeshMaterials)target;
            if (my.excludedRenderers == null) my.excludedRenderers = new List<SkinnedMeshRenderer>();

            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", nameof(AtlasSkinnedMeshMaterials.excludedRenderers));
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            DrawSkinnedMeshList(my);

            EditorGUILayout.Space();
            DrawBakeSection(my);
        }

        // ------------------------------------------------------------------ skinned mesh list

        private static void DrawSkinnedMeshList(AtlasSkinnedMeshMaterials my)
        {
            var scanned = MaterialAtlasProcessor.ScanSkinnedRenderers(my);
            var includedCount = scanned.Count(r => !my.excludedRenderers.Contains(r));

            EditorGUILayout.LabelField($"Skinned Meshes ({includedCount}/{scanned.Length} included)", EditorStyles.boldLabel);

            if (scanned.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No SkinnedMeshRenderers with a mesh assigned were found under this GameObject.",
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
                if (GUILayout.Button("Include All"))
                {
                    Undo.RecordObject(my, "Include All Skinned Meshes");
                    my.excludedRenderers.Clear();
                    EditorUtility.SetDirty(my);
                }
                if (GUILayout.Button("Exclude All"))
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
                "Atlasing/merging normally happens during the build (Play mode or avatar upload) and never touches your original assets. This button does the same work right now instead, on a duplicate, and saves the result as real project assets you can inspect.",
                EditorStyles.wordWrappedMiniLabel);

            var hasWork = MaterialAtlasProcessor.GetIncludedSkinnedRenderers(my).Length > 0;
            using (new EditorGUI.DisabledScope(!hasWork))
            {
                if (GUILayout.Button("Create Optimized Copy In Hierarchy", GUILayout.Height(26f)))
                {
                    CreateOptimizedCopy(my);
                }
            }

            if (!hasWork)
            {
                EditorGUILayout.HelpBox(
                    "No included skinned meshes to atlas - check the list above.", MessageType.Info);
            }
        }

        private static void CreateOptimizedCopy(AtlasSkinnedMeshMaterials my)
        {
            var sourceGameObject = my.gameObject;
            var sourceTransform = my.transform;

            GameObject copy = null;
            try
            {
                EditorUtility.DisplayProgressBar(ToolName, "Atlasing and merging…", 0.5f);

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
                $"Created '{copy.name}' with {meshCount} mesh(es) and {materialCount} material slot(s) total.\n\n" +
                $"Meshes and materials saved to {OutputFolder}.",
                "OK");
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
