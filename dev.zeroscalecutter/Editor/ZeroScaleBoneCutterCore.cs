using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroScaleBoneCutter.Editors
{
    /// <summary>
    /// Pure mesh math: which vertices are driven by zero-scale bones, and rebuilding a mesh with
    /// the triangles that touch them removed. Operates on one SkinnedMeshRenderer at a time.
    /// </summary>
    internal static class ZeroScaleBoneCutterCore
    {
        private const float ScaleEpsilon = 0.0001f;

        public class Result
        {
            public Mesh mesh;
            public int trianglesRemoved;
            public int verticesRemoved;
        }

        public static bool HasZeroScaleBones(SkinnedMeshRenderer renderer)
        {
            var bones = renderer.bones;
            if (bones == null) return false;
            foreach (var bone in bones)
            {
                if (bone == null || bone.lossyScale.magnitude < ScaleEpsilon) return true;
            }
            return false;
        }

        /// <summary>
        /// Removes the mesh portion driven by zero-scale bones. A vertex is marked for removal
        /// when, depending on <paramref name="aggressiveRemoval"/>:
        ///   - off (conservative): every one of its weight slots points at a zero-scale bone (no
        ///     surviving weight at all) - a vertex that's even partially still held up by a real
        ///     bone is left in place, so blended boundaries stay closed.
        ///   - on (aggressive): any weight slot at all points at a zero-scale bone.
        /// A triangle is then removed if any of its three vertices is marked. Everything else
        /// (normals, tangents, every UV channel present, vertex colors, bone weights, blend shapes)
        /// is remapped onto the compacted vertex buffer; bindposes/bones are left untouched.
        /// </summary>
        public static Result Cut(SkinnedMeshRenderer renderer, bool aggressiveRemoval)
        {
            var sourceMesh = renderer.sharedMesh;
            var bones = renderer.bones;
            var vertexCount = sourceMesh.vertexCount;
            var weights = sourceMesh.boneWeights;

            var isZeroScale = new bool[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                isZeroScale[i] = bones[i] == null || bones[i].lossyScale.magnitude < ScaleEpsilon;
            }

            var removeVertex = new bool[vertexCount];
            for (var v = 0; v < weights.Length; v++)
            {
                var w = weights[v];
                var anyZero = false;
                var anyNonZero = false;
                CheckSlot(w.boneIndex0, w.weight0, isZeroScale, ref anyZero, ref anyNonZero);
                CheckSlot(w.boneIndex1, w.weight1, isZeroScale, ref anyZero, ref anyNonZero);
                CheckSlot(w.boneIndex2, w.weight2, isZeroScale, ref anyZero, ref anyNonZero);
                CheckSlot(w.boneIndex3, w.weight3, isZeroScale, ref anyZero, ref anyNonZero);

                removeVertex[v] = aggressiveRemoval ? anyZero : anyZero && !anyNonZero;
            }

            var newIndicesBySubmesh = new int[sourceMesh.subMeshCount][];
            var used = new bool[vertexCount];
            var trianglesRemoved = 0;

            for (var sub = 0; sub < sourceMesh.subMeshCount; sub++)
            {
                var indices = sourceMesh.GetTriangles(sub);
                var kept = new List<int>(indices.Length);
                for (var t = 0; t + 2 < indices.Length; t += 3)
                {
                    var a = indices[t];
                    var b = indices[t + 1];
                    var c = indices[t + 2];
                    if (removeVertex[a] || removeVertex[b] || removeVertex[c])
                    {
                        trianglesRemoved++;
                        continue;
                    }
                    kept.Add(a);
                    kept.Add(b);
                    kept.Add(c);
                    used[a] = true;
                    used[b] = true;
                    used[c] = true;
                }
                newIndicesBySubmesh[sub] = kept.ToArray();
            }

            var oldToNew = new int[vertexCount];
            var keepList = new List<int>(vertexCount);
            for (var v = 0; v < vertexCount; v++)
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

            for (var sub = 0; sub < newIndicesBySubmesh.Length; sub++)
            {
                var indices = newIndicesBySubmesh[sub];
                for (var i = 0; i < indices.Length; i++) indices[i] = oldToNew[indices[i]];
            }

            var newMesh = AssembleMesh(sourceMesh, keep, newIndicesBySubmesh, vertexCount);

            return new Result
            {
                mesh = newMesh,
                trianglesRemoved = trianglesRemoved,
                verticesRemoved = vertexCount - keep.Length,
            };
        }

        private static void CheckSlot(int boneIndex, float weight, bool[] isZeroScale, ref bool anyZero, ref bool anyNonZero)
        {
            if (weight <= 0) return;
            if (boneIndex >= 0 && boneIndex < isZeroScale.Length && isZeroScale[boneIndex]) anyZero = true;
            else anyNonZero = true;
        }

        private static Mesh AssembleMesh(Mesh source, int[] keep, int[][] newIndicesBySubmesh, int vertexCount)
        {
            var mesh = new Mesh { name = source.name + " (Cut)" };
            mesh.indexFormat = keep.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            mesh.SetVertices(Gather(source.vertices, keep));

            var normals = source.normals;
            if (normals != null && normals.Length == vertexCount) mesh.SetNormals(Gather(normals, keep));

            var tangents = source.tangents;
            if (tangents != null && tangents.Length == vertexCount) mesh.SetTangents(Gather(tangents, keep));

            if (source.HasVertexAttribute(VertexAttribute.Color))
            {
                if (source.GetVertexAttributeFormat(VertexAttribute.Color) == VertexAttributeFormat.UNorm8)
                {
                    var colors32 = source.colors32;
                    if (colors32.Length == vertexCount) mesh.colors32 = Gather(colors32, keep);
                }
                else
                {
                    var colors = source.colors;
                    if (colors.Length == vertexCount) mesh.colors = Gather(colors, keep);
                }
            }

            CopyUVs(source, mesh, keep, vertexCount);
            CopyBoneWeights(source, mesh, keep, vertexCount);
            mesh.bindposes = source.bindposes;

            mesh.subMeshCount = newIndicesBySubmesh.Length;
            for (var sub = 0; sub < newIndicesBySubmesh.Length; sub++)
            {
                mesh.SetTriangles(newIndicesBySubmesh[sub], sub);
            }

            CopyBlendShapes(source, mesh, keep, vertexCount);

            mesh.RecalculateBounds();
            return mesh;
        }

        private static void CopyUVs(Mesh source, Mesh dest, int[] keep, int vertexCount)
        {
            for (var channel = 0; channel < 8; channel++)
            {
                var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                if (!source.HasVertexAttribute(attribute)) continue;

                var dimension = source.GetVertexAttributeDimension(attribute);
                switch (dimension)
                {
                    case 2:
                    {
                        var values = new List<Vector2>();
                        source.GetUVs(channel, values);
                        if (values.Count == vertexCount) dest.SetUVs(channel, Gather(values, keep));
                        break;
                    }
                    case 3:
                    {
                        var values = new List<Vector3>();
                        source.GetUVs(channel, values);
                        if (values.Count == vertexCount) dest.SetUVs(channel, Gather(values, keep));
                        break;
                    }
                    default:
                    {
                        var values = new List<Vector4>();
                        source.GetUVs(channel, values);
                        if (values.Count == vertexCount) dest.SetUVs(channel, Gather(values, keep));
                        break;
                    }
                }
            }
        }

        private static void CopyBoneWeights(Mesh source, Mesh dest, int[] keep, int vertexCount)
        {
            var weights = source.boneWeights;
            if (weights.Length != vertexCount) return;
            dest.boneWeights = Gather(weights, keep);
        }

        private static void CopyBlendShapes(Mesh source, Mesh dest, int[] keep, int vertexCount)
        {
            if (source.blendShapeCount == 0) return;

            var deltaVerts = new Vector3[vertexCount];
            var deltaNorms = new Vector3[vertexCount];
            var deltaTans = new Vector3[vertexCount];

            for (var shape = 0; shape < source.blendShapeCount; shape++)
            {
                var name = source.GetBlendShapeName(shape);
                var frames = source.GetBlendShapeFrameCount(shape);
                for (var frame = 0; frame < frames; frame++)
                {
                    var weight = source.GetBlendShapeFrameWeight(shape, frame);
                    source.GetBlendShapeFrameVertices(shape, frame, deltaVerts, deltaNorms, deltaTans);
                    dest.AddBlendShapeFrame(
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
    }
}
