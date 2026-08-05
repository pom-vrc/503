// Requires NDMF (nadena.dev.ndmf). If your project does not have NDMF installed, delete this
// one file: the runtime component and processor compile fine without it.

using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using ZeroScaleBoneCutter.Editors;
using ZeroScaleBoneCutter.Runtime;

[assembly: ExportsPlugin(typeof(ZeroScaleBoneCutterNdmfPlugin))]

namespace ZeroScaleBoneCutter.Editors
{
    public class ZeroScaleBoneCutterNdmfPlugin : Plugin<ZeroScaleBoneCutterNdmfPlugin>
    {
        public override string QualifiedName => "dev.zeroscalecutter.ZeroScaleBoneCutter";
        public override string DisplayName => "Zero Scale Bone Cutter";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                // Cutting away zero-scale-bone geometry first means the decimator and atlaser (and
                // whatever d4rk/AAO do afterwards) all work on the already-trimmed mesh. Both
                // AfterPlugin/BeforePlugin calls are no-ops if that plugin isn't installed.
                .AfterPlugin("dev.hai-vr.prefabulous.universal.DeletePolygons")
                .BeforePlugin("dev.shapekeydecimator.DecimatePolygonsByShapeKey")
                .BeforePlugin("dev.materialatlaser.AtlasSkinnedMeshMaterials")
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run("Cut Zero Scale Bone Mesh Portions", CutMeshes);
        }

        private void CutMeshes(BuildContext context)
        {
            var components = context.AvatarRootTransform
                .GetComponentsInChildren<ZeroScaleBoneMeshCutter>(true)
                .ToList();
            if (components.Count == 0) return;

            foreach (var component in components)
            {
                if (component == null) continue;
                ZeroScaleBoneCutterProcessor.Process(component);
            }

            foreach (var component in components)
            {
                if (component != null) Object.DestroyImmediate(component);
            }
        }
    }
}
