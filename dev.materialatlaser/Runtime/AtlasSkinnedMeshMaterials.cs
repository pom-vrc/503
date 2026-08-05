using System.Collections.Generic;
using UnityEngine;

namespace MaterialAtlaser.Runtime
{
    /// <summary>
    /// Which power-of-two size each generated atlas texture is capped at.
    /// </summary>
    public enum AtlasResolution
    {
        [InspectorName("1024")] Res1024 = 1024,
        [InspectorName("2048")] Res2048 = 2048,
        [InspectorName("4096")] Res4096 = 4096,
        [InspectorName("8192")] Res8192 = 8192,
    }

    /// <summary>
    /// Non-destructive material atlaser.
    ///
    /// During an NDMF build (entering Play mode, or uploading an avatar) every SkinnedMeshRenderer
    /// under this GameObject (and, optionally, every plain MeshRenderer too) has its materials'
    /// main textures packed into a small number of shared atlas textures. UVs are remapped so the
    /// meshes still sample the right pixels, submeshes that land in the same atlas are merged, and
    /// each renderer ends up with far fewer material slots than it started with - by default just
    /// one. The goal is to hand tools like d4rkAvatarOptimizer meshes/materials that are trivially
    /// mergeable, so it can combine skinned meshes and material slots much more aggressively than
    /// it otherwise could.
    ///
    /// This only atlases the main texture (typically _MainTex/_BaseMap). Normal maps and other
    /// secondary maps (metallic/gloss, occlusion, emission, detail maps, etc.) are not repacked -
    /// their UV space would no longer line up after the main texture is moved into an atlas tile,
    /// so they are dropped from the resulting atlas material rather than left pointing at stale
    /// coordinates. Materials with non-default texture tiling (mainTextureScale/Offset) are left
    /// out of the atlas entirely and keep rendering on their own submesh/material - baking tiling
    /// into a tile and wrapping the mesh's UVs back into it isn't reliable, since there's no way to
    /// tell "this mesh tiles" apart from "this mesh legitimately uses UVs outside [0,1] for an
    /// unrelated reason" from the data alone, and guessing wrong corrupts the mesh's UVs.
    ///
    /// Unlike a conservative atlaser, this one deliberately atlases across shader differences too,
    /// including opaque materials sharing a group with cutout/transparent ones (e.g. lilToon's
    /// separately-shadered Outline/Transparent/TransparentOutline variants). The atlas material's
    /// shader always comes from whichever member has the largest texture, so any other member in
    /// that group trades away whatever its own shader gave it - most visibly, a transparent/cutout
    /// material grouped with an opaque template renders opaque from then on. That's accepted on
    /// purpose in exchange for hitting the requested atlas/material count even on avatars that mix
    /// shader variants throughout.
    ///
    /// The original mesh and material assets are never modified. Processing happens on the
    /// in-memory copy of the avatar that NDMF builds, so this is safe to leave on an avatar at all
    /// times - nothing changes until you actually enter Play mode or upload.
    /// </summary>
    [AddComponentMenu("Material Atlaser/Atlas Skinned Mesh Materials")]
    [DisallowMultipleComponent]
    public class AtlasSkinnedMeshMaterials : MonoBehaviour
#if VRC_SDK_VRCSDK3 && !UDON
        , VRC.SDKBase.IEditorOnly
#endif
    {
        [Header("Scan")]
        [Tooltip("When enabled, plain (non-skinned) MeshRenderers under this GameObject are left untouched and keep their original materials. Disable to fold them into the atlas as well.")]
        [SerializeField] public bool ignoreRegularMeshes = true;

        [Header("Atlas")]
        [Tooltip("Maximum size (in pixels) of each generated atlas texture. Materials are packed as densely as they fit; if there isn't enough room they're shrunk to fit rather than overflowing.")]
        [SerializeField] public AtlasResolution atlasSize = AtlasResolution.Res2048;

        [Tooltip("How many atlas textures/materials to split the scanned materials across. 1 means every material ends up on a single shared material (one material slot), even across different shaders - a transparent/cutout material grouped with an opaque one will render opaque from then on. Raising this spreads materials over more atlases - and more material slots - to keep texture detail (and shader features) higher when there are a lot of very different materials.")]
        [Min(1)]
        [SerializeField] public int atlasCount = 1;

        [Header("Merge")]
        [Tooltip("After atlasing, combine every affected SkinnedMeshRenderer into a single SkinnedMeshRenderer sharing one merged mesh, instead of leaving the renderers separate for d4rkAvatarOptimizer (or another tool) to merge afterwards. Turn this on if d4rkAvatarOptimizer isn't picking up the merge on its own - it looks for renderers/materials that are already trivially mergeable, and this does that merge directly rather than hoping it recognizes the result. Only SkinnedMeshRenderers are combined; plain MeshRenderers (even if atlased because 'Ignore Regular Meshes' is off) are left separate. Renderers that rely on being toggled independently (e.g. via animated active-state) should not be merged, since they'd lose that independence.")]
        [SerializeField] public bool mergeSkinnedMeshesAndMaterialSlots = true;

        [Tooltip("When meshes are actually merged (2+ skinned meshes with 'Merge Skinned Meshes And Material Slots' on), this does one of two things. If it matches one of the included skinned meshes by name, merging targets that renderer directly - it survives as-is instead of whichever renderer happened to be scanned first, so anything with a direct reference to it (most importantly VRCAvatarDescriptor's eyelids/viseme 'Body' mesh) keeps working. Otherwise it's just the merged mesh's name (and also renames the surviving renderer's GameObject to match, so it shows up in the Hierarchy, not just the Mesh field in its Inspector). Leave blank to default to the first included skinned mesh's name plus \" (Merged)\", with nothing renamed.")]
        [SerializeField] public string mergedMeshName = "";

        /// <summary>
        /// SkinnedMeshRenderers found under this GameObject that are excluded from atlasing/merging,
        /// even though the automatic scan would otherwise pick them up. Managed from the "Skinned
        /// Meshes" list in the Inspector - not meant to be edited directly.
        /// </summary>
        [HideInInspector]
        [SerializeField] public List<SkinnedMeshRenderer> excludedRenderers = new List<SkinnedMeshRenderer>();
    }
}
