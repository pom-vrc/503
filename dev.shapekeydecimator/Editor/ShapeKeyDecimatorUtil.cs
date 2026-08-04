using System;
using System.Collections.Generic;
using System.Linq;
using ShapeKeyDecimator.Runtime;
using UnityEngine;

namespace ShapeKeyDecimator.Editors
{
    /// <summary>
    /// Shared, dependency-free helpers: which vertices a blend shape moves, how many triangles sit
    /// inside that region, and which renderers a component applies to.
    /// </summary>
    public static class ShapeKeyDecimatorUtil
    {
        /// <summary>Positions closer than this are treated as the same point when welding.</summary>
        public const float WeldEpsilon = 1e-5f;

        /// <summary>
        /// The targets a component addresses. Always explicit: the component never searches the
        /// hierarchy, so it can only ever touch meshes the user assigned by hand.
        /// </summary>
        public static List<DecimationTarget> FindTargets(DecimatePolygonsByShapeKey component)
        {
            var result = new List<DecimationTarget>();
            if (component == null) return result;

            var seen = new HashSet<Renderer>();

            if (component.renderers != null)
            {
                foreach (var skinned in component.renderers)
                {
                    if (skinned == null || skinned.sharedMesh == null) continue;
                    if (!seen.Add(skinned)) continue;
                    result.Add(DecimationTarget.For(skinned));
                }
            }

            if (component.meshRenderers != null)
            {
                foreach (var meshRenderer in component.meshRenderers)
                {
                    if (meshRenderer == null) continue;
                    if (!seen.Add(meshRenderer)) continue;
                    var target = DecimationTarget.For(meshRenderer);
                    if (target != null) result.Add(target);
                }
            }

            return result;
        }

        public static List<string> GetBlendShapeNames(Mesh mesh)
        {
            var result = new List<string>(mesh.blendShapeCount);
            for (var i = 0; i < mesh.blendShapeCount; i++) result.Add(mesh.GetBlendShapeName(i));
            return result;
        }

        /// <summary>
        /// Marks every vertex that any frame of the given blend shape displaces further than
        /// <paramref name="threshold"/>. The result is then propagated across welded duplicates so
        /// that UV/normal seam copies of the same point are treated consistently.
        /// </summary>
        public static bool[] ComputeAffectedVertices(Mesh mesh, string blendShapeName, float threshold, int[] vertToGroup = null)
        {
            var vertexCount = mesh.vertexCount;
            var affected = new bool[vertexCount];

            var shapeIndex = mesh.GetBlendShapeIndex(blendShapeName);
            if (shapeIndex < 0) return affected;

            var deltaVerts = new Vector3[vertexCount];
            var deltaNorms = new Vector3[vertexCount];
            var deltaTans = new Vector3[vertexCount];

            var sqrThreshold = threshold * threshold;
            var frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
            for (var frame = 0; frame < frameCount; frame++)
            {
                mesh.GetBlendShapeFrameVertices(shapeIndex, frame, deltaVerts, deltaNorms, deltaTans);
                for (var i = 0; i < vertexCount; i++)
                {
                    if (!affected[i] && deltaVerts[i].sqrMagnitude > sqrThreshold) affected[i] = true;
                }
            }

            if (vertToGroup != null) PropagateAcrossWelds(affected, vertToGroup);
            return affected;
        }

        /// <summary>Any welded group with at least one affected member becomes fully affected.</summary>
        public static void PropagateAcrossWelds(bool[] affected, int[] vertToGroup)
        {
            var groupAffected = new HashSet<int>();
            for (var i = 0; i < affected.Length; i++)
            {
                if (affected[i]) groupAffected.Add(vertToGroup[i]);
            }
            for (var i = 0; i < affected.Length; i++)
            {
                if (!affected[i] && groupAffected.Contains(vertToGroup[i])) affected[i] = true;
            }
        }

        /// <summary>
        /// Groups vertices that share a position (within <see cref="WeldEpsilon"/>). Returns a
        /// per-vertex group id; group ids are dense and start at 0.
        /// </summary>
        public static int[] WeldVertices(Vector3[] positions, out int groupCount)
        {
            var vertToGroup = new int[positions.Length];
            var lookup = new Dictionary<Vector3Int, int>(positions.Length);
            var next = 0;

            const float inv = 1f / WeldEpsilon;
            for (var i = 0; i < positions.Length; i++)
            {
                var p = positions[i];
                // Exact integer key, so two vertices are only welded when they quantize to the
                // same cell. No hash collisions can merge unrelated points.
                var key = new Vector3Int(
                    Mathf.RoundToInt(p.x * inv),
                    Mathf.RoundToInt(p.y * inv),
                    Mathf.RoundToInt(p.z * inv));

                if (lookup.TryGetValue(key, out var group))
                {
                    vertToGroup[i] = group;
                }
                else
                {
                    lookup.Add(key, next);
                    vertToGroup[i] = next;
                    next++;
                }
            }

            groupCount = next;
            return vertToGroup;
        }

