using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShapeKeyDecimator.Editors
{
    /// <summary>
    /// Quadric error metric decimator restricted to one or more vertex regions.
    ///
    /// It performs *half-edge* collapses: a vertex is removed and its triangles are re-indexed onto
    /// a surviving neighbour, and the survivor never moves. That has three properties that matter a
    /// lot for avatar meshes:
    ///
    ///   - every surviving vertex keeps its authored UVs, bone weights, colours and normals, so no
    ///     attribute interpolation artefacts and no texture swimming;
    ///   - every blend shape survives untouched, because deltas are simply dropped for removed
    ///     vertices and copied verbatim for the rest;
    ///   - geometry outside the region cannot move, so the decimated area stitches back into the
    ///     untouched mesh exactly.
    ///
    /// Collapse order is driven by the classic Garland-Heckbert quadric error, so flat areas are
    /// simplified long before curved ones.
    /// </summary>
    public class ShapeKeyMeshDecimator
    {
        public struct Region
        {
            public string name;
            /// <summary>0 = untouched, 1 = collapse the region as far as topology allows.</summary>
            public float strength;
            /// <summary>Per source-vertex flag: is this vertex part of the region.</summary>
            public bool[] affectedVertices;
        }

        public class RegionReport
        {
            public string name;
            public float strength;
            public int trianglesInRegion;
            public int trianglesRemoved;
        }

        public class Report
        {
            public int originalTriangles;
            public int newTriangles;
            public int originalVertices;
            public int newVertices;
            public readonly List<RegionReport> regions = new List<RegionReport>();
            public int TrianglesRemoved => originalTriangles - newTriangles;
        }

        // ---------------------------------------------------------------- public entry point

        public static Mesh Decimate(Mesh source, IList<Region> regions, DecimateSettings settings, out Report report)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            // Note: no isReadable gate. Mesh.isReadable reflects the importer's Read/Write setting,
            // which only restricts access in a player build. Decimation always runs inside the
            // Editor (NDMF build or the copy button), where the asset data is available.
            var instance = new ShapeKeyMeshDecimator(source, settings);
            return instance.Run(regions, out report);
        }

        // ---------------------------------------------------------------- state

        private readonly Mesh _source;
        private readonly DecimateSettings _settings;

        private readonly int _vertexCount;
        private readonly Vector3[] _positions;
        private readonly Vector3[] _normals;
        private readonly List<Vector4> _uv0 = new List<Vector4>();

        private int _groupCount;
        private readonly int[] _vertToGroup;
        private Vector3[] _groupPos;
        private List<int>[] _groupOriginalVerts;
        private List<int>[] _groupTris;
        private List<int>[] _groupNeighbors;
        private Quadric[] _quadrics;
        private int[] _parent;
        private bool[] _groupAlive;
        private int[] _version;
        private bool[] _groupOnBorder;
        private int[] _groupSubmeshMask;
        private HashSet<long> _borderEdges;

        private bool _hasUvs;
        private Vector2[] _groupUv;          // mean UV per group, used for the UV cost term
        private int[] _groupUvCornerCount;   // distinct UVs sharing this position
        private bool[] _groupOnUvSeam;
        private HashSet<long> _uvSeamEdges;
        private float _uvCostScale;          // converts UV distance into world-space-comparable units

        private int[] _triG;   // current welded-group indices, mutated by collapses
        private int[] _triV;   // original vertex indices, never mutated
        private bool[] _triAlive;
        private int _triangleCount;

        private SubMeshRecord[] _subMeshes;

        private readonly List<Candidate> _heap = new List<Candidate>();

        private class SubMeshRecord
        {
            public MeshTopology topology;
            public List<int> triangles;   // indices into the _tri* arrays
            public int[] rawIndices;      // used for non-triangle topologies
        }

        private ShapeKeyMeshDecimator(Mesh source, DecimateSettings settings)
        {
            _source = source;
            _settings = settings;
            _vertexCount = source.vertexCount;
            _positions = source.vertices;
            _normals = source.normals;
            source.GetUVs(0, _uv0);
            _hasUvs = _uv0.Count == _vertexCount;

            // The geometric quadric error is in squared world units. Multiplying squared UV
            // distance by the square of this puts the UV term in the same units, so the two can be
            // summed and the weight stays meaningful across differently scaled avatars.
            var extent = source.bounds.size.magnitude;
            _uvCostScale = extent > 1e-6f ? extent : 1f;

            _vertToGroup = ShapeKeyDecimatorUtil.WeldVertices(_positions, out _groupCount);
        }

        private Vector2 Uv(int vertex)
        {
            var uv = _uv0[vertex];
            return new Vector2(uv.x, uv.y);
        }

        private Mesh Run(IList<Region> regions, out Report report)
        {
            BuildTopology();

            report = new Report
            {
                originalTriangles = _triangleCount,
                originalVertices = _vertexCount
            };

            if (regions != null)
            {
                var groupMasks = new bool[regions.Count][];
                for (var r = 0; r < regions.Count; r++)
                {
                    groupMasks[r] = ToGroupMask(regions[r].affectedVertices);
                }

                for (var r = 0; r < regions.Count; r++)
                {
                    var region = regions[r];
                    var entry = new RegionReport
                    {
                        name = region.name,
                        strength = region.strength
                    };
                    entry.trianglesInRegion = CountAliveTrianglesIn(groupMasks[r]);
                    entry.trianglesRemoved = RunRegion(groupMasks[r], region.strength, entry.trianglesInRegion);
                    report.regions.Add(entry);
                }
            }

            var mesh = BuildMesh(out var newTriangleCount, out var newVertexCount);
            report.newTriangles = newTriangleCount;
            report.newVertices = newVertexCount;
            return mesh;
        }

        // ---------------------------------------------------------------- topology construction

        private void BuildTopology()
        {
            _groupPos = new Vector3[_groupCount];
            _groupOriginalVerts = new List<int>[_groupCount];
            _groupTris = new List<int>[_groupCount];
            _groupNeighbors = new List<int>[_groupCount];
            _quadrics = new Quadric[_groupCount];
            _parent = new int[_groupCount];
            _groupAlive = new bool[_groupCount];
            _version = new int[_groupCount];
            _groupOnBorder = new bool[_groupCount];
            _groupSubmeshMask = new int[_groupCount];
            _groupUv = new Vector2[_groupCount];
            _groupUvCornerCount = new int[_groupCount];
            _groupOnUvSeam = new bool[_groupCount];
            _uvSeamEdges = new HashSet<long>();

            for (var g = 0; g < _groupCount; g++)
            {
                _groupOriginalVerts[g] = new List<int>(2);
                _groupTris[g] = new List<int>(6);
                _groupNeighbors[g] = new List<int>(6);
                _parent[g] = g;
                _groupAlive[g] = true;
            }

            for (var v = 0; v < _vertexCount; v++)
            {
                var g = _vertToGroup[v];
                if (_groupOriginalVerts[g].Count == 0) _groupPos[g] = _positions[v];
                _groupOriginalVerts[g].Add(v);
            }

            ComputeGroupUvs();

            // Gather triangles across all triangle-topology submeshes.
            var triV = new List<int>();
            var triG = new List<int>();
            var triSub = new List<int>();
            _subMeshes = new SubMeshRecord[_source.subMeshCount];

            for (var submesh = 0; submesh < _source.subMeshCount; submesh++)
            {
                var topology = _source.GetTopology(submesh);
                var indices = _source.GetIndices(submesh);
                var record = new SubMeshRecord { topology = topology };
                _subMeshes[submesh] = record;

                if (topology != MeshTopology.Triangles)
                {
                    record.rawIndices = indices;
                    continue;
                }

                record.triangles = new List<int>(indices.Length / 3);
                for (var i = 0; i + 2 < indices.Length; i += 3)
                {
                    var a = indices[i];
                    var b = indices[i + 1];
                    var c = indices[i + 2];
                    record.triangles.Add(triV.Count / 3);
                    triV.Add(a); triV.Add(b); triV.Add(c);
                    triG.Add(_vertToGroup[a]); triG.Add(_vertToGroup[b]); triG.Add(_vertToGroup[c]);
                    triSub.Add(submesh);
                }
            }

            _triV = triV.ToArray();
            _triG = triG.ToArray();
            _triangleCount = triSub.Count;
            _triAlive = new bool[_triangleCount];
            for (var t = 0; t < _triangleCount; t++) _triAlive[t] = true;

            var submeshBit = new int[_triangleCount];
            for (var t = 0; t < _triangleCount; t++)
            {
                submeshBit[t] = 1 << Mathf.Min(triSub[t], 31);
            }

            // Per-group triangle lists, quadrics, submesh masks.
            var edgeTriangleCount = new Dictionary<long, int>(_triangleCount * 2);
            var seenEdges = new HashSet<long>();
            var edgeUvSeen = new Dictionary<long, EdgeUv>(_hasUvs ? _triangleCount * 2 : 0);

            for (var t = 0; t < _triangleCount; t++)
            {
                var i0 = t * 3;
                var ga = _triG[i0];
                var gb = _triG[i0 + 1];
                var gc = _triG[i0 + 2];

                _groupTris[ga].Add(t);
                if (gb != ga) _groupTris[gb].Add(t);
                if (gc != ga && gc != gb) _groupTris[gc].Add(t);

                _groupSubmeshMask[ga] |= submeshBit[t];
                _groupSubmeshMask[gb] |= submeshBit[t];
                _groupSubmeshMask[gc] |= submeshBit[t];

                var pa = _groupPos[ga];
                var pb = _groupPos[gb];
                var pc = _groupPos[gc];
                var normal = Vector3.Cross(pb - pa, pc - pa);
                var area = normal.magnitude;
                if (area > 1e-12f)
                {
                    normal /= area;
                    var d = -Vector3.Dot(normal, pa);
                    // Area weighting makes large flat faces dominate, which is what we want.
                    var plane = Quadric.FromPlane(normal.x, normal.y, normal.z, d, area);
                    _quadrics[ga].Add(plane);
                    _quadrics[gb].Add(plane);
                    _quadrics[gc].Add(plane);
                }

                AccumulateEdge(edgeTriangleCount, seenEdges, ga, gb);
                AccumulateEdge(edgeTriangleCount, seenEdges, gb, gc);
                AccumulateEdge(edgeTriangleCount, seenEdges, gc, ga);

                if (_hasUvs)
                {
                    AccumulateUvEdge(edgeUvSeen, t, 0, 1);
                    AccumulateUvEdge(edgeUvSeen, t, 1, 2);
                    AccumulateUvEdge(edgeUvSeen, t, 2, 0);
                }
            }

            foreach (var key in _uvSeamEdges)
            {
                DecodeEdge(key, out var a, out var b);
                _groupOnUvSeam[a] = true;
                _groupOnUvSeam[b] = true;
            }

            _borderEdges = new HashSet<long>();
            foreach (var pair in edgeTriangleCount)
            {
                if (pair.Value != 1) continue;
                _borderEdges.Add(pair.Key);
                DecodeEdge(pair.Key, out var a, out var b);
                _groupOnBorder[a] = true;
                _groupOnBorder[b] = true;
            }
        }

        private struct EdgeUv
        {
            public Vector2 lo;
            public Vector2 hi;
        }

        /// <summary>
        /// Mean UV per welded group, and how many distinct UVs share that position. A count above
        /// one means the position is a UV seam: several texture-space corners occupy the same point
        /// in 3D, which is exactly the configuration a naive collapse zips shut.
        /// </summary>
        private void ComputeGroupUvs()
        {
            if (!_hasUvs) return;

            for (var g = 0; g < _groupCount; g++)
            {
                var members = _groupOriginalVerts[g];
                var sum = Vector2.zero;
                var distinct = 0;

                for (var i = 0; i < members.Count; i++)
                {
                    var uv = Uv(members[i]);
                    sum += uv;

                    var isNew = true;
                    for (var j = 0; j < i; j++)
                    {
                        if (!ApproximatelyEqual(Uv(members[j]), uv)) continue;
                        isNew = false;
                        break;
                    }
                    if (isNew) distinct++;
                }

                _groupUv[g] = members.Count > 0 ? sum / members.Count : Vector2.zero;
                _groupUvCornerCount[g] = distinct;
            }
        }

        /// <summary>
        /// An edge is a UV seam when two triangles sharing it disagree about the UVs at its
        /// endpoints — the parameterisation is discontinuous across it.
        /// </summary>
        private void AccumulateUvEdge(Dictionary<long, EdgeUv> seen, int triangle, int cornerA, int cornerB)
        {
            var i0 = triangle * 3;
            var groupA = _triG[i0 + cornerA];
            var groupB = _triG[i0 + cornerB];
            if (groupA == groupB) return;

            var uvA = Uv(_triV[i0 + cornerA]);
            var uvB = Uv(_triV[i0 + cornerB]);

            // Order the pair by group id so both triangles describe the edge the same way.
            var current = groupA < groupB
                ? new EdgeUv { lo = uvA, hi = uvB }
                : new EdgeUv { lo = uvB, hi = uvA };

            var key = EdgeKey(groupA, groupB);
            if (!seen.TryGetValue(key, out var previous))
            {
                seen.Add(key, current);
                return;
            }

            if (!ApproximatelyEqual(previous.lo, current.lo) || !ApproximatelyEqual(previous.hi, current.hi))
            {
                _uvSeamEdges.Add(key);
            }
        }

        private static bool ApproximatelyEqual(Vector2 a, Vector2 b)
        {
            const float epsilon = 1e-6f;
            return (a - b).sqrMagnitude <= epsilon * epsilon;
        }

        private void AccumulateEdge(Dictionary<long, int> counts, HashSet<long> alreadyLinked, int a, int b)
        {
            if (a == b) return;
            var key = EdgeKey(a, b);
            counts.TryGetValue(key, out var existing);
            counts[key] = existing + 1;

            // Neighbour lists only need each undirected edge once.
            if (alreadyLinked.Add(key))
            {
                _groupNeighbors[a].Add(b);
                _groupNeighbors[b].Add(a);
            }
        }

        private static long EdgeKey(int a, int b)
        {
            var lo = Math.Min(a, b);
            var hi = Math.Max(a, b);
            return ((long)lo << 32) | (uint)hi;
        }

        private static void DecodeEdge(long key, out int a, out int b)
        {
            a = (int)(key >> 32);
            b = (int)(key & 0xFFFFFFFFL);
        }

        private bool[] ToGroupMask(bool[] affectedVertices)
        {
            var mask = new bool[_groupCount];

            // A null mask means the whole mesh. Boundary protection then has nothing to protect,
            // since no group has a neighbour outside the region, but border and submesh protection
            // still apply.
            if (affectedVertices == null)
            {
                for (var g = 0; g < _groupCount; g++) mask[g] = true;
                return mask;
            }

            var limit = Mathf.Min(affectedVertices.Length, _vertexCount);
            for (var v = 0; v < limit; v++)
            {
                if (affectedVertices[v]) mask[_vertToGroup[v]] = true;
            }
            return mask;
        }

        // ---------------------------------------------------------------- region pass

        private int CountAliveTrianglesIn(bool[] mask)
        {
            var count = 0;
            for (var t = 0; t < _triangleCount; t++)
            {
                if (!_triAlive[t]) continue;
                var i0 = t * 3;
                if (mask[_triG[i0]] && mask[_triG[i0 + 1]] && mask[_triG[i0 + 2]]) count++;
            }
            return count;
        }

        private int RunRegion(bool[] mask, float strength, int trianglesInRegion)
        {
            strength = Mathf.Clamp01(strength);
            if (strength <= 0f || trianglesInRegion == 0) return 0;

            var budget = Mathf.RoundToInt(trianglesInRegion * strength);
            if (budget <= 0) return 0;

            var eligible = new bool[_groupCount];
            for (var g = 0; g < _groupCount; g++) RefreshEligibility(eligible, mask, g);

            _heap.Clear();
            for (var g = 0; g < _groupCount; g++)
            {
                if (!eligible[g]) continue;
                var neighbors = _groupNeighbors[g];
                for (var i = 0; i < neighbors.Count; i++)
                {
                    var n = neighbors[i];
                    if (n != g && _groupAlive[n]) TryPush(g, n);
                }
            }

            var removed = 0;
            var guard = 0;
            var maxIterations = trianglesInRegion * 8 + 1024;

            while (removed < budget && _heap.Count > 0 && guard++ < maxIterations)
            {
                var candidate = Pop();
                var from = candidate.from;
                var to = candidate.to;

                if (!_groupAlive[from] || !_groupAlive[to]) continue;
                if (_version[from] != candidate.versionFrom || _version[to] != candidate.versionTo) continue;
                if (!eligible[from]) continue;
                if (!IsValidCollapse(from, to)) continue;

                removed += Collapse(from, to);

                eligible[from] = false;
                RefreshEligibility(eligible, mask, to);

                var neighbors = _groupNeighbors[to];
                for (var i = 0; i < neighbors.Count; i++)
                {
                    var n = neighbors[i];
                    if (n == to || !_groupAlive[n]) continue;
                    RefreshEligibility(eligible, mask, n);
                    if (eligible[n]) TryPush(n, to);
                    if (eligible[to]) TryPush(to, n);
                }
            }

            return removed;
        }

        /// <summary>
        /// A group can be removed when it is inside the region and, if boundary protection is on,
        /// when none of its neighbours sit outside the region.
        /// </summary>
        private void RefreshEligibility(bool[] eligible, bool[] mask, int g)
        {
            if (!_groupAlive[g] || !mask[g])
            {
                eligible[g] = false;
                return;
            }

            if (!_settings.protectRegionBoundary)
            {
                eligible[g] = true;
                return;
            }

            var neighbors = _groupNeighbors[g];
            for (var i = 0; i < neighbors.Count; i++)
            {
                var n = neighbors[i];
                if (n == g || !_groupAlive[n]) continue;
                if (!mask[n])
                {
                    eligible[g] = false;
                    return;
                }
            }

            eligible[g] = true;
        }

        private bool IsValidCollapse(int from, int to)
        {
            if (_settings.preserveSubmeshBoundaries && _groupSubmeshMask[from] != _groupSubmeshMask[to])
            {
                return false;
            }

            if (_settings.preserveBorders && _groupOnBorder[from])
            {
                if (!_groupOnBorder[to]) return false;
                if (!_borderEdges.Contains(EdgeKey(from, to))) return false;
            }

            if (_hasUvs && _settings.preserveUvSeams)
            {
                // A seam vertex may only slide along its own seam, never into the interior. Pulling
                // it inward is what merges two texture-space corners into one and tears the seam.
                if (_groupOnUvSeam[from])
                {
                    if (!_groupOnUvSeam[to]) return false;
                    if (!_uvSeamEdges.Contains(EdgeKey(from, to))) return false;
                }

                // Even along a seam, the survivor must have at least as many distinct UV corners,
                // otherwise several corners of `from` would have to share one of `to`'s.
                if (_groupUvCornerCount[from] > _groupUvCornerCount[to]) return false;
            }

            return !WouldFlipNormals(from, to);
        }

        private bool WouldFlipNormals(int from, int to)
        {
            var target = _groupPos[to];
            var minDot = Mathf.Cos(Mathf.Clamp(_settings.maxNormalDeviation, 0f, 179f) * Mathf.Deg2Rad);

            var tris = _groupTris[from];
            for (var i = 0; i < tris.Count; i++)
            {
                var t = tris[i];
                if (!_triAlive[t]) continue;
                var i0 = t * 3;
                var ga = _triG[i0];
                var gb = _triG[i0 + 1];
                var gc = _triG[i0 + 2];
                if (ga != from && gb != from && gc != from) continue;   // stale list entry
                if (ga == to || gb == to || gc == to) continue;          // triangle will be deleted

                var oa = _groupPos[ga];
                var ob = _groupPos[gb];
                var oc = _groupPos[gc];
                var na = ga == from ? target : oa;
                var nb = gb == from ? target : ob;
                var nc = gc == from ? target : oc;

                var oldNormal = Vector3.Cross(ob - oa, oc - oa);
                if (oldNormal.sqrMagnitude < 1e-18f) continue;
                var newNormal = Vector3.Cross(nb - na, nc - na);
                if (newNormal.sqrMagnitude < 1e-18f) return true;

                if (Vector3.Dot(oldNormal.normalized, newNormal.normalized) < minDot) return true;
            }

            return false;
        }

        private int Collapse(int from, int to)
        {
            var killed = 0;
            var tris = _groupTris[from];
            for (var i = 0; i < tris.Count; i++)
            {
                var t = tris[i];
                if (!_triAlive[t]) continue;
                var i0 = t * 3;
                var ga = _triG[i0];
                var gb = _triG[i0 + 1];
                var gc = _triG[i0 + 2];
                if (ga != from && gb != from && gc != from) continue;

                if (ga == to || gb == to || gc == to)
                {
                    _triAlive[t] = false;
                    killed++;
                    continue;
                }

                if (ga == from) _triG[i0] = to;
                if (gb == from) _triG[i0 + 1] = to;
                if (gc == from) _triG[i0 + 2] = to;
                _groupTris[to].Add(t);
            }
            tris.Clear();

            _quadrics[to].Add(_quadrics[from]);
            _groupOnBorder[to] = _groupOnBorder[to] || _groupOnBorder[from];
            _groupSubmeshMask[to] |= _groupSubmeshMask[from];

            if (_hasUvs)
            {
                // The survivor now carries whatever seam ran through the removed vertex, so it must
                // stay locked for the rest of the run.
                _groupOnUvSeam[to] = _groupOnUvSeam[to] || _groupOnUvSeam[from];
            }

            var fromNeighbors = _groupNeighbors[from];
            for (var i = 0; i < fromNeighbors.Count; i++)
            {
                var n = fromNeighbors[i];
                if (n == to || n == from || !_groupAlive[n]) continue;
                AddNeighbor(to, n);
                AddNeighbor(n, to);
            }
            fromNeighbors.Clear();

            _groupAlive[from] = false;
            _parent[from] = to;
            _version[to]++;
            return killed;
        }

        private void AddNeighbor(int g, int n)
        {
            var list = _groupNeighbors[g];
            list.Add(n);
            if (list.Count <= 96) return;

            var distinct = new HashSet<int>();
            var write = 0;
            for (var i = 0; i < list.Count; i++)
            {
                var value = list[i];
                if (value == g || !_groupAlive[value]) continue;
                if (!distinct.Add(value)) continue;
                list[write++] = value;
            }
            list.RemoveRange(write, list.Count - write);
        }

        private int Find(int g)
        {
            var root = g;
            while (_parent[root] != root) root = _parent[root];
            while (_parent[g] != root)
            {
                var next = _parent[g];
                _parent[g] = root;
                g = next;
            }
            return root;
        }

        // ---------------------------------------------------------------- candidate heap

        private struct Candidate
        {
            public float cost;
            public int from;
            public int to;
            public int versionFrom;
            public int versionTo;
        }

        private void TryPush(int from, int to)
        {
            if (from == to) return;
            if (!_groupAlive[from] || !_groupAlive[to]) return;

            var merged = _quadrics[from];
            merged.Add(_quadrics[to]);
            var target = _groupPos[to];
            var error = merged.Evaluate(target.x, target.y, target.z);
            if (error < 0d) error = 0d;

            // Tiny length term breaks ties towards short edges, which keeps triangle shape sane
            // on perfectly flat areas where every quadric error is zero.
            var cost = (float)error + (target - _groupPos[from]).magnitude * 1e-6f;

            // The removed vertex's corners inherit the survivor's UVs, so the UV displacement along
            // this edge *is* the texture error the collapse introduces. Without this term the queue
            // is blind to stretching and will happily flatten a texture-dense area first.
            if (_hasUvs && _settings.uvWeight > 0f)
            {
                var uvDelta = (_groupUv[to] - _groupUv[from]).sqrMagnitude;
                cost += _settings.uvWeight * uvDelta * _uvCostScale * _uvCostScale;
            }

            Push(new Candidate
            {
                cost = cost,
                from = from,
                to = to,
                versionFrom = _version[from],
                versionTo = _version[to]
            });
        }

        private void Push(Candidate candidate)
        {
            _heap.Add(candidate);
            var child = _heap.Count - 1;
            while (child > 0)
            {
                var parent = (child - 1) / 2;
                if (_heap[parent].cost <= _heap[child].cost) break;
                (_heap[parent], _heap[child]) = (_heap[child], _heap[parent]);
                child = parent;
            }
        }

        private Candidate Pop()
        {
            var result = _heap[0];
            var last = _heap.Count - 1;
            _heap[0] = _heap[last];
            _heap.RemoveAt(last);

            var parent = 0;
            while (true)
            {
                var left = parent * 2 + 1;
                var right = left + 1;
                var smallest = parent;
                if (left < _heap.Count && _heap[left].cost < _heap[smallest].cost) smallest = left;
                if (right < _heap.Count && _heap[right].cost < _heap[smallest].cost) smallest = right;
                if (smallest == parent) break;
                (_heap[parent], _heap[smallest]) = (_heap[smallest], _heap[parent]);
                parent = smallest;
            }

            return result;
        }

        // ---------------------------------------------------------------- mesh reconstruction

        private int[] BuildVertexMap()
        {
            var map = new int[_vertexCount];
            var buckets = new Dictionary<int, List<int>>();

            for (var v = 0; v < _vertexCount; v++)
            {
                var g = _vertToGroup[v];
                var root = Find(g);
                if (root == g)
                {
                    map[v] = v;
                    continue;
                }

                if (!buckets.TryGetValue(root, out var list))
                {
                    list = new List<int>();
                    buckets.Add(root, list);
                }
                list.Add(v);
            }

            foreach (var bucket in buckets) AssignRemovedVertices(bucket.Key, bucket.Value, map);
            return map;
        }

        /// <summary>
        /// A welded group holds several vertices when the mesh has a UV or normal seam there. When
        /// removed vertices are folded into a surviving group, corners that share a UV may share a
        /// target, but corners with *different* UVs must not — that is precisely what tears a seam.
        /// So distinct UVs are assigned distinct survivor corners wherever the survivor has enough
        /// of them.
        /// </summary>
        private void AssignRemovedVertices(int survivingGroup, List<int> removed, int[] map)
        {
            var candidates = _groupOriginalVerts[survivingGroup];
            if (candidates.Count == 1)
            {
                for (var i = 0; i < removed.Count; i++) map[removed[i]] = candidates[0];
                return;
            }

            var hasNormals = _normals != null && _normals.Length == _vertexCount;

            var resolvedUvs = new List<Vector2>();
            var resolvedTargets = new List<int>();
            var taken = new HashSet<int>();

            for (var i = 0; i < removed.Count; i++)
            {
                var vertex = removed[i];
                var uv = _hasUvs ? Uv(vertex) : Vector2.zero;

                // Same texture-space corner as one we already placed: reuse that decision.
                var reused = -1;
                if (_hasUvs)
                {
                    for (var r = 0; r < resolvedUvs.Count; r++)
                    {
                        if (!ApproximatelyEqual(resolvedUvs[r], uv)) continue;
                        reused = resolvedTargets[r];
                        break;
                    }
                }

                if (reused >= 0)
                {
                    map[vertex] = reused;
                    continue;
                }

                var best = -1;
                var bestScore = float.MaxValue;
                var bestIncludingTaken = candidates[0];
                var bestTakenScore = float.MaxValue;

                for (var c = 0; c < candidates.Count; c++)
                {
                    var candidate = candidates[c];
                    var score = 0f;
                    if (_hasUvs) score += (Uv(candidate) - uv).sqrMagnitude * 10f;
                    if (hasNormals) score += 1f - Vector3.Dot(_normals[candidate], _normals[vertex]);

                    if (score < bestTakenScore)
                    {
                        bestTakenScore = score;
                        bestIncludingTaken = candidate;
                    }

                    if (taken.Contains(candidate) || score >= bestScore) continue;
                    bestScore = score;
                    best = candidate;
                }

                // Every corner already claimed: fall back to the closest one rather than dropping
                // the triangle. The corner-count rule in IsValidCollapse makes this rare.
                var target = best >= 0 ? best : bestIncludingTaken;
                map[vertex] = target;
                taken.Add(target);
                resolvedUvs.Add(uv);
                resolvedTargets.Add(target);
            }
        }

        private Mesh BuildMesh(out int newTriangleCount, out int newVertexCount)
        {
            var vertexMap = BuildVertexMap();

            // Remap and drop degenerate triangles, submesh by submesh.
            var newIndices = new int[_subMeshes.Length][];
            var used = new bool[_vertexCount];
            newTriangleCount = 0;

            for (var submesh = 0; submesh < _subMeshes.Length; submesh++)
            {
                var record = _subMeshes[submesh];
                if (record.topology != MeshTopology.Triangles)
                {
                    var raw = new int[record.rawIndices.Length];
                    for (var i = 0; i < raw.Length; i++)
                    {
                        var mapped = vertexMap[record.rawIndices[i]];
                        raw[i] = mapped;
                        used[mapped] = true;
                    }
                    newIndices[submesh] = raw;
                    continue;
                }

                var kept = new List<int>(record.triangles.Count * 3);
                for (var i = 0; i < record.triangles.Count; i++)
                {
                    var t = record.triangles[i];
                    if (!_triAlive[t]) continue;
                    var i0 = t * 3;
                    var a = vertexMap[_triV[i0]];
                    var b = vertexMap[_triV[i0 + 1]];
                    var c = vertexMap[_triV[i0 + 2]];
                    if (a == b || b == c || a == c) continue;
                    kept.Add(a); kept.Add(b); kept.Add(c);
                    used[a] = true; used[b] = true; used[c] = true;
                    newTriangleCount++;
                }
                newIndices[submesh] = kept.ToArray();
            }

            // Compact the vertex buffer, keeping the original ordering.
            var oldToNew = new int[_vertexCount];
            var keepList = new List<int>(_vertexCount);
            for (var v = 0; v < _vertexCount; v++)
            {
                if (!used[v])
                {
                    oldToNew[v] = -1;
                    continue;
                }
                oldToNew[v] = keepList.Count;
                keepList.Add(v);
            }
            var keep = keepList.ToArray();
            newVertexCount = keep.Length;

            for (var submesh = 0; submesh < newIndices.Length; submesh++)
            {
                var indices = newIndices[submesh];
                for (var i = 0; i < indices.Length; i++) indices[i] = oldToNew[indices[i]];
            }

            return AssembleMesh(keep, newIndices, newVertexCount);
        }

        private Mesh AssembleMesh(int[] keep, int[][] newIndices, int newVertexCount)
        {
            var mesh = new Mesh { name = _source.name };
            mesh.indexFormat = newVertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            mesh.SetVertices(Gather(_positions, keep));

            if (_normals != null && _normals.Length == _vertexCount) mesh.SetNormals(Gather(_normals, keep));

            var tangents = _source.tangents;
            if (tangents != null && tangents.Length == _vertexCount) mesh.SetTangents(Gather(tangents, keep));

            if (_source.HasVertexAttribute(VertexAttribute.Color))
            {
                if (_source.GetVertexAttributeFormat(VertexAttribute.Color) == VertexAttributeFormat.UNorm8)
                {
                    var colors32 = _source.colors32;
                    if (colors32.Length == _vertexCount) mesh.colors32 = Gather(colors32, keep);
                }
                else
                {
                    var colors = _source.colors;
                    if (colors.Length == _vertexCount) mesh.colors = Gather(colors, keep);
                }
            }

            CopyUVs(mesh, keep);
            CopyBoneWeights(mesh, keep);
            mesh.bindposes = _source.bindposes;

            mesh.subMeshCount = newIndices.Length;
            for (var submesh = 0; submesh < newIndices.Length; submesh++)
            {
                mesh.SetIndices(newIndices[submesh], _subMeshes[submesh].topology, submesh, false);
            }

            CopyBlendShapes(mesh, keep);

            mesh.RecalculateBounds();
            return mesh;
        }

        private void CopyUVs(Mesh mesh, int[] keep)
        {
            for (var channel = 0; channel < 8; channel++)
            {
                var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                if (!_source.HasVertexAttribute(attribute)) continue;

                var dimension = _source.GetVertexAttributeDimension(attribute);
                switch (dimension)
                {
                    case 2:
                    {
                        var values = new List<Vector2>();
                        _source.GetUVs(channel, values);
                        if (values.Count == _vertexCount) mesh.SetUVs(channel, Gather(values, keep));
                        break;
                    }
                    case 3:
                    {
                        var values = new List<Vector3>();
                        _source.GetUVs(channel, values);
                        if (values.Count == _vertexCount) mesh.SetUVs(channel, Gather(values, keep));
                        break;
                    }
                    default:
                    {
                        var values = new List<Vector4>();
                        _source.GetUVs(channel, values);
                        if (values.Count == _vertexCount) mesh.SetUVs(channel, Gather(values, keep));
                        break;
                    }
                }
            }
        }

        private void CopyBoneWeights(Mesh mesh, int[] keep)
        {
            var bonesPerVertex = _source.GetBonesPerVertex();
            if (bonesPerVertex.Length != _vertexCount) return;

            var allWeights = _source.GetAllBoneWeights();

            var offsets = new int[_vertexCount];
            var running = 0;
            for (var v = 0; v < _vertexCount; v++)
            {
                offsets[v] = running;
                running += bonesPerVertex[v];
            }

            var newBonesPerVertex = new byte[keep.Length];
            var total = 0;
            for (var i = 0; i < keep.Length; i++)
            {
                var count = bonesPerVertex[keep[i]];
                newBonesPerVertex[i] = count;
                total += count;
            }

            var newWeights = new BoneWeight1[total];
            var write = 0;
            for (var i = 0; i < keep.Length; i++)
            {
                var v = keep[i];
                var count = bonesPerVertex[v];
                for (var k = 0; k < count; k++) newWeights[write++] = allWeights[offsets[v] + k];
            }

            var bpvNative = new NativeArray<byte>(newBonesPerVertex, Allocator.Temp);
            var weightsNative = new NativeArray<BoneWeight1>(newWeights, Allocator.Temp);
            try
            {
                mesh.SetBoneWeights(bpvNative, weightsNative);
            }
            finally
            {
                bpvNative.Dispose();
                weightsNative.Dispose();
            }
        }

        private void CopyBlendShapes(Mesh mesh, int[] keep)
        {
            if (_source.blendShapeCount == 0) return;

            var deltaVerts = new Vector3[_vertexCount];
            var deltaNorms = new Vector3[_vertexCount];
            var deltaTans = new Vector3[_vertexCount];

            for (var shape = 0; shape < _source.blendShapeCount; shape++)
            {
                var name = _source.GetBlendShapeName(shape);
                var frames = _source.GetBlendShapeFrameCount(shape);
                for (var frame = 0; frame < frames; frame++)
                {
                    var weight = _source.GetBlendShapeFrameWeight(shape, frame);
                    _source.GetBlendShapeFrameVertices(shape, frame, deltaVerts, deltaNorms, deltaTans);
                    mesh.AddBlendShapeFrame(
                        name,
                        weight,
                        Gather(deltaVerts, keep),
                        Gather(deltaNorms, keep),
                        Gather(deltaTans, keep));
                }
            }
        }

        private static T[] Gather<T>(T[] source, int[] keep)
        {
            var result = new T[keep.Length];
            for (var i = 0; i < keep.Length; i++) result[i] = source[keep[i]];
            return result;
        }

        private static T[] Gather<T>(List<T> source, int[] keep)
        {
            var result = new T[keep.Length];
            for (var i = 0; i < keep.Length; i++) result[i] = source[keep[i]];
            return result;
        }

        // ---------------------------------------------------------------- quadric

        /// <summary>
        /// Symmetric 4x4 quadric stored as its 10 unique coefficients (Garland-Heckbert 1997).
        /// </summary>
        private struct Quadric
        {
            private double _m0, _m1, _m2, _m3, _m4, _m5, _m6, _m7, _m8, _m9;

            public static Quadric FromPlane(double a, double b, double c, double d, double weight)
            {
                return new Quadric
                {
                    _m0 = a * a * weight, _m1 = a * b * weight, _m2 = a * c * weight, _m3 = a * d * weight,
                    _m4 = b * b * weight, _m5 = b * c * weight, _m6 = b * d * weight,
                    _m7 = c * c * weight, _m8 = c * d * weight,
                    _m9 = d * d * weight
                };
            }

            public void Add(Quadric other)
            {
                _m0 += other._m0; _m1 += other._m1; _m2 += other._m2; _m3 += other._m3;
                _m4 += other._m4; _m5 += other._m5; _m6 += other._m6;
                _m7 += other._m7; _m8 += other._m8;
                _m9 += other._m9;
            }

            public double Evaluate(double x, double y, double z)
            {
                return _m0 * x * x + 2 * _m1 * x * y + 2 * _m2 * x * z + 2 * _m3 * x
                       + _m4 * y * y + 2 * _m5 * y * z + 2 * _m6 * y
                       + _m7 * z * z + 2 * _m8 * z
                       + _m9;
            }
        }
    }
}
