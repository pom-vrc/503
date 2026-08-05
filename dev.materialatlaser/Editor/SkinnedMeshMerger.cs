using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MaterialAtlaser.Editors
{
    /// <summary>
    /// Combines several already-atlased SkinnedMeshRenderers into one renderer sharing one merged
    /// mesh. This exists because d4rkAvatarOptimizer's own merge pass doesn't always pick up meshes
    /// atlased by this tool (it applies its own, stricter compatibility checks - shadow/layer
    /// settings, animation curves, default-enabled state, etc. - on top of material equality), so
    /// relying on it to finish the job isn't guaranteed. This does the merge directly instead.
    ///
    /// Bone handling follows the same approach as anatawa12's Avatar Optimizer's merge pass: bones
    /// are never deduplicated across source renderers, even when two renderers reference the same
    /// bone Transform - each source's own bindpose for that bone travels with its own vertices, by
    /// simple concatenation. Deduplicating by Transform would only save a little array space, but
    /// risks using the wrong bindpose for a shared bone if the two source meshes' bind data ever
    /// differs even slightly (separate export/import/rebind passes), which is exactly the class of
    /// bug that puts merged geometry in the wrong pose.
    ///
    /// Only SkinnedMeshRenderers are merged (never plain MeshRenderers). A renderer that's toggled
    /// independently of the others (an animated active-state, a PhysBone-driven visibility, etc.)
    /// loses that independence once folded in here - there is no detection for this, it's a
    /// judgment call left to whoever enables the merge toggle.
    /// </summary>
    internal static class SkinnedMeshMerger
    {
        /// <summary>
        /// Returns the surviving (primary) renderer, or null if there was nothing to merge.
        /// <paramref name="preferredPrimary"/>, if given, becomes that survivor instead of whichever
        /// renderer happens to be first - everything else still merges into it exactly the same way.
        /// </summary>
        public static SkinnedMeshRenderer Merge(
            string ownerName, SkinnedMeshRenderer[] renderers, string meshName,
            SkinnedMeshRenderer preferredPrimary = null)
        {
            var candidates = renderers.Where(r => r != null && r.sharedMesh != null).ToArray();
            if (candidates.Length <= 1) return null;

            if (preferredPrimary != null)
            {
                var preferredIndex = Array.IndexOf(candidates, preferredPrimary);
                if (preferredIndex > 0)
                {
                    (candidates[0], candidates[preferredIndex]) = (candidates[preferredIndex], candidates[0]);
                }
            }

            var mergedBones = new List<Transform>();
            var mergedBindposes = new List<Matrix4x4>();

            var mergedVertices = new List<Vector3>();
            var mergedNormals = new List<Vector3>();
            var mergedTangents = new List<Vector4>();
            var mergedUv = new List<Vector2>();
            var mergedColors = new List<Color32>();
            var mergedBoneWeights = new List<BoneWeight>();
            var hasColors = candidates.Any(r => r.sharedMesh.colors32.Length > 0);

            // Grouped by material reference rather than atlas index: this handles atlas materials
            // (shared instances, so submeshes across renderers naturally combine) and any passed-
            // through materials (outline/utility passes left out of the atlas) uniformly - the
            // latter only combine with each other when they happen to be the same instance too.
            var trianglesByMaterial = new Dictionary<Material, List<int>>();
            var materialOrder = new List<Material>();
            var vertexOffsets = new int[candidates.Length];
            var boneOffsets = new int[candidates.Length];
            var totalVertexCount = 0;

            for (var r = 0; r < candidates.Length; r++)
            {
                var renderer = candidates[r];
                var mesh = renderer.sharedMesh;
                vertexOffsets[r] = totalVertexCount;
                totalVertexCount += mesh.vertexCount;

                var bones = renderer.bones;
                var bindposes = mesh.bindposes;
                boneOffsets[r] = mergedBones.Count;
                var boneCount = Mathf.Min(bones.Length, bindposes.Length);
                for (var b = 0; b < boneCount; b++)
                {
                    mergedBones.Add(bones[b]);
                    mergedBindposes.Add(bindposes[b]);
                }

                mergedVertices.AddRange(mesh.vertices);
                mergedNormals.AddRange(PadToVertexCount(mesh.normals, mesh.vertexCount));
                mergedTangents.AddRange(PadToVertexCount(mesh.tangents, mesh.vertexCount));
                mergedUv.AddRange(PadToVertexCount(mesh.uv, mesh.vertexCount));
                if (hasColors)
                {
                    var colors = mesh.colors32;
                    mergedColors.AddRange(colors.Length == mesh.vertexCount
                        ? colors
                        : Enumerable.Repeat(new Color32(255, 255, 255, 255), mesh.vertexCount));
                }

                var boneBase = boneOffsets[r];
                foreach (var weight in mesh.boneWeights)
                {
                    mergedBoneWeights.Add(new BoneWeight
                    {
                        boneIndex0 = RemapBoneIndex(weight.boneIndex0, boneBase, boneCount),
                        boneIndex1 = RemapBoneIndex(weight.boneIndex1, boneBase, boneCount),
                        boneIndex2 = RemapBoneIndex(weight.boneIndex2, boneBase, boneCount),
                        boneIndex3 = RemapBoneIndex(weight.boneIndex3, boneBase, boneCount),
                        weight0 = weight.weight0,
                        weight1 = weight.weight1,
                        weight2 = weight.weight2,
                        weight3 = weight.weight3,
                    });
                }

                var materials = renderer.sharedMaterials;
                var offset = vertexOffsets[r];
                for (var sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
                {
                    var material = materials[sub];
                    if (material == null) continue;

                    if (!trianglesByMaterial.TryGetValue(material, out var list))
                    {
                        list = new List<int>();
                        trianglesByMaterial[material] = list;
                        materialOrder.Add(material);
                    }
                    foreach (var index in mesh.GetTriangles(sub)) list.Add(index + offset);
                }
            }

            var mergedMesh = new Mesh { name = meshName };
            if (totalVertexCount > 65535) mergedMesh.indexFormat = IndexFormat.UInt32;
            mergedMesh.SetVertices(mergedVertices);
            mergedMesh.SetNormals(mergedNormals);
            mergedMesh.SetTangents(mergedTangents);
            mergedMesh.SetUVs(0, mergedUv);
            if (hasColors) mergedMesh.SetColors(mergedColors);
            mergedMesh.boneWeights = mergedBoneWeights.ToArray();
            mergedMesh.bindposes = mergedBindposes.ToArray();

            MergeBlendShapes(candidates, vertexOffsets, totalVertexCount, mergedMesh);

            mergedMesh.subMeshCount = materialOrder.Count;
            for (var i = 0; i < materialOrder.Count; i++)
            {
                mergedMesh.SetTriangles(trianglesByMaterial[materialOrder[i]], i);
            }

            var primary = candidates[0];
            var rootBone = candidates.Select(c => c.rootBone).FirstOrDefault(b => b != null);
            mergedMesh.bounds = ComputeMergedBounds(candidates, rootBone != null ? rootBone : primary.transform);

            primary.sharedMesh = mergedMesh;
            primary.bones = mergedBones.ToArray();
            primary.sharedMaterials = materialOrder.ToArray();
            if (rootBone != null) primary.rootBone = rootBone;

            ApplyBlendShapeWeights(candidates, primary, mergedMesh);

            for (var i = 1; i < candidates.Length; i++)
            {
                var leftoverGameObject = candidates[i].gameObject;
                UnityEngine.Object.DestroyImmediate(candidates[i]);

                // Only Transform left, and nothing parented under it - it's dead weight, remove it.
                // Otherwise (colliders, PhysBones, other scripts, children) leave it in place.
                if (leftoverGameObject.transform.childCount == 0 &&
                    leftoverGameObject.GetComponents<Component>().Length == 1)
                {
                    UnityEngine.Object.DestroyImmediate(leftoverGameObject);
                }
            }

            Debug.Log($"(AtlasSkinnedMeshMaterials) '{ownerName}': merged {candidates.Length} skinned meshes into " +
                      $"'{primary.gameObject.name}' ({mergedBones.Count} bone reference(s), {materialOrder.Count} material slot(s)).");

            return primary;
        }

        private static T[] PadToVertexCount<T>(T[] source, int vertexCount)
        {
            return source.Length == vertexCount ? source : new T[vertexCount];
        }

        private static int RemapBoneIndex(int index, int boneBase, int boneCount)
        {
            return index >= 0 && index < boneCount ? boneBase + index : boneBase;
        }

        /// <summary>
        /// SkinnedMeshRenderer.localBounds is defined relative to the root bone, not the raw mesh
        /// vertex data - so it can't just be RecalculateBounds()'d from the merged vertex buffer.
        /// Each source's own local bounds are transformed into the merged root bone's space (via its
        /// own root bone, falling back to its own transform if it has none) and unioned, matching
        /// anatawa12 Avatar Optimizer's MergeBounds.
        /// </summary>
        private static Bounds ComputeMergedBounds(SkinnedMeshRenderer[] candidates, Transform targetRootBone)
        {
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;

            foreach (var renderer in candidates)
            {
                var sourceSpace = renderer.rootBone != null ? renderer.rootBone : renderer.transform;
                var bounds = renderer.sharedMesh.bounds;
                var center = bounds.center;
                var extents = bounds.extents;

                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    var inTarget = targetRootBone.InverseTransformPoint(sourceSpace.TransformPoint(corner));
                    min = Vector3.Min(min, inTarget);
                    max = Vector3.Max(max, inTarget);
                }
            }

            var result = new Bounds();
            result.SetMinMax(min, max);
            return result;
        }

        /// <summary>
        /// The merge only carries over blend shape delta data - the SkinnedMeshRenderer's current
        /// weight for each shape lives separately (SkinnedMeshRenderer.GetBlendShapeWeight) and is
        /// never touched by reassigning sharedMesh. Without this, any accessory that relied on a
        /// non-zero default blend shape weight (a default pose, a permanently-on toggle shape) would
        /// silently revert to that shape's zero/rest state once its renderer is gone - looking like
        /// the geometry it drove has shifted or is in the wrong place.
        /// </summary>
        private static void ApplyBlendShapeWeights(SkinnedMeshRenderer[] candidates, SkinnedMeshRenderer primary, Mesh mergedMesh)
        {
            for (var s = 0; s < mergedMesh.blendShapeCount; s++)
            {
                var name = mergedMesh.GetBlendShapeName(s);
                foreach (var renderer in candidates)
                {
                    var index = renderer.sharedMesh.GetBlendShapeIndex(name);
                    if (index < 0) continue;
                    primary.SetBlendShapeWeight(s, renderer.GetBlendShapeWeight(index));
                    break;
                }
            }
        }

        /// <summary>
        /// Blend shapes are merged by name across all source meshes. For a given name, the source
        /// mesh with the most frames provides the frame weights; each frame's deltas are built by
        /// copying every source's own deltas for that frame (if it has one) into that source's
        /// vertex range, leaving zero elsewhere. This covers the common case cleanly (a shape only
        /// exists on one source mesh) and degrades gracefully rather than crashing when the same
        /// name exists on multiple sources with mismatched frame counts.
        /// </summary>
        private static void MergeBlendShapes(
            SkinnedMeshRenderer[] candidates, int[] vertexOffsets, int totalVertexCount, Mesh mergedMesh)
        {
            var shapeNames = new List<string>();
            var seen = new HashSet<string>();
            foreach (var renderer in candidates)
            {
                var mesh = renderer.sharedMesh;
                for (var s = 0; s < mesh.blendShapeCount; s++)
                {
                    if (seen.Add(mesh.GetBlendShapeName(s))) shapeNames.Add(mesh.GetBlendShapeName(s));
                }
            }

            foreach (var shapeName in shapeNames)
            {
                var maxFrames = 0;
                List<float> frameWeights = null;
                foreach (var renderer in candidates)
                {
                    var mesh = renderer.sharedMesh;
                    var index = mesh.GetBlendShapeIndex(shapeName);
                    if (index < 0) continue;
                    var frames = mesh.GetBlendShapeFrameCount(index);
                    if (frames > maxFrames)
                    {
                        maxFrames = frames;
                        frameWeights = Enumerable.Range(0, frames)
                            .Select(f => mesh.GetBlendShapeFrameWeight(index, f)).ToList();
                    }
                }
                if (maxFrames == 0 || frameWeights == null) continue;

                for (var frame = 0; frame < maxFrames; frame++)
                {
                    var deltaVerts = new Vector3[totalVertexCount];
                    var deltaNormals = new Vector3[totalVertexCount];
                    var deltaTangents = new Vector3[totalVertexCount];

                    for (var r = 0; r < candidates.Length; r++)
                    {
                        var mesh = candidates[r].sharedMesh;
                        var index = mesh.GetBlendShapeIndex(shapeName);
                        if (index < 0 || frame >= mesh.GetBlendShapeFrameCount(index)) continue;

                        var srcVerts = new Vector3[mesh.vertexCount];
                        var srcNormals = new Vector3[mesh.vertexCount];
                        var srcTangents = new Vector3[mesh.vertexCount];
                        mesh.GetBlendShapeFrameVertices(index, frame, srcVerts, srcNormals, srcTangents);

                        Array.Copy(srcVerts, 0, deltaVerts, vertexOffsets[r], srcVerts.Length);
                        Array.Copy(srcNormals, 0, deltaNormals, vertexOffsets[r], srcNormals.Length);
                        Array.Copy(srcTangents, 0, deltaTangents, vertexOffsets[r], srcTangents.Length);
                    }

                    mergedMesh.AddBlendShapeFrame(shapeName, frameWeights[frame], deltaVerts, deltaNormals, deltaTangents);
                }
            }
        }
    }
}
