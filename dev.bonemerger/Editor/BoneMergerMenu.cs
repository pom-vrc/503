using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BoneMerger.Editors {
    /// <summary>
    /// Right-click (or GameObject menu) entry point. This is a destructive, immediate operation -
    /// unlike the NDMF-based tools in this repo it is not part of the non-destructive build
    /// pipeline and runs the moment it's invoked. Everything it does is wrapped in a single Undo
    /// group, so Ctrl+Z reverts the whole operation.
    /// </summary>
    public static class BoneMergerMenu {
        const string ToolName = "Bone Merger";
        const string OutputFolder = "Assets/BoneMerger Output";

        [MenuItem("GameObject/Bone Merger/Merge Selected Bone(s) Into Parent", false, 20)]
        static void MergeSelectedBones() {
            var selected = Selection.transforms.Where(t => t != null && t.parent != null).ToList();
            var skippedNoParent = Selection.transforms.Length - selected.Count;
            if (selected.Count == 0) {
                EditorUtility.DisplayDialog(ToolName,
                    "Select one or more bones with a parent in the Hierarchy first.", "OK");
                return;
            }

            var mergeTargets = BoneMergerCore.ResolveMergeTargets(selected);
            var validSelections = selected.Where(t => mergeTargets[t] != null).ToList();
            skippedNoParent += selected.Count - validSelections.Count;

            var selectedSet = new HashSet<Transform>(validSelections);
            var allRenderers = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
            var affectedRenderers = allRenderers
                .Where(r => r.sharedMesh != null && r.bones != null && r.bones.Any(b => b != null && selectedSet.Contains(b)))
                .ToArray();

            var mergedBones = new HashSet<Transform>();
            foreach (var r in affectedRenderers)
                foreach (var b in r.bones)
                    if (b != null && selectedSet.Contains(b)) mergedBones.Add(b);
            var noWeightBones = validSelections.Where(t => !mergedBones.Contains(t)).ToList();

            if (mergedBones.Count == 0) {
                EditorUtility.DisplayDialog(ToolName,
                    "None of the selected object(s) are referenced by any SkinnedMeshRenderer's bone weights. Nothing to merge.",
                    "OK");
                return;
            }

            var bonesWithExtraComponents = mergedBones.Where(b => b.GetComponents<Component>().Length > 1).ToList();

            var confirm = new StringBuilder();
            confirm.AppendLine($"Merge {mergedBones.Count} bone(s) into their parent(s)?");
            confirm.AppendLine($"{affectedRenderers.Length} renderer(s)/mesh(es) will be modified and saved to {OutputFolder}.");
            confirm.AppendLine("The merged bone GameObject(s) will be deleted; any of their children that weren't also selected are reparented onto the merge target first.");
            if (bonesWithExtraComponents.Count > 0)
                confirm.AppendLine($"\n{bonesWithExtraComponents.Count} of them carry other components (PhysBone, constraints, etc.) - those will be deleted too.");
            confirm.AppendLine("\nThis is destructive but undoable (Ctrl+Z).");

            if (!EditorUtility.DisplayDialog(ToolName, confirm.ToString(), "Merge", "Cancel"))
                return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Merge Bones Into Parent");
            var undoGroup = Undo.GetCurrentGroup();

            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder("Assets", "BoneMerger Output");

            var updatedMeshPaths = new List<string>();
            foreach (var renderer in affectedRenderers) {
                var result = BoneMergerCore.Merge(renderer, mergeTargets);
                Undo.RecordObject(renderer, "Merge Bones Into Parent");
                renderer.sharedMesh = result.mesh;
                renderer.bones = result.bones;

                var path = AssetDatabase.GenerateUniqueAssetPath($"{OutputFolder}/{SanitizeFileName(result.mesh.name)}.asset");
                AssetDatabase.CreateAsset(result.mesh, path);
                updatedMeshPaths.Add(path);
            }
            AssetDatabase.SaveAssets();

            // Reparent survivors before deleting, so nothing gets destroyed along with its parent.
            foreach (var bone in mergedBones) {
                if (bone == null) continue;
                var target = mergeTargets[bone];
                for (var i = bone.childCount - 1; i >= 0; i--) {
                    var child = bone.GetChild(i);
                    if (!selectedSet.Contains(child))
                        Undo.SetTransformParent(child, target, "Merge Bones Into Parent");
                }
            }
            foreach (var bone in mergedBones)
                if (bone != null) Undo.DestroyObjectImmediate(bone.gameObject);

            Undo.CollapseUndoOperations(undoGroup);

            var summary = new StringBuilder();
            summary.AppendLine($"Merged {mergedBones.Count} bone(s) into their parent(s).");
            summary.AppendLine($"Updated {affectedRenderers.Length} renderer(s):");
            foreach (var path in updatedMeshPaths) summary.AppendLine($"  {path}");
            if (noWeightBones.Count > 0) {
                summary.AppendLine($"\n{noWeightBones.Count} selected object(s) had no mesh weight and were left untouched:");
                foreach (var b in noWeightBones)
                    if (b != null) summary.AppendLine($"  {b.name}");
            }
            if (skippedNoParent > 0)
                summary.AppendLine($"\n{skippedNoParent} selected object(s) had no parent and were skipped.");

            EditorUtility.DisplayDialog(ToolName, summary.ToString(), "OK");
        }

        [MenuItem("GameObject/Bone Merger/Merge Selected Bone(s) Into Parent", true)]
        static bool ValidateMergeSelectedBones() {
            return Selection.transforms.Any(t => t != null && t.parent != null);
        }

        static string SanitizeFileName(string value) {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        }
    }
}