        /// <summary>Total triangles across every submesh with triangle topology.</summary>
        public static int CountTriangles(Mesh mesh)
        {
            var total = 0;
            for (var i = 0; i < mesh.subMeshCount; i++)
            {
                if (mesh.GetTopology(i) != MeshTopology.Triangles) continue;
                total += (int)(mesh.GetIndexCount(i) / 3);
            }
            return total;
        }

        /// <summary>
        /// Triangles that lie entirely inside the affected region. These are the only triangles a
        /// region pass can remove, so this is the number the inspector reports per shape key.
        /// </summary>
        public static int CountTrianglesInRegion(Mesh mesh, bool[] affected)
        {
            var count = 0;
            for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                if (mesh.GetTopology(submesh) != MeshTopology.Triangles) continue;
                var indices = mesh.GetIndices(submesh);
                for (var i = 0; i + 2 < indices.Length; i += 3)
                {
                    if (affected[indices[i]] && affected[indices[i + 1]] && affected[indices[i + 2]]) count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Resolves the entries of every component that applies to <paramref name="smr"/> into
        /// decimation regions on that renderer's mesh. Returns null when nothing applies.
        /// </summary>
        public static List<ShapeKeyMeshDecimator.Region> BuildRegionsFor(
            DecimationTarget target,
            IEnumerable<DecimatePolygonsByShapeKey> components,
            out DecimateSettings settings)
        {
            settings = DecimateSettings.Default;

            var mesh = target.SharedMesh;
            if (mesh == null) return null;

            // A plain mesh has no shape keys to select regions with, so only the whole-mesh pass
            // can ever apply to it.
            var allowShapeKeys = target.SupportsShapeKeys;
            var available = allowShapeKeys ? GetBlendShapeNames(mesh) : new List<string>();

            // Only needed once a shape key region is actually requested; a whole-mesh-only setup
            // should not pay for welding twice.
            int[] vertToGroup = null;

            var regions = new List<ShapeKeyMeshDecimator.Region>();
            var seen = new Dictionary<string, int>();
            var any = false;
            var wholeMeshStrength = 0f;

            foreach (var component in components)
            {
                if (component == null) continue;
                if (!target.IsTargetedBy(component)) continue;

                if (!any)
                {
                    settings = DecimateSettings.From(component);
                    any = true;
                }

                if (component.wholeMeshActive)
                {
                    wholeMeshStrength = Mathf.Max(wholeMeshStrength, Mathf.Clamp01(component.wholeMeshStrength));
                }

                if (!allowShapeKeys || component.shapeKeys == null) continue;
                foreach (var entry in component.shapeKeys)
                {
                    if (entry == null || !entry.active) continue;
                    if (string.IsNullOrEmpty(entry.blendShape)) continue;
                    if (entry.strength <= 0f) continue;
                    if (!available.Contains(entry.blendShape)) continue;

                    // The same shape key listed by two components: keep the stronger request.
                    if (seen.TryGetValue(entry.blendShape, out var existingIndex))
                    {
                        if (regions[existingIndex].strength >= entry.strength) continue;
                        var replaced = regions[existingIndex];
                        replaced.strength = entry.strength;
                        regions[existingIndex] = replaced;
                        continue;
                    }

                    if (vertToGroup == null) vertToGroup = WeldVertices(mesh.vertices, out _);
                    var affected = ComputeAffectedVertices(mesh, entry.blendShape, component.deltaThreshold, vertToGroup);
                    seen.Add(entry.blendShape, regions.Count);
                    regions.Add(new ShapeKeyMeshDecimator.Region
                    {
                        name = entry.blendShape,
                        strength = Mathf.Clamp01(entry.strength),
                        affectedVertices = affected
                    });
                }
            }

            // The whole-mesh pass always goes last, so the targeted shape key reductions land on the
            // original topology and the general pass then thins out whatever is left.
            if (wholeMeshStrength > 0f)
            {
                regions.Add(new ShapeKeyMeshDecimator.Region
                {
                    name = WholeMeshRegionName,
                    strength = wholeMeshStrength,
                    affectedVertices = null   // null means "every vertex"
                });
            }

            return regions.Count == 0 ? null : regions;
        }

        public const string WholeMeshRegionName = "<whole mesh>";

        public static string FormatCount(int value)
        {
            return value.ToString("N0");
        }
    }

    /// <summary>
    /// One thing to decimate, hiding the difference between a skinned mesh and a plain one.
    ///
    /// A plain mesh has no blend shapes, so it has no shape key regions to select and can only be
    /// touched by the whole-mesh pass. <see cref="SupportsShapeKeys"/> is how the rest of the code
    /// knows that without special-casing types everywhere.
    /// </summary>
    public class DecimationTarget
    {
        public Renderer renderer;
        public SkinnedMeshRenderer skinned;      // null for a plain mesh
        public MeshRenderer meshRenderer;        // null for a skinned mesh
        public MeshFilter filter;                // where a plain mesh actually stores its Mesh

        /// <summary>
        /// Recorded at construction rather than inferred from a null check. A destroyed component
        /// compares equal to null in Unity, so testing <c>skinned != null</c> later would silently
        /// send a skinned target down the plain-mesh path and dereference a null filter.
        /// </summary>
        private bool _isSkinned;

        public bool SupportsShapeKeys => _isSkinned;
        public GameObject GameObject => renderer.gameObject;
        public Transform Transform => renderer.transform;
        public string Name => renderer.name;

        /// <summary>False once the underlying objects have been deleted from the scene.</summary>
        public bool IsAlive => renderer != null && (_isSkinned ? skinned != null : filter != null);

        public static DecimationTarget For(SkinnedMeshRenderer skinned)
        {
            if (skinned == null || skinned.sharedMesh == null) return null;
            return new DecimationTarget { renderer = skinned, skinned = skinned, _isSkinned = true };
        }

        /// <summary>
        /// Returns null when the object has no MeshFilter or no mesh, since a MeshRenderer on its own
        /// has nothing to decimate.
        /// </summary>
        public static DecimationTarget For(MeshRenderer meshRenderer)
        {
            if (meshRenderer == null) return null;

            var filter = meshRenderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return null;

            return new DecimationTarget
            {
                renderer = meshRenderer,
                meshRenderer = meshRenderer,
                filter = filter,
                _isSkinned = false
            };
        }

        public Mesh SharedMesh
        {
            get
            {
                if (_isSkinned) return skinned != null ? skinned.sharedMesh : null;
                return filter != null ? filter.sharedMesh : null;
            }
            set
            {
                if (_isSkinned)
                {
                    if (skinned != null) skinned.sharedMesh = value;
                }
                else if (filter != null)
                {
                    filter.sharedMesh = value;
                }
            }
        }

        public bool RendererEnabled
        {
            get => renderer != null && renderer.enabled;
            set
            {
                if (renderer != null) renderer.enabled = value;
            }
        }

        /// <summary>True when this component lists this exact renderer.</summary>
        public bool IsTargetedBy(DecimatePolygonsByShapeKey component)
        {
            if (component == null) return false;

            if (_isSkinned)
            {
                return component.renderers != null && component.renderers.Contains(skinned);
            }
            return component.meshRenderers != null && component.meshRenderers.Contains(meshRenderer);
        }

        /// <summary>Blend shape weights are index based, so they are captured by name and restored by name.</summary>
        public Dictionary<string, float> CaptureBlendShapeWeights()
        {
            var weights = new Dictionary<string, float>();
            if (!_isSkinned || skinned == null) return weights;

            var mesh = skinned.sharedMesh;
            if (mesh == null) return weights;

            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                weights[mesh.GetBlendShapeName(i)] = skinned.GetBlendShapeWeight(i);
            }
            return weights;
        }

        public void ApplyBlendShapeWeights(Dictionary<string, float> weights)
        {
            if (!_isSkinned || skinned == null) return;

            var mesh = skinned.sharedMesh;
            if (mesh == null) return;

            for (var i = 0; i < mesh.blendShapeCount; i++)
            {
                if (weights.TryGetValue(mesh.GetBlendShapeName(i), out var weight))
                {
                    skinned.SetBlendShapeWeight(i, weight);
                }
            }
        }

        /// <summary>Rebinds this target onto the equivalent components of a duplicated GameObject.</summary>
        public DecimationTarget RebindTo(GameObject copy)
        {
            if (_isSkinned)
            {
                var copiedSkinned = copy.GetComponent<SkinnedMeshRenderer>();
                return copiedSkinned == null
                    ? null
                    : new DecimationTarget { renderer = copiedSkinned, skinned = copiedSkinned, _isSkinned = true };
            }

            var copiedRenderer = copy.GetComponent<MeshRenderer>();
            var copiedFilter = copy.GetComponent<MeshFilter>();
            if (copiedRenderer == null || copiedFilter == null) return null;

            return new DecimationTarget
            {
                renderer = copiedRenderer,
                meshRenderer = copiedRenderer,
                filter = copiedFilter,
                _isSkinned = false
            };
        }
    }

    /// <summary>Knobs shared by the build-time pass and the editor preview.</summary>
    public struct DecimateSettings
    {
        public bool protectRegionBoundary;
        public bool preserveBorders;
        public bool preserveSubmeshBoundaries;
        public bool preserveUvSeams;
        public float uvWeight;
        public float maxNormalDeviation;

        public static DecimateSettings Default => new DecimateSettings
        {
            protectRegionBoundary = true,
            preserveBorders = true,
            preserveSubmeshBoundaries = true,
            preserveUvSeams = true,
            uvWeight = 1f,
            maxNormalDeviation = 100f
        };

        public static DecimateSettings From(DecimatePolygonsByShapeKey component)
        {
            return new DecimateSettings
            {
                protectRegionBoundary = component.protectRegionBoundary,
                preserveBorders = component.preserveBorders,
                preserveSubmeshBoundaries = component.preserveSubmeshBoundaries,
                preserveUvSeams = component.preserveUvSeams,
                uvWeight = component.uvWeight,
                maxNormalDeviation = component.maxNormalDeviation
            };
        }
    }
}
