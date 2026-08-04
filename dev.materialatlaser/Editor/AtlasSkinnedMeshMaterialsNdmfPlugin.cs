// Requires NDMF (nadena.dev.ndmf). If your project does not have NDMF installed, delete this
// one file: the runtime component and processor compile fine without it.

using System.Linq;
using MaterialAtlaser.Editors;
using MaterialAtlaser.Runtime;
using nadena.dev.ndmf;
using UnityEngine;

[assembly: ExportsPlugin(typeof(AtlasSkinnedMeshMaterialsNdmfPlugin))]

namespace MaterialAtlaser.Editors
{
    public class AtlasSkinnedMeshMaterialsNdmfPlugin : Plugin<AtlasSkinnedMeshMaterialsNdmfPlugin>
    {
        public override string QualifiedName => "dev.materialatlaser.AtlasSkinnedMeshMaterials";
        public override string DisplayName => "Material Atlaser - Atlas Skinned Mesh Materials";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                // Run after polygon-reducing tools so the final geometry gets atlased (not
                // geometry that's about to be cut away), and before mesh/material mergers so they
                // see already-atlased, and therefore trivially mergeable, materials. Both AfterPlugin
                // calls are no-ops if those plugins aren't installed.
                .AfterPlugin("dev.hai-vr.prefabulous.universal.DeletePolygons")
                .AfterPlugin("dev.shapekeydecimator.DecimatePolygonsByShapeKey")
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run("Atlas Skinned Mesh Materials", AtlasMaterials);
        }

        private void AtlasMaterials(BuildContext context)
        {
            var components = context.AvatarRootTransform
                .GetComponentsInChildren<AtlasSkinnedMeshMaterials>(true)
                .ToList();
            if (components.Count == 0) return;

            foreach (var component in components)
            {
                if (component == null) continue;
                MaterialAtlasProcessor.Process(component);
            }

            foreach (var component in components)
            {
                if (component != null) Object.DestroyImmediate(component);
            }
        }
    }
}
