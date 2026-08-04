using System.Collections.Generic;
using System.Linq;
using ShapeKeyDecimator.Runtime;
using UnityEngine;

namespace ShapeKeyDecimator.Editors
{
    /// <summary>
    /// The actual work, shared by the NDMF build pass and the "create decimated copy" button.
    /// Knows nothing about NDMF, so this file compiles in any project.
    /// </summary>
    public static class ShapeKeyDecimationProcessor
    {
        public class RendererResult
        {
            public DecimationTarget target;
            public Mesh mesh;
            public ShapeKeyMeshDecimator.Report report;
        }

        /// <summary>
        /// Decimates every target that at least one of the given components applies to.
        /// Does not touch the renderers; the caller decides what to do with the produced meshes.
        /// </summary>
        public static List<RendererResult> Process(
            IEnumerable<DecimationTarget> candidateTargets,
            IList<DecimatePolygonsByShapeKey> components,
            bool logToConsole = true)
        {
            var results = new List<RendererResult>();
            if (components == null || components.Count == 0) return results;

            foreach (var target in candidateTargets)
            {
                // A deferred rebuild can run after the user deleted the object it was queued for.
                if (target == null || !target.IsAlive || target.SharedMesh == null) continue;

                var regions = ShapeKeyDecimatorUtil.BuildRegionsFor(target, components, out var settings);
                if (regions == null) continue;

                Mesh newMesh;
                ShapeKeyMeshDecimator.Report report;
                try
                {
                    newMesh = ShapeKeyMeshDecimator.Decimate(target.SharedMesh, regions, settings, out report);
                }
                catch (System.Exception exception)
                {
                    Debug.LogError($"[Shape Key Decimator] Failed on '{target.Name}': {exception}", target.renderer);
                    continue;
                }

                if (logToConsole)
                {
                    var perRegion = string.Join(", ", report.regions
                        .Select(r => $"{r.name} {r.trianglesRemoved}/{r.trianglesInRegion} @ {r.strength:0.##}"));
                    Debug.Log(
                        $"[Shape Key Decimator] {target.Name}: {report.originalTriangles:N0} -> {report.newTriangles:N0} tris " +
                        $"({report.TrianglesRemoved:N0} removed), {report.originalVertices:N0} -> {report.newVertices:N0} verts. [{perRegion}]",
                        target.renderer);
                }

                results.Add(new RendererResult { target = target, mesh = newMesh, report = report });
            }

            return results;
        }

        /// <summary>
        /// Every target explicitly assigned to any of the components, deduplicated. Targets are
        /// always explicit, so a component can never reach a mesh the user did not hand it.
        /// </summary>
        public static List<DecimationTarget> CollectCandidateTargets(
            IList<DecimatePolygonsByShapeKey> components)
        {
            var result = new List<DecimationTarget>();
            var seen = new HashSet<Renderer>();

            foreach (var component in components)
            {
                if (component == null) continue;
                foreach (var target in ShapeKeyDecimatorUtil.FindTargets(component))
                {
                    if (seen.Add(target.renderer)) result.Add(target);
                }
            }

            return result;
        }
    }

    public class RegionMeasurement
    {
        public int trianglesInRegion;
        public int trianglesRemoved;
    }

    /// <summary>Exact counts from a real decimation run, aggregated across renderers.</summary>
    public class MeasuredDecimation
    {
        public int settingsHash;
        public int originalTriangles;
        public int newTriangles;
        public readonly Dictionary<string, RegionMeasurement> regions = new Dictionary<string, RegionMeasurement>();

        public int TrianglesRemoved => originalTriangles - newTriangles;
    }

    /// <summary>
    /// Cache of measured results, keyed by component and validated by a settings hash.
    ///
    /// This exists because the inspector's fast estimate is only a projection of intent: it
    /// multiplies region triangles by the slider and cannot know how many collapses the validity
    /// rules will actually reject. Whenever a real pass has run for the current settings — because a
    /// preview is live, or the user pressed Measure — the true numbers are shown instead.
    /// </summary>
    public static class ShapeKeyDecimationMeasurements
    {
        private static readonly Dictionary<int, MeasuredDecimation> Store = new Dictionary<int, MeasuredDecimation>();

        public static void Record(
            DecimatePolygonsByShapeKey component,
            int settingsHash,
            IEnumerable<ShapeKeyDecimationProcessor.RendererResult> results)
        {
            if (component == null) return;

            var measured = new MeasuredDecimation { settingsHash = settingsHash };

            foreach (var result in results)
            {
                if (result?.report == null) continue;

                measured.originalTriangles += result.report.originalTriangles;
                measured.newTriangles += result.report.newTriangles;

                foreach (var region in result.report.regions)
                {
                    if (region.name == null) continue;
                    if (!measured.regions.TryGetValue(region.name, out var entry))
                    {
                        entry = new RegionMeasurement();
                        measured.regions.Add(region.name, entry);
                    }
                    entry.trianglesInRegion += region.trianglesInRegion;
                    entry.trianglesRemoved += region.trianglesRemoved;
                }
            }

            Store[component.GetInstanceID()] = measured;
        }

        /// <summary>Returns the measurement for these exact settings, or null if there isn't one.</summary>
        public static MeasuredDecimation Get(DecimatePolygonsByShapeKey component, int settingsHash)
        {
            if (component == null) return null;
            if (!Store.TryGetValue(component.GetInstanceID(), out var measured)) return null;
            return measured.settingsHash == settingsHash ? measured : null;
        }

        public static void Clear(DecimatePolygonsByShapeKey component)
        {
            if (component != null) Store.Remove(component.GetInstanceID());
        }

        public static void ClearAll()
        {
            Store.Clear();
        }
    }
}
