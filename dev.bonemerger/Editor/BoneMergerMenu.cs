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

        /// <summary>Shorthand for the localization table.</summary>
        private static string T(string english) => BoneMergerLocalization.T(english);

        private static string T(string english, params object[] args) => BoneMergerLocalization.T(english, args);

        [MenuItem("GameObject/Bone Merger/Merge Selected Bone(s) Into Parent", false, 20)]
        static void MergeSelectedBones() {
            var selected = Selection.transforms.Where(t => t != null && t.parent != null).ToList();
            var skippedNoParent = Selection.transforms.Length - selected.Count;
            if (selected.Count == 0) {
                EditorUtility.DisplayDialog(ToolName,
                    T("Select one or more bones with a parent in the Hierarchy first."), T("OK"));
                return;
            }

            var mergeTargets = BoneMergerCore.ResolveMergeTargets(selected);
            var validSelections = selected.Where(t => mergeTargets[t] != null).ToList();
            skippedNoParent += selected.Count - validSelections.Count;

            // Every valid selection is merged structurally - deleted, with its own children
            // reparented onto its resolved target - whether or not it actually carries any mesh
            // weight. A weightless bone (a twist/IK/socket helper, or just leftover from a prior
            // edit) has nothing to remap on the mesh side, but there's no reason to silently skip
            // it instead of folding it into its parent like the rest of the selection; leaving it
            // out here previously meant it could still get destroyed as a side effect of ITS OWN
            // parent being deleted (Unity destroys a GameObject's whole subtree), just without
            // ever being reparented first - the worst of both outcomes.
            var mergedBones = new HashSet<Transform>(validSelections);
            if (mergedBones.Count == 0) {
                EditorUtility.DisplayDialog(ToolName,
                    T("Select one or more bones with a parent in the Hierarchy first."), T("OK"));
                return;
            }

            var allRenderers = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
            var affectedRenderers = allRenderers
                .Where(r => r.sharedMesh != null && r.bones != null && r.bones.Any(b => b != null && mergedBones.Contains(b)))
                .ToArray();

            var weightedBones = new HashSet<Transform>();
            foreach (var r in affectedRenderers)
                foreach (var b in r.bones)
                    if (b != null && mergedBones.Contains(b)) weightedBones.Add(b);
            var noWeightBones = mergedBones.Where(t => !weightedBones.Contains(t)).ToList();

            var bonesWithExtraComponents = mergedBones.Where(b => b.GetComponents<Component>().Length > 1).ToList();

            var confirm = new StringBuilder();
            confirm.AppendLine(T("Merge {0} bone(s) into their parent(s)?", mergedBones.Count));
            confirm.AppendLine(T("{0} renderer(s)/mesh(es) will be modified and saved to {1}.", affectedRenderers.Length, OutputFolder));
            confirm.AppendLine(T("The merged bone GameObject(s) will be deleted; any of their children that weren't also selected are reparented onto the merge target first."));
            if (bonesWithExtraComponents.Count > 0)
                confirm.AppendLine(T("\n{0} of them carry other components (PhysBone, constraints, etc.) - those will be deleted too.", bonesWithExtraComponents.Count));
            confirm.AppendLine(T("\nThis is destructive but undoable (Ctrl+Z)."));

            if (!EditorUtility.DisplayDialog(ToolName, confirm.ToString(), T("Merge"), T("Cancel")))
                return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Merge Bones Into Parent");
            var undoGroup = Undo.GetCurrentGroup();

            if (affectedRenderers.Length > 0 && !AssetDatabase.IsValidFolder(OutputFolder))
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
                    if (!mergedBones.Contains(child))
                        Undo.SetTransformParent(child, target, "Merge Bones Into Parent");
                }
            }
            foreach (var bone in mergedBones)
                if (bone != null) Undo.DestroyObjectImmediate(bone.gameObject);

            Undo.CollapseUndoOperations(undoGroup);

            var summary = new StringBuilder();
            summary.AppendLine(T("Merged {0} bone(s) into their parent(s).", mergedBones.Count));
            summary.AppendLine(T("Updated {0} renderer(s):", affectedRenderers.Length));
            foreach (var path in updatedMeshPaths) summary.AppendLine($"  {path}");
            if (noWeightBones.Count > 0) {
                summary.AppendLine(T("\n{0} of them had no mesh weight - merged structurally with nothing to remap:", noWeightBones.Count));
                foreach (var b in noWeightBones)
                    if (b != null) summary.AppendLine($"  {b.name}");
            }
            if (skippedNoParent > 0)
                summary.AppendLine(T("\n{0} selected object(s) had no parent and were skipped.", skippedNoParent));

            EditorUtility.DisplayDialog(ToolName, summary.ToString(), T("OK"));
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
