using System;
using System.Collections.Generic;
using System.Linq;
using MaterialAtlaser.Runtime;
using UnityEngine;

namespace MaterialAtlaser.Editors
{
    /// <summary>
    /// Does the actual atlasing work for one AtlasSkinnedMeshMaterials component. Knows nothing
    /// about NDMF - it just walks the component's transform, packs textures, remaps UVs and
    /// reassigns renderers - so it can be called from the build pass or, in principle, any other
    /// caller (an editor button, a test) without dragging NDMF along.
    /// </summary>
    internal static class MaterialAtlasProcessor
    {
        private readonly struct AtlasPlacement
        {
            public readonly int GroupIndex;
            public readonly Rect Rect;

            public AtlasPlacement(int groupIndex, Rect rect)
            {
                GroupIndex = groupIndex;
                Rect = rect;
            }
        }

        /// <summary>
        /// Every SkinnedMeshRenderer under the component's GameObject that has a mesh assigned -
        /// the full scan, before excludedRenderers is applied. Shared with the Inspector so the
        /// "Skinned Meshes" list shows exactly what the scan itself sees.
        /// </summary>
        public static SkinnedMeshRenderer[] ScanSkinnedRenderers(AtlasSkinnedMeshMaterials component)
        {
            return component.transform.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null)
                .ToArray();
        }

        /// <summary>Scanned renderers with excludedRenderers filtered out - what actually gets atlased.</summary>
        public static SkinnedMeshRenderer[] GetIncludedSkinnedRenderers(AtlasSkinnedMeshMaterials component)
        {
            var excluded = component.excludedRenderers;
            return ScanSkinnedRenderers(component)
                .Where(r => excluded == null || !excluded.Contains(r))
                .ToArray();
        }

        public static void Process(AtlasSkinnedMeshMaterials component)
        {
            var root = component.transform;

            var skinnedRenderers = GetIncludedSkinnedRenderers(component);
            var meshRenderers = component.ignoreRegularMeshes
                ? Array.Empty<MeshRenderer>()
                : root.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r => r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                    .ToArray();

            if (skinnedRenderers.Length == 0 && meshRenderers.Length == 0) return;

            var allMaterials = CollectDistinctMaterials(skinnedRenderers, meshRenderers);
            if (allMaterials.Count == 0) return;

            // Materials with non-default tiling are left out of the atlas entirely (they pass
            // through TryRemapMesh untouched, same as any material outside the scan). Baking tiling
            // into a tile and wrapping the mesh's own UVs back into it (an earlier version of this
            // tool did this with frac()) corrupts meshes whose UVs legitimately extend outside
            // [0,1] for reasons that have nothing to do with tiling - there is no reliable way to
            // tell "this is tiling" apart from "this is a deliberate multi-tile UV layout" from the
            // mesh data alone, so the safe default is to not touch it.
            var tiledMaterials = allMaterials.Where(m => !MaterialAtlasTextureUtil.HasIdentityTiling(m)).ToList();
            var atlasableMaterials = allMaterials.Where(MaterialAtlasTextureUtil.HasIdentityTiling).ToList();

            if (tiledMaterials.Count > 0)
            {
                Debug.LogWarning($"(AtlasSkinnedMeshMaterials) '{component.gameObject.name}': " +
                                  $"{tiledMaterials.Count} material(s) with non-default texture tiling " +
                                  $"({string.Join(", ", tiledMaterials.Select(m => m.name))}) were left out of the " +
                                  "atlas and keep rendering on their own submesh/material.");
            }

            if (atlasableMaterials.Count == 0) return;

            // Unlike a conservative reference-grade atlaser (which refuses to combine materials
            // that differ in shader, blend mode, etc.), this deliberately atlases across shader
            // differences too - including opaque materials sharing a group with cutout/transparent
            // ones. The atlas material's shader comes from whichever member has the largest texture
            // (see BuildAtlasForGroup), so any member using a different shader trades away whatever
            // that shader gave it - most visibly, a transparent/cutout material folded in with an
            // opaque template renders opaque. That's an accepted, intentional cost in exchange for
            // getting materials down to as few slots as `atlasCount` asks for, full stop.
            var groupCount = Mathf.Clamp(component.atlasCount, 1, atlasableMaterials.Count);
            var maxAtlasSize = (int)component.atlasSize;

            var placements = new Dictionary<Material, AtlasPlacement>();
            var atlasMaterialsList = new List<Material>();

            var groupMembers = BinPackMaterialsByTextureArea(atlasableMaterials, groupCount);
            foreach (var members in groupMembers)
            {
                if (members.Count == 0) continue;
                var groupIndex = atlasMaterialsList.Count;
                atlasMaterialsList.Add(BuildAtlasForGroup(component, members, groupIndex, maxAtlasSize, placements));
            }

            var atlasMaterialsByGroup = atlasMaterialsList.ToArray();

            var processedRenderers = 0;
            var atlasedSkinnedRenderers = new List<SkinnedMeshRenderer>();
            foreach (var smr in skinnedRenderers)
            {
                if (ProcessRenderer(smr, smr.sharedMesh, m => smr.sharedMesh = m, placements, atlasMaterialsByGroup))
                {
                    processedRenderers++;
                    atlasedSkinnedRenderers.Add(smr);
                }
            }
            foreach (var mr in meshRenderers)
            {
                var mf = mr.GetComponent<MeshFilter>();
                if (ProcessRenderer(mr, mf.sharedMesh, m => mf.sharedMesh = m, placements, atlasMaterialsByGroup))
                    processedRenderers++;
            }

            Debug.Log($"(AtlasSkinnedMeshMaterials) '{component.gameObject.name}': merged {atlasableMaterials.Count} " +
                      $"material(s) into {atlasMaterialsByGroup.Length} atlas material(s) across " +
                      $"{processedRenderers} renderer(s).");

            if (component.mergeSkinnedMeshesAndMaterialSlots)
            {
                SkinnedMeshMerger.Merge(component.gameObject.name, atlasedSkinnedRenderers.ToArray());
            }
        }

