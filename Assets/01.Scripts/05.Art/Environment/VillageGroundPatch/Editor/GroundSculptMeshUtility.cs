#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;

// Unity
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shinshi.Art.Editor
{
    /// <summary> 원본 메시를 보존하며 정적 지형의 편집 사본과 월드 높이 브러시를 계산한다 </summary>
    public static class GroundSculptMeshUtility
    {
        public enum ESculptMode
        {
            Raise,
            Lower,
            Smooth,
            Flatten
        }

        /****************************************
        *                Fields
        ****************************************/

        private const int   MAX_VERTICES          = 100000;
        private const int   MAX_SUBDIVISION_PASSES = 24;
        private const int   VERTICES_PER_TRIANGLE = 3;
        private const int   UV_CHANNEL_COUNT      = 8;
        private const int   INDEX_SHIFT           = 32;
        private const int   UINT16_VERTEX_LIMIT   = 65535;
        private const int   UV_TWO_COMPONENTS     = 2;
        private const int   UV_THREE_COMPONENTS   = 3;
        private const float HALF                  = 0.5f;
        private const float WELD_DISTANCE         = 0.0001f;
        private const float MIN_CHANGE            = 0.000001f;
        private const float MIN_EDGE_LENGTH       = 0.01f;
        private const float MAX_EDGE_TOLERANCE     = 1.00001f;

        private sealed class MeshContent
        {
            public List<Vector3> _positions;
            public List<Vector3> _normals;
            public List<Vector4> _tangents;
            public List<Color>   _colors;
            public List<int>[]   _submeshes;
            public readonly List<Vector4>[] _uvs          = new List<Vector4>[UV_CHANNEL_COUNT];
            public readonly int[]           _uvDimensions = new int[UV_CHANNEL_COUNT];
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 삼각형과 표면 속성을 보존한 읽기 가능한 사본. 호출자가 에셋 저장과 파괴를 관리한다 </summary>
        public static Mesh CreateEditableMesh(Mesh source, Transform transform, float maxEdgeLength)
        {
            if ((source == null) || (transform == null) || (!IsFinite(maxEdgeLength))
                || (maxEdgeLength < MIN_EDGE_LENGTH) || (source.vertexCount == 0)
                || (source.vertexCount > MAX_VERTICES) || (source.blendShapeCount != 0))
            {
                throw new ArgumentException("정점 1~100000개의 정적 메시와 0.01m 이상의 분할 간격이 필요하다");
            }

            Mesh editable = null;
            if (source.lodCount > 1)
            {
                throw new ArgumentException("내부 LOD가 없는 지형 표면 메시를 선택해야 한다");
            }
            try
            {
                MeshContent content = ReadContent(source);
                Subdivide(content, transform, maxEdgeLength);
                editable = new Mesh
                {
                    name = source.name + "_Sculpt",
                    indexFormat = (content._positions.Count > UINT16_VERTEX_LIMIT) ? IndexFormat.UInt32 : IndexFormat.UInt16
                };
                editable.SetVertices(content._positions);
                if (content._normals != null)
                {
                    editable.SetNormals(content._normals);
                }
                if (content._tangents != null)
                {
                    editable.SetTangents(content._tangents);
                }
                if (content._colors != null)
                {
                    editable.SetColors(content._colors);
                }
                WriteUvs(editable, content);
                editable.subMeshCount = content._submeshes.Length;
                for (int submesh = 0; submesh < content._submeshes.Length; ++submesh)
                {
                    editable.SetTriangles(content._submeshes[submesh], submesh, false);
                }
                if (content._normals == null)
                {
                    editable.RecalculateNormals();
                }
                editable.RecalculateBounds();
                return editable;
            }
            catch
            {
                if (editable != null)
                {
                    UnityEngine.Object.DestroyImmediate(editable);
                }
                throw;
            }
        }

        /// <summary> 한 stamp의 높이 변위. Smooth/Flatten의 amount는 0~1 혼합 비율이다 </summary>
        public static int ApplyBrush(Mesh mesh, Transform transform, Vector3 center, float radius,
            float amount, ESculptMode mode, float flattenHeight, bool preserveBoundary)
        {
            if ((mesh == null) || (transform == null) || (!mesh.isReadable)
                || (!IsFinite(radius)) || (radius <= 0f) || (!IsFinite(amount))
                || (!IsFinite(flattenHeight)) || (!IsFinite(center.x)) || (!IsFinite(center.y))
                || (!IsFinite(center.z)) || (!Enum.IsDefined(typeof(ESculptMode), mode)))
            {
                throw new ArgumentException("읽기 가능한 메시와 유효한 브러시 값이 필요하다");
            }
            if (Mathf.Abs(amount) <= MIN_CHANGE)
            {
                return 0;
            }

            Vector3[] vertices = mesh.vertices;
            var groupByPosition = new Dictionary<Vector3Int, int>();
            var positions = new List<Vector3>();
            var groups = new int[vertices.Length];
            for (int vertex = 0; vertex < vertices.Length; ++vertex)
            {
                Vector3 world = transform.TransformPoint(vertices[vertex]);
                Vector3Int key = Quantize(world);
                if (!groupByPosition.TryGetValue(key, out int group))
                {
                    group = positions.Count;
                    groupByPosition.Add(key, group);
                    positions.Add(world);
                }
                groups[vertex] = group;
            }

            var neighbors = new HashSet<int>[positions.Count];
            var edges = new Dictionary<ulong, int>();
            for (int group = 0; group < neighbors.Length; ++group)
            {
                neighbors[group] = new HashSet<int>();
            }
            for (int submesh = 0; submesh < mesh.subMeshCount; ++submesh)
            {
                if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                {
                    throw new ArgumentException("삼각형 지형 메시만 조형할 수 있다");
                }
                int[] indices = mesh.GetIndices(submesh);
                for (int triangle = 0; triangle < indices.Length; triangle += VERTICES_PER_TRIANGLE)
                {
                    int a = groups[indices[triangle]];
                    int b = groups[indices[triangle + 1]];
                    int c = groups[indices[triangle + 2]];
                    AddNeighborEdge(a, b, neighbors, edges);
                    AddNeighborEdge(b, c, neighbors, edges);
                    AddNeighborEdge(c, a, neighbors, edges);
                }
            }

            var boundaries = new bool[positions.Count];
            if (preserveBoundary)
            {
                foreach (KeyValuePair<ulong, int> edge in edges)
                {
                    if (edge.Value == 1)
                    {
                        boundaries[(int)(edge.Key >> INDEX_SHIFT)] = true;
                        boundaries[(int)(edge.Key & uint.MaxValue)] = true;
                    }
                }
            }
            var offsets = new float[positions.Count];
            for (int group = 0; group < positions.Count; ++group)
            {
                float distance = Vector3.Distance(positions[group], center);
                if ((boundaries[group]) || (distance >= radius))
                {
                    continue;
                }
                float falloff = Mathf.SmoothStep(1f, 0f, distance / radius);
                float height = positions[group].y;
                switch (mode)
                {
                    case ESculptMode.Raise:
                        offsets[group] = Mathf.Abs(amount) * falloff;
                        break;
                    case ESculptMode.Lower:
                        offsets[group] = -Mathf.Abs(amount) * falloff;
                        break;
                    case ESculptMode.Smooth:
                        if (neighbors[group].Count == 0)
                        {
                            continue;
                        }
                        float heightSum = 0f;
                        foreach (int neighbor in neighbors[group])
                        {
                            heightSum += positions[neighbor].y;
                        }
                        offsets[group] = ((heightSum / neighbors[group].Count) - height) * Mathf.Clamp01(amount) * falloff;
                        break;
                    case ESculptMode.Flatten:
                        offsets[group] = (flattenHeight - height) * Mathf.Clamp01(amount) * falloff;
                        break;
                }
            }

            int changed = 0;
            for (int vertex = 0; vertex < vertices.Length; ++vertex)
            {
                float offset = offsets[groups[vertex]];
                if (Mathf.Abs(offset) <= MIN_CHANGE)
                {
                    continue;
                }
                Vector3 world = transform.TransformPoint(vertices[vertex]);
                world.y += offset;
                vertices[vertex] = transform.InverseTransformPoint(world);
                ++changed;
            }
            if (changed != 0)
            {
                mesh.vertices = vertices;
                mesh.RecalculateNormals();
                if (mesh.HasVertexAttribute(VertexAttribute.TexCoord0))
                {
                    mesh.RecalculateTangents();
                }
                mesh.RecalculateBounds();
            }
            return changed;
        }

        private static MeshContent ReadContent(Mesh source)
        {
            using (Mesh.MeshDataArray array = MeshUtility.AcquireReadOnlyMeshData(source))
            {
                Mesh.MeshData data = array[0];
                if ((data.HasVertexAttribute(VertexAttribute.BlendWeight))
                    || (data.HasVertexAttribute(VertexAttribute.BlendIndices)))
                {
                    throw new ArgumentException("스킨 메시를 정적 지형으로 편집할 수 없다");
                }
                var content = new MeshContent
                {
                    _positions = ReadAttribute<Vector3>(data.vertexCount, data.GetVertices),
                    _submeshes = new List<int>[data.subMeshCount]
                };
                if (data.HasVertexAttribute(VertexAttribute.Normal))
                {
                    content._normals = ReadAttribute<Vector3>(data.vertexCount, data.GetNormals);
                }
                if (data.HasVertexAttribute(VertexAttribute.Tangent))
                {
                    content._tangents = ReadAttribute<Vector4>(data.vertexCount, data.GetTangents);
                }
                if (data.HasVertexAttribute(VertexAttribute.Color))
                {
                    content._colors = ReadAttribute<Color>(data.vertexCount, data.GetColors);
                }
                for (int channel = 0; channel < UV_CHANNEL_COUNT; ++channel)
                {
                    VertexAttribute attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                    if (!data.HasVertexAttribute(attribute))
                    {
                        continue;
                    }
                    int uvChannel = channel;
                    content._uvDimensions[channel] = data.GetVertexAttributeDimension(attribute);
                    if (content._uvDimensions[channel] < UV_TWO_COMPONENTS)
                    {
                        throw new ArgumentException("UV 차원이 2~4인 지형 표면 메시가 필요하다");
                    }
                    content._uvs[channel] = ReadAttribute<Vector4>(data.vertexCount, values => data.GetUVs(uvChannel, values));
                }
                for (int submesh = 0; submesh < data.subMeshCount; ++submesh)
                {
                    SubMeshDescriptor descriptor = data.GetSubMesh(submesh);
                    if ((descriptor.topology != MeshTopology.Triangles)
                        || ((descriptor.indexCount % VERTICES_PER_TRIANGLE) != 0))
                    {
                        throw new ArgumentException("모든 submesh가 삼각형인 정적 지형이 필요하다");
                    }
                    int submeshIndex = submesh;
                    content._submeshes[submesh] = ReadAttribute<int>(descriptor.indexCount,
                        indices => data.GetIndices(indices, submeshIndex, true));
                }
                return content;
            }
        }

        private static List<T> ReadAttribute<T>(int count, Action<NativeArray<T>> read) where T : struct
        {
            using (var values = new NativeArray<T>(count, Allocator.Temp))
            {
                read(values);
                return new List<T>(values.ToArray());
            }
        }

        private static void WriteUvs(Mesh mesh, MeshContent content)
        {
            for (int channel = 0; channel < UV_CHANNEL_COUNT; ++channel)
            {
                List<Vector4> values = content._uvs[channel];
                if (values == null)
                {
                    continue;
                }
                if (content._uvDimensions[channel] <= UV_TWO_COMPONENTS)
                {
                    mesh.SetUVs(channel, values.ConvertAll(value => new Vector2(value.x, value.y)));
                }
                else if (content._uvDimensions[channel] == UV_THREE_COMPONENTS)
                {
                    mesh.SetUVs(channel, values.ConvertAll(value => new Vector3(value.x, value.y, value.z)));
                }
                else
                {
                    mesh.SetUVs(channel, values);
                }
            }
        }

        // 같은 edge의 midpoint를 모든 인접 삼각형이 공유한다. 선택적 분할로 T 접합이 생기는 것을 막는다.
        private static void Subdivide(MeshContent content, Transform transform, float maxEdgeLength)
        {
            float maxLengthSquared = maxEdgeLength * maxEdgeLength * MAX_EDGE_TOLERANCE;
            for (int pass = 0; pass < MAX_SUBDIVISION_PASSES; ++pass)
            {
                var midpoints = new Dictionary<ulong, int>();
                foreach (List<int> indices in content._submeshes)
                {
                    for (int triangle = 0; triangle < indices.Count; triangle += VERTICES_PER_TRIANGLE)
                    {
                        MarkEdge(indices[triangle], indices[triangle + 1], content, transform, maxLengthSquared, midpoints);
                        MarkEdge(indices[triangle + 1], indices[triangle + 2], content, transform, maxLengthSquared, midpoints);
                        MarkEdge(indices[triangle + 2], indices[triangle], content, transform, maxLengthSquared, midpoints);
                    }
                }
                if (midpoints.Count == 0)
                {
                    return;
                }
                for (int submesh = 0; submesh < content._submeshes.Length; ++submesh)
                {
                    List<int> old = content._submeshes[submesh];
                    var divided = new List<int>(old.Count);
                    for (int triangle = 0; triangle < old.Count; triangle += VERTICES_PER_TRIANGLE)
                    {
                        SplitTriangle(old[triangle], old[triangle + 1], old[triangle + 2], midpoints, divided);
                    }
                    content._submeshes[submesh] = divided;
                }
            }
            throw new InvalidOperationException("메시 분할 24회 한도를 넘었다. 분할 간격을 늘려야 한다");
        }

        private static void MarkEdge(int a, int b, MeshContent content, Transform transform,
            float maxLengthSquared, Dictionary<ulong, int> midpoints)
        {
            ulong key = EdgeKey(a, b);
            if ((midpoints.ContainsKey(key))
                || ((transform.TransformVector(content._positions[a] - content._positions[b]).sqrMagnitude) <= maxLengthSquared))
            {
                return;
            }
            if (content._positions.Count >= MAX_VERTICES)
            {
                throw new InvalidOperationException("편집 메시가 100000 정점을 넘는다. 분할 간격을 늘려야 한다");
            }
            int midpoint = content._positions.Count;
            content._positions.Add(Vector3.LerpUnclamped(content._positions[a], content._positions[b], HALF));
            if (content._normals != null)
            {
                content._normals.Add(Vector3.LerpUnclamped(content._normals[a], content._normals[b], HALF).normalized);
            }
            if (content._tangents != null)
            {
                Vector4 tangent = Vector4.LerpUnclamped(content._tangents[a], content._tangents[b], HALF);
                Vector3 direction = new Vector3(tangent.x, tangent.y, tangent.z).normalized;
                content._tangents.Add(new Vector4(direction.x, direction.y, direction.z, tangent.w));
            }
            if (content._colors != null)
            {
                content._colors.Add(Color.LerpUnclamped(content._colors[a], content._colors[b], HALF));
            }
            for (int channel = 0; channel < UV_CHANNEL_COUNT; ++channel)
            {
                List<Vector4> values = content._uvs[channel];
                if (values != null)
                {
                    values.Add(Vector4.LerpUnclamped(values[a], values[b], HALF));
                }
            }
            midpoints.Add(key, midpoint);
        }

        private static void SplitTriangle(int a, int b, int c, Dictionary<ulong, int> midpoints, List<int> output)
        {
            bool splitAb = midpoints.TryGetValue(EdgeKey(a, b), out int ab);
            bool splitBc = midpoints.TryGetValue(EdgeKey(b, c), out int bc);
            bool splitCa = midpoints.TryGetValue(EdgeKey(c, a), out int ca);
            if ((splitAb) && (splitBc) && (splitCa))
            {
                AddTriangle(output, a, ab, ca);
                AddTriangle(output, ab, b, bc);
                AddTriangle(output, ca, bc, c);
                AddTriangle(output, ab, bc, ca);
            }
            else if ((splitAb) && (splitBc))
            {
                SplitTwoEdges(output, a, b, c, ab, bc);
            }
            else if ((splitBc) && (splitCa))
            {
                SplitTwoEdges(output, b, c, a, bc, ca);
            }
            else if ((splitCa) && (splitAb))
            {
                SplitTwoEdges(output, c, a, b, ca, ab);
            }
            else if (splitAb)
            {
                SplitOneEdge(output, a, b, c, ab);
            }
            else if (splitBc)
            {
                SplitOneEdge(output, b, c, a, bc);
            }
            else if (splitCa)
            {
                SplitOneEdge(output, c, a, b, ca);
            }
            else
            {
                AddTriangle(output, a, b, c);
            }
        }

        private static void SplitOneEdge(List<int> output, int a, int b, int c, int ab)
        {
            AddTriangle(output, a, ab, c);
            AddTriangle(output, ab, b, c);
        }

        private static void SplitTwoEdges(List<int> output, int a, int b, int c, int ab, int bc)
        {
            AddTriangle(output, a, ab, c);
            AddTriangle(output, ab, bc, c);
            AddTriangle(output, ab, b, bc);
        }

        private static void AddTriangle(List<int> output, int a, int b, int c)
        {
            output.Add(a);
            output.Add(b);
            output.Add(c);
        }

        private static void AddNeighborEdge(int a, int b, HashSet<int>[] neighbors, Dictionary<ulong, int> edges)
        {
            if (a == b)
            {
                return;
            }
            neighbors[a].Add(b);
            neighbors[b].Add(a);
            ulong key = EdgeKey(a, b);
            edges.TryGetValue(key, out int count);
            edges[key] = count + 1;
        }

        private static ulong EdgeKey(int a, int b)
        {
            return ((ulong)(uint)Math.Min(a, b) << INDEX_SHIFT) | (uint)Math.Max(a, b);
        }

        private static Vector3Int Quantize(Vector3 position)
        {
            return new Vector3Int(Mathf.RoundToInt(position.x / WELD_DISTANCE),
                Mathf.RoundToInt(position.y / WELD_DISTANCE), Mathf.RoundToInt(position.z / WELD_DISTANCE));
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }
    }
}
#endif
