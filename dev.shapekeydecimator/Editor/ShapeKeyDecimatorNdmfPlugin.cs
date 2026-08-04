// Requires NDMF (nadena.dev.ndmf). If your project does not have NDMF installed, delete this one
// file: everything else, including the "Create decimated copy" button, compiles without it.

using System.Linq;
using nadena.dev.ndmf;
using ShapeKeyDecimator.Editors;
using ShapeKeyDecimator.Runtime;
using UnityEngine;

[assembly: ExportsPlugin(typeof(ShapeKeyDecimatorNdmfPlugin))]

namespace ShapeKeyDecimator.Editors
{
    // If your NDMF version supports it and you build for multiple platforms, you can add
    // [RunsOnAllPlatforms] here. It is left off so this compiles against older NDMF releases.
    public class ShapeKeyDecimatorNdmfPlugin : Plugin<ShapeKeyDecimatorNdmfPlugin>
    {
        public override string QualifiedName => "dev.shapekeydecimator.DecimatePolygonsByShapeKey";
        public override string DisplayName => "Shape Key Decimator - Decimate Polygons By Shape Key";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                // Run before mesh optimizers so they see (and can further shrink) the reduced mesh.
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run("Decimate Polygons By Shape Key", DecimatePolygons);
        }

        private void DecimatePolygons(BuildContext context)
        {
            var components = context.AvatarRootTransform
                .GetComponentsInChildren<DecimatePolygonsByShapeKey>(true)
                .ToList();
            if (components.Count == 0) return;

            var candidates = ShapeKeyDecimationProcessor.CollectCandidateTargets(components);
            if (candidates.Count == 0)
            {
                foreach (var component in components)
                {
                    if (component != null) Object.DestroyImmediate(component);
                }
                return;
            }

            var results = ShapeKeyDecimationProcessor.Process(candidates, components);

            foreach (var result in results)
            {
                result.mesh.name = result.target.SharedMesh.name + " (Decimated)";
                result.target.SharedMesh = result.mesh;

                // Blend shape weights are index-based; names and order are preserved by the
                // decimator, so existing weights and animations keep working. Nothing to remap.
            }

            foreach (var component in components)
            {
                if (component != null) Object.DestroyImmediate(component);
            }
        }
    }
}