        private static List<Material> CollectDistinctMaterials(
            SkinnedMeshRenderer[] skinnedRenderers, MeshRenderer[] meshRenderers)
        {
            var distinctMaterials = new List<Material>();
            var seen = new HashSet<Material>();

            void Collect(Renderer r)
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m != null && seen.Add(m)) distinctMaterials.Add(m);
                }
            }

            foreach (var smr in skinnedRenderers) Collect(smr);
            foreach (var mr in meshRenderers) Collect(mr);
            return distinctMaterials;
        }

        /// <summary>
        /// Greedily assigns each material (largest main texture first) to whichever group currently
        /// holds the least total texture area, so groups end up roughly balanced instead of one
        /// atlas being crammed full while another sits empty.
        /// </summary>
        private static List<Material>[] BinPackMaterialsByTextureArea(List<Material> materials, int groupCount)
        {
            var groupAreas = new long[groupCount];
            var groupMembers = new List<Material>[groupCount];
            for (var i = 0; i < groupCount; i++) groupMembers[i] = new List<Material>();

            foreach (var material in materials.OrderByDescending(MaterialAtlasTextureUtil.GetMainTexturePixelArea))
            {
                var target = 0;
                for (var i = 1; i < groupCount; i++)
                {
                    if (groupAreas[i] < groupAreas[target]) target = i;
                }
                groupMembers[target].Add(material);
                groupAreas[target] += MaterialAtlasTextureUtil.GetMainTexturePixelArea(material);
            }

            return groupMembers;
        }

        /// <summary>
        /// Packs one atlas group's materials into a single atlas texture and material, and records
        /// each member's placement rect. Members are not required to share a shader - see the note
        /// in Process about why that's an accepted tradeoff here.
        /// </summary>
        private static Material BuildAtlasForGroup(
            AtlasSkinnedMeshMaterials component, List<Material> members, int groupIndex, int maxAtlasSize,
            Dictionary<Material, AtlasPlacement> placements)
        {
            // Materials that happen to share the exact same main texture (lilToon's separately-
            // shadered Opaque/Transparent/Outline variants very commonly reuse the same _MainTex)
            // only need one tile baked and packed between them, not one each - packing the same
            // pixels twice is pure wasted atlas space.
            var membersByTexture = new Dictionary<Texture, List<Material>>();
            var untexturedMembers = new List<Material>();
            foreach (var member in members)
            {
                var texture = member.mainTexture;
                if (texture == null)
                {
                    untexturedMembers.Add(member);
                    continue;
                }
                if (!membersByTexture.TryGetValue(texture, out var sharing))
                {
                    sharing = new List<Material>();
                    membersByTexture[texture] = sharing;
                }
                sharing.Add(member);
            }

            var tileOwners = membersByTexture.Values.Select(sharing => sharing[0]).Concat(untexturedMembers).ToList();

            // Pack tightly with RectanglePacker rather than Texture2D.PackTextures - measured
            // directly, PackTextures leaves large unused margins even when handed appropriately-
            // sized tiles, where RectanglePacker actually fills the space. RectanglePacker's bin
            // isn't capped to maxAtlasSize and its natural aspect ratio won't be exactly square, so
            // after packing at a rough initial scale, everything is uniformly renormalized so the
            // bin's *larger* dimension exactly touches maxAtlasSize - correcting overflow if the
            // initial guess was too generous, or filling unused headroom if it was too conservative.
            var rawSizes = tileOwners.Select(MaterialAtlasTextureUtil.GetRawTileSize).ToArray();
            var scale = MaterialAtlasTextureUtil.ComputeFitScale(rawSizes, maxAtlasSize);
            var tileSizes = rawSizes.Select(s => MaterialAtlasTextureUtil.ScaleTileSize(s, scale, maxAtlasSize * 4)).ToArray();
            var packed = RectanglePacker.Pack(tileSizes, out var binSize);

            var normalize = maxAtlasSize / (float)Mathf.Max(binSize.x, binSize.y);
            for (var i = 0; i < packed.Length; i++)
            {
                packed[i] = new RectInt(
                    Mathf.RoundToInt(packed[i].x * normalize), Mathf.RoundToInt(packed[i].y * normalize),
                    Mathf.Max(4, Mathf.RoundToInt(packed[i].width * normalize)),
                    Mathf.Max(4, Mathf.RoundToInt(packed[i].height * normalize)));
            }

            var tiles = tileOwners.Select((m, i) => MaterialAtlasTextureUtil.BuildTile(m, new Vector2Int(packed[i].width, packed[i].height))).ToArray();

            var atlasTexture = MaterialAtlasTextureUtil.CompositeAtlas(tiles, packed, maxAtlasSize);
            atlasTexture.name = $"{component.gameObject.name} Atlas {groupIndex}";

            foreach (var tile in tiles) UnityEngine.Object.DestroyImmediate(tile);

            // RectanglePacker's Y grows downward from the first tile; UV space grows upward from
            // the bottom, so the placement's top edge (packed.y) becomes the UV rect's top, and its
            // bottom edge (packed.y + packed.height) becomes the UV rect's bottom (yMin).
            var rects = packed.Select(p => new Rect(
                p.x / (float)maxAtlasSize,
                1f - (p.y + p.height) / (float)maxAtlasSize,
                p.width / (float)maxAtlasSize,
                p.height / (float)maxAtlasSize)).ToArray();

            var atlasAsset = MaterialAtlasTextureUtil.SaveAtlasAsset(atlasTexture, component.gameObject.name, groupIndex);
            UnityEngine.Object.DestroyImmediate(atlasTexture);

            // The largest-texture material in the group becomes the property/shader template for
            // the whole group's atlas material. Other members keep their own pixels (baked into the
            // atlas) but take on the template's shader and every property that differs from it -
            // including blend mode, so a transparent/cutout member templated by an opaque one
            // renders opaque from here on. Intentional; see the note in Process.
            var template = members[0];
            var atlasMaterial = new Material(template)
            {
                name = $"{component.gameObject.name} Atlas Material {groupIndex}",
            };
            atlasMaterial.mainTexture = atlasAsset;
            atlasMaterial.mainTextureScale = Vector2.one;
            atlasMaterial.mainTextureOffset = Vector2.zero;
            MaterialAtlasTextureUtil.ClearUnsupportedMaps(atlasMaterial);

            for (var i = 0; i < tileOwners.Count; i++)
            {
                var owner = tileOwners[i];
                var texture = owner.mainTexture;
                if (texture != null && membersByTexture.TryGetValue(texture, out var sharing))
                {
                    foreach (var member in sharing) placements[member] = new AtlasPlacement(groupIndex, rects[i]);
                }
                else
                {
                    placements[owner] = new AtlasPlacement(groupIndex, rects[i]);
                }
            }

            return atlasMaterial;
        }

        private static bool ProcessRenderer(
            Renderer renderer, Mesh originalMesh, Action<Mesh> assignMesh,
            Dictionary<Material, AtlasPlacement> placements, Material[] atlasMaterialsByGroup)
        {
            var materials = renderer.sharedMaterials;
            if (!TryRemapMesh(originalMesh, materials, placements, atlasMaterialsByGroup,
                    out var newMesh, out var newMaterials))
            {
                return false;
            }

            assignMesh(newMesh);
            renderer.sharedMaterials = newMaterials;
            return true;
        }

        private static bool TryRemapMesh(
            Mesh originalMesh, Material[] sourceMaterials,
            Dictionary<Material, AtlasPlacement> placements, Material[] atlasMaterialsByGroup,
            out Mesh newMesh, out Material[] newMaterials)
        {
            newMesh = null;
            newMaterials = null;
            if (originalMesh == null) return false;

            // originalUvs is read-only reference data; every triangle index gets looked up in it,
            // never mutated. remappedUvs is what gets written, and remappedVertices guards each
            // vertex index so it's transformed exactly once. Without that guard, a vertex shared by
            // multiple triangles in the same submesh - the normal case for any real mesh, since
            // adjacent triangles always share edge vertices - would have the same affine transform
            // applied once per triangle that touches it, compounding each time. For a mesh region
            // where vertices are shared by many triangles, that compounding contracts every UV in
            // the region toward a single point, which is exactly what a "flat, undetailed patch of
            // wrong color" on the rendered mesh looks like - this was the actual bug behind it.
            var originalUvs = originalMesh.uv;
            var remappedUvs = (Vector2[])originalUvs.Clone();
            var remappedVertices = new HashSet<int>();
            var trianglesByGroup = new Dictionary<int, List<int>>();
            var trianglesByPassthroughMaterial = new Dictionary<Material, List<int>>();
            var passthroughOrder = new List<Material>();
            var submeshCount = Mathf.Min(sourceMaterials.Length, originalMesh.subMeshCount);

            for (var i = 0; i < submeshCount; i++)
            {
                var material = sourceMaterials[i];
                if (material == null) continue;

                var triangles = originalMesh.GetTriangles(i);

                if (!placements.TryGetValue(material, out var placement))
                {
                    // Not part of the atlas scan (an excluded outline/utility material, or a
                    // material outside this component's scope). Keep its geometry and UVs exactly
                    // as authored, on its own submesh, rather than dropping it.
                    if (!trianglesByPassthroughMaterial.TryGetValue(material, out var passthroughList))
                    {
                        passthroughList = new List<int>();
                        trianglesByPassthroughMaterial[material] = passthroughList;
                        passthroughOrder.Add(material);
                    }
                    passthroughList.AddRange(triangles);
                    continue;
                }

                // Remap every vertex this submesh touches into its material's tile rect with a
                // plain affine transform - no fractional wrapping. Materials are guaranteed identity
                // tiling by this point (HasIdentityTiling), so the mesh's own authored UVs are used
                // as-is; wrapping them would corrupt any mesh whose UVs legitimately extend outside
                // [0,1] for non-tiling reasons. Note: if a vertex is shared between two submeshes
                // that land in different tiles (rare - material seams almost always duplicate
                // vertices in exported meshes), whichever submesh is processed first wins for it.
                foreach (var vertexIndex in triangles)
                {
                    if (!remappedVertices.Add(vertexIndex)) continue;
                    var uv = originalUvs[vertexIndex];
                    remappedUvs[vertexIndex] = new Vector2(
                        placement.Rect.xMin + uv.x * placement.Rect.width,
                        placement.Rect.yMin + uv.y * placement.Rect.height);
                }

                if (!trianglesByGroup.TryGetValue(placement.GroupIndex, out var list))
                {
                    list = new List<int>();
                    trianglesByGroup[placement.GroupIndex] = list;
                }
                list.AddRange(triangles);
            }

            if (trianglesByGroup.Count == 0 && passthroughOrder.Count == 0) return false;

            newMesh = UnityEngine.Object.Instantiate(originalMesh);
            newMesh.name = originalMesh.name + " (Atlased)";
            newMesh.uv = remappedUvs;

            var usedGroups = trianglesByGroup.Keys.OrderBy(g => g).ToList();
            newMesh.subMeshCount = usedGroups.Count + passthroughOrder.Count;
            var submeshIndex = 0;
            for (; submeshIndex < usedGroups.Count; submeshIndex++)
            {
                newMesh.SetTriangles(trianglesByGroup[usedGroups[submeshIndex]], submeshIndex);
            }
            for (var p = 0; p < passthroughOrder.Count; p++, submeshIndex++)
            {
                newMesh.SetTriangles(trianglesByPassthroughMaterial[passthroughOrder[p]], submeshIndex);
            }

            newMaterials = usedGroups.Select(g => atlasMaterialsByGroup[g]).Concat(passthroughOrder).ToArray();
            return true;
        }
    }
}
