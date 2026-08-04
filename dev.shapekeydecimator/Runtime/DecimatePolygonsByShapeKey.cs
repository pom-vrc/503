using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShapeKeyDecimator.Runtime
{
    /// <summary>
    /// One shape key selected for decimation, with the strength of the reduction applied to the
    /// region of the mesh that shape key moves.
    /// </summary>
    [Serializable]
    public class ShapeKeyDecimationTarget
    {
        /// <summary>Name of the blend shape whose affected vertices define the decimation region.</summary>
        public string blendShape;

        /// <summary>0 = no decimation, 1 = maximum decimation of that region.</summary>
        [Range(0f, 1f)] public float strength = 0.5f;

        /// <summary>Allows temporarily disabling an entry without losing its slider value.</summary>
        public bool active = true;
    }

    /// <summary>
    /// Non-destructive polygon decimation driven by shape keys.
    ///
    /// The component itself does nothing at runtime. During an NDMF build (entering Play mode, or
    /// uploading an avatar) the vertices moved by each listed blend shape are collected, and the
    /// triangles inside that region are reduced by the entry's strength using quadric error metric
    /// edge collapses. A whole-mesh pass can then reduce everything that is left. The original mesh
    /// asset is never modified.
    ///
    /// Blend shapes are preserved: collapses only ever remove a vertex and re-index its triangles
    /// onto a surviving neighbour, so every remaining blend shape delta stays exactly as authored.
    /// </summary>
    [AddComponentMenu("Shape Key Decimator/503 Decimate Polys By Shape Key")]
    [DisallowMultipleComponent]
    public class DecimatePolygonsByShapeKey : MonoBehaviour
#if VRC_SDK_VRCSDK3 && !UDON
        , VRC.SDKBase.IEditorOnly
#endif
    {
        [Tooltip("Skinned meshes to decimate. These can use both shape key regions and the whole mesh slider.")]
        [SerializeField] public SkinnedMeshRenderer[] renderers = Array.Empty<SkinnedMeshRenderer>();

        [Tooltip("Plain meshes to decimate. These have no shape keys, so only the whole mesh slider applies to them.")]
        [SerializeField] public MeshRenderer[] meshRenderers = Array.Empty<MeshRenderer>();

        [SerializeField] public List<ShapeKeyDecimationTarget> shapeKeys = new List<ShapeKeyDecimationTarget>();

        [Header("Whole Mesh")]
        [Tooltip("Decimation applied to the entire mesh after every shape key region has been processed. 0 = no decimation, 1 = maximum.")]
        [Range(0f, 1f)] [SerializeField] public float wholeMeshStrength;

        [Tooltip("Allows temporarily disabling the whole-mesh pass without losing its slider value.")]
        [SerializeField] public bool wholeMeshActive = true;

        [Header("Region")]
        [Tooltip("A vertex counts as part of the region when any blend shape frame moves it further than this distance. Raise it to ignore near-zero deltas that some exporters leave behind.")]
        [SerializeField] public float deltaThreshold = 0.0001f;

        [Tooltip("Never collapse vertices that sit on the edge of the region, so the seam between decimated and untouched geometry keeps its original shape.")]
        [SerializeField] public bool protectRegionBoundary = true;

        [Header("Quality")]
        [Tooltip("Keep vertices on open mesh borders (hems, cuffs, eyelid openings) unless the collapse runs along the border itself.")]
        [SerializeField] public bool preserveBorders = true;

        [Tooltip("Do not collapse a vertex onto one that belongs to a different submesh, so material boundaries stay put.")]
        [SerializeField] public bool preserveSubmeshBoundaries = true;

        [Tooltip("Lock UV seams so a seam vertex can only slide along its own seam, never inward. Turning this off allows more reduction but lets seams tear and smear the texture.")]
        [SerializeField] public bool preserveUvSeams = true;

        [Tooltip("How strongly UV stretching counts against a collapse. 0 ignores UVs entirely (geometry-only, fastest degradation of textures), 1 balances texture error against shape error, higher values protect the texture at the cost of silhouette accuracy.")]
        [Range(0f, 8f)] [SerializeField] public float uvWeight = 1f;

        [Tooltip("Reject a collapse when it would rotate any surrounding triangle by more than this many degrees. Lower values are safer, higher values decimate further.")]
        [Range(0f, 179f)] [SerializeField] public float maxNormalDeviation = 100f;

        /// <summary>
        /// Renderers that preview mode disabled. Preview objects themselves are never written to the
        /// scene, so if the editor is interrupted hard enough that they vanish without a teardown
        /// (a crash, a forced reload) this list is what lets the originals be switched back on.
        /// Not shown in the inspector; managed entirely by the preview manager.
        /// </summary>
        [HideInInspector] [SerializeField]
        public List<Renderer> previewDisabledRenderers = new List<Renderer>();

        public void SanitizeInEditor()
        {
            if (shapeKeys == null) shapeKeys = new List<ShapeKeyDecimationTarget>();
            if (renderers == null) renderers = Array.Empty<SkinnedMeshRenderer>();
            if (meshRenderers == null) meshRenderers = Array.Empty<MeshRenderer>();
            if (previewDisabledRenderers == null) previewDisabledRenderers = new List<Renderer>();
        }
    }

    /// <summary>
    /// Attached to a temporary preview duplicate. Everything needed to undo a preview lives here
    /// rather than in editor-side state, so a preview can always be found and torn down by scanning
    /// the scene — no matter what happened to the editor in between.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public class ShapeKeyDecimatorPreviewMarker : MonoBehaviour
#if VRC_SDK_VRCSDK3 && !UDON
        , VRC.SDKBase.IEditorOnly
#endif
    {
        public DecimatePolygonsByShapeKey owner;
        public Renderer original;
        public bool reEnableOriginalOnTeardown;
        public Mesh generatedMesh;

        /// <summary>Settings hash the preview was built from, used to detect staleness.</summary>
        public int builtHash;
    }
}
