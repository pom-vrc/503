using UnityEngine;

namespace ZeroScaleBoneCutter.Runtime
{
    /// <summary>
    /// Non-destructive zero-scale bone mesh cutter.
    ///
    /// Scaling a bone to zero is a common trick for permanently hiding part of a skinned mesh (an
    /// alternate clothing layer, a removed accessory) without deleting geometry by hand. The mesh
    /// data is still there, still being skinned and rendered every frame - it just collapses to a
    /// point. This component finds every SkinnedMeshRenderer under it, and for any that reference a
    /// zero-scale bone, removes the triangles that bone drives from the mesh entirely.
    ///
    /// During an NDMF build (entering Play mode, or uploading an avatar) this runs automatically.
    /// The original mesh asset is never modified - a new mesh is built and assigned in its place.
    /// </summary>
    [AddComponentMenu("Zero Scale Bone Cutter/Zero Scale Bone Mesh Cutter")]
    [DisallowMultipleComponent]
    public class ZeroScaleBoneMeshCutter : MonoBehaviour
#if VRC_SDK_VRCSDK3 && !UDON
        , VRC.SDKBase.IEditorOnly
#endif
    {
        [Tooltip("When off (default), a triangle is only cut when every one of its vertices is entirely weighted to zero-scale bones - a vertex that still carries any weight on a surviving bone is left alone, so a boundary that blends into the rest of the mesh stays closed instead of tearing open a hole. Turn this on to remove anything touching a zero-scale bone at all, even partially weighted - more thorough, but can expose a hole at a hard (non-blended) boundary.")]
        [SerializeField] public bool aggressiveRemoval = false;
    }
}
