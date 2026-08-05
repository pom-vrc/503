using System.Collections.Generic;
using UnityEngine;

namespace BoneMerger.Editors {
    /// <summary>
    /// Pure mesh/weight math for merging bones into their parent. Operates on one
    /// SkinnedMeshRenderer at a time; BoneMergerMenu handles hierarchy mutation, dialogs and
    /// orchestration across multiple renderers.
    /// </summary>
    public static class BoneMergerCore {
        public class Result {
            public Mesh mesh;
            public Transform[] bones;
        }

        /// <summary>
        /// Produces a new mesh (the original asset is left untouched) with every weight slot that
        /// referenced a merged bone reassigned to its resolved target, and the bindpose for that
        /// slot recomputed so the deformed result is identical. This is a minimal-diff transform:
        /// every bone slot NOT selected for merging is kept exactly as-is, in its original order,
        /// even if it happens to carry no weight on this particular mesh - only bones actually being
        /// merged are removed from the returned bones array.
        /// </summary>
        public static Result Merge(SkinnedMeshRenderer renderer, IReadOnlyDictionary<Transform, Transform> mergeTargets) {
            var sourceMesh = renderer.sharedMesh;
            var sourceBones = renderer.bones;
            var sourceBindposes = new List<Matrix4x4>();
            sourceMesh.GetBindposes(sourceBindposes);
            var sourceWeights = sourceMesh.boneWeights;

            var newBones = new List<Transform>();
            var newBindposes = new List<Matrix4x4>();
            // Only merged-away bones are deduplicated against each other (several original bones
            // can land on the same target with a genuinely different recomputed bindpose each, or
            // coincidentally the same one) - untouched bones always get their own fresh slot so the
            // rest of the array stays a stable 1:1 mapping.
            var mergedCandidatesByBone = new Dictionary<Transform, List<(Matrix4x4 pose, int index)>>();
            var oldToNewIndex = new int[sourceBones.Length];

            for (int i = 0; i < sourceBones.Length; i++) {
                var originalBone = sourceBones[i];
                if (originalBone == null) {
                    oldToNewIndex[i] = -1;
                    continue;
                }
                var bindpose = sourceBindposes[i];

                if (!mergeTargets.TryGetValue(originalBone, out var resolvedTarget) || resolvedTarget == null) {
                    // Not selected for merging: always keep, unchanged, as its own slot.
                    oldToNewIndex[i] = newBones.Count;
                    newBones.Add(originalBone);
                    newBindposes.Add(bindpose);
                    continue;
                }

                bindpose = resolvedTarget.worldToLocalMatrix * originalBone.localToWorldMatrix * bindpose;

                if (!mergedCandidatesByBone.TryGetValue(resolvedTarget, out var candidates)) {
                    candidates = new List<(Matrix4x4, int)>();
                    mergedCandidatesByBone[resolvedTarget] = candidates;
                }
                var foundIndex = -1;
                foreach (var (pose, index) in candidates) {
                    if (ApproximatelyEqual(pose, bindpose)) {
                        foundIndex = index;
                        break;
                    }
                }
                if (foundIndex < 0) {
                    foundIndex = newBones.Count;
                    newBones.Add(resolvedTarget);
                    newBindposes.Add(bindpose);
                    candidates.Add((bindpose, foundIndex));
                }
                oldToNewIndex[i] = foundIndex;
            }

            var newWeights = new BoneWeight[sourceWeights.Length];
            for (int i = 0; i < sourceWeights.Length; i++) {
                var w = sourceWeights[i];
                RemapSlot(oldToNewIndex, w.boneIndex0, out var boneIndex0, w.weight0, out var weight0);
                RemapSlot(oldToNewIndex, w.boneIndex1, out var boneIndex1, w.weight1, out var weight1);
                RemapSlot(oldToNewIndex, w.boneIndex2, out var boneIndex2, w.weight2, out var weight2);
                RemapSlot(oldToNewIndex, w.boneIndex3, out var boneIndex3, w.weight3, out var weight3);
                newWeights[i] = new BoneWeight {
                    boneIndex0 = boneIndex0, weight0 = weight0,
                    boneIndex1 = boneIndex1, weight1 = weight1,
                    boneIndex2 = boneIndex2, weight2 = weight2,
                    boneIndex3 = boneIndex3, weight3 = weight3,
                };
            }

            var mesh = Object.Instantiate(sourceMesh);
            mesh.name = sourceMesh.name + " (Bone Merged)";
            mesh.boneWeights = newWeights;
            mesh.bindposes = newBindposes.ToArray();

            return new Result { mesh = mesh, bones = newBones.ToArray() };
        }

        static void RemapSlot(int[] oldToNewIndex, int oldBoneIndex, out int newBoneIndex, float oldWeight, out float newWeight) {
            var mapped = oldBoneIndex >= 0 && oldBoneIndex < oldToNewIndex.Length ? oldToNewIndex[oldBoneIndex] : -1;
            if (mapped < 0) {
                newBoneIndex = 0;
                newWeight = 0;
            } else {
                newBoneIndex = mapped;
                newWeight = oldWeight;
            }
        }

        static bool ApproximatelyEqual(Matrix4x4 a, Matrix4x4 b, float epsilon = 0.0001f) {
            for (var i = 0; i < 16; i++)
                if (Mathf.Abs(a[i] - b[i]) > epsilon)
                    return false;
            return true;
        }

        /// <summary>
        /// Resolves each selected bone's actual merge target by walking up through the selected
        /// set: a contiguous chain of selected bones all collapse onto the first ancestor that is
        /// not itself selected, not just each bone's immediate parent. A bone whose chain runs off
        /// the top of the hierarchy (no unselected ancestor) maps to null.
        /// </summary>
        public static Dictionary<Transform, Transform> ResolveMergeTargets(IEnumerable<Transform> selected) {
            var selectedSet = new HashSet<Transform>(selected);
            var result = new Dictionary<Transform, Transform>();
            foreach (var bone in selectedSet) {
                var target = bone;
                while (target != null && selectedSet.Contains(target)) target = target.parent;
                result[bone] = target;
            }
            return result;
        }
    }
}
