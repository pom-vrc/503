using System.Linq;
using UnityEngine;
using ZeroScaleBoneCutter.Runtime;

namespace ZeroScaleBoneCutter.Editors
{
    /// <summary>
    /// Shared entry point used by both the NDMF build pass and the "bake" button - finds every
    /// SkinnedMeshRenderer under the component and cuts the ones that reference a zero-scale bone.
    /// </summary>
    internal static class ZeroScaleBoneCutterProcessor
    {
        public static SkinnedMeshRenderer[] ScanSkinnedRenderers(ZeroScaleBoneMeshCutter component)
        {
            return component.transform.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh != null)
                .ToArray();
        }

        public static SkinnedMeshRenderer[] GetAffectedRenderers(ZeroScaleBoneMeshCutter component)
        {
            return ScanSkinnedRenderers(component).Where(ZeroScaleBoneCutterCore.HasZeroScaleBones).ToArray();
        }

        public static void Process(ZeroScaleBoneMeshCutter component)
        {
            foreach (var renderer in GetAffectedRenderers(component))
            {
                var result = ZeroScaleBoneCutterCore.Cut(renderer, component.aggressiveRemoval);
                if (result.trianglesRemoved == 0)
                {
                    Object.DestroyImmediate(result.mesh);
                    continue;
                }

                renderer.sharedMesh = result.mesh;
                Debug.Log($"(ZeroScaleBoneMeshCutter) '{component.gameObject.name}': removed {result.trianglesRemoved} " +
                          $"triangle(s) / {result.verticesRemoved} vertex(es) from '{renderer.name}' driven by zero-scale bones.");
            }
        }
    }
}
