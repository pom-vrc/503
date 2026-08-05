using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroScaleBoneCutter.Runtime;

namespace ZeroScaleBoneCutter.Editors
{
    /// <summary>
    /// Custom inspector for ZeroScaleBoneMeshCutter: a live count of how many skinned meshes under
    /// this GameObject would actually be cut, plus a "bake" button that runs the cut right now on a
    /// duplicate and saves the result as real, inspectable assets. The live NDMF build path (Play
    /// mode / upload) is unaffected by any of this - it keeps working the same way whether or not
    /// this button has ever been used.
    /// </summary>
    [CustomEditor(typeof(ZeroScaleBoneMeshCutter))]
    public class ZeroScaleBoneMeshCutterEditor : Editor
    {
        private const string ToolName = "Zero Scale Bone Cutter";
        private const string OutputFolder = "Assets/ZeroScaleBoneCutter Output";

        /// <summary>Shorthand for the localization table.</summary>
        private static string T(string english) => ZeroScaleBoneCutterLocalization.T(english);

        private static string T(string english, params object[] args) => ZeroScaleBoneCutterLocalization.T(english, args);

        public override void OnInspectorGUI()
        {
            var my = (ZeroScaleBoneMeshCutter)target;

            ZeroScaleBoneCutterLocalization.DrawLanguageBar();

            serializedObject.Update();
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(nameof(ZeroScaleBoneMeshCutter.aggressiveRemoval)),
                new GUIContent(T("Aggressive Removal"),
                    T("When off (default), a triangle is only cut when every one of its vertices is entirely weighted to zero-scale bones - a vertex that still carries any weight on a surviving bone is left alone, so a boundary that blends into the rest of the mesh stays closed instead of tearing open a hole. Turn this on to remove anything touching a zero-scale bone at all, even partially weighted - more thorough, but can expose a hole at a hard (non-blended) boundary.")));
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            DrawScanPreview(my);

            EditorGUILayout.Space();
            DrawBakeSection(my);
        }

        // ------------------------------------------------------------------ scan preview

        private static void DrawScanPreview(ZeroScaleBoneMeshCutter my)
        {
            var affected = ZeroScaleBoneCutterProcessor.GetAffectedRenderers(my);
            EditorGUILayout.HelpBox(
                affected.Length == 0
                    ? T("No skinned meshes under this GameObject reference a zero-scale bone.")
                    : T("{0} skinned mesh(es) have zero-scale bones and will be cut.", affected.Length),
                affected.Length == 0 ? MessageType.Info : MessageType.None);
        }

        // ------------------------------------------------------------------ bake

        private static void DrawBakeSection(ZeroScaleBoneMeshCutter my)
        {
            GUILayout.Label(
                T("Cutting normally happens during the build (Play mode or avatar upload) and never touches your original assets. This button does the same work right now instead, on a duplicate, and saves the result as real project assets you can inspect."),
                EditorStyles.wordWrappedMiniLabel);

            var hasWork = ZeroScaleBoneCutterProcessor.GetAffectedRenderers(my).Length > 0;
            using (new EditorGUI.DisabledScope(!hasWork))
            {
                if (GUILayout.Button(T("Create Optimized Copy In Hierarchy"), GUILayout.Height(26f)))
                {
                    CreateOptimizedCopy(my);
                }
            }

            if (!hasWork)
            {
                EditorGUILayout.HelpBox(T("No zero-scale bones found - nothing to cut."), MessageType.Info);
            }
        }

        private static void CreateOptimizedCopy(ZeroScaleBoneMeshCutter my)
        {
            var sourceGameObject = my.gameObject;
            var sourceTransform = my.transform;

            GameObject copy = null;
            var totalTrianglesRemoved = 0;
            var modifiedMeshCount = 0;
            var savedPaths = new List<string>();

            try
            {
                EditorUtility.DisplayProgressBar(ToolName, T("Cutting…"), 0.5f);

                copy = Instantiate(sourceGameObject, sourceTransform.parent);
                copy.name = sourceGameObject.name + " (Cut)";
                copy.transform.SetSiblingIndex(sourceTransform.GetSiblingIndex() + 1);

                var copiedComponent = copy.GetComponent<ZeroScaleBoneMeshCutter>();

                if (!AssetDatabase.IsValidFolder(OutputFolder))
                {
                    AssetDatabase.CreateFolder("Assets", "ZeroScaleBoneCutter Output");
                }

                foreach (var renderer in ZeroScaleBoneCutterProcessor.GetAffectedRenderers(copiedComponent))
                {
                    var result = ZeroScaleBoneCutterCore.Cut(renderer, copiedComponent.aggressiveRemoval);
                    if (result.trianglesRemoved == 0)
                    {
                        DestroyImmediate(result.mesh);
                        continue;
                    }

                    renderer.sharedMesh = result.mesh;
                    totalTrianglesRemoved += result.trianglesRemoved;
                    modifiedMeshCount++;

                    var path = AssetDatabase.GenerateUniqueAssetPath($"{OutputFolder}/{SanitizeFileName(result.mesh.name)}.asset");
                    AssetDatabase.CreateAsset(result.mesh, path);
                    savedPaths.Add(path);
                }
                AssetDatabase.SaveAssets();

                DestroyImmediate(copiedComponent);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Undo.RegisterCreatedObjectUndo(copy, "Create Optimized Copy");
            Selection.activeGameObject = copy;

            EditorUtility.DisplayDialog(ToolName,
                T("Created '{0}'.\n\n{1} mesh(es) modified, {2} triangle(s) removed total.\n\nMeshes saved to {3}.",
                    copy.name, modifiedMeshCount, totalTrianglesRemoved, OutputFolder),
                T("OK"));
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }
}
