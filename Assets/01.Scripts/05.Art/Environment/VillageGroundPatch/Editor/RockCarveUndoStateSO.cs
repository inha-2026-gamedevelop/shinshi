#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.Art.Editor
{
    /// <summary> 바위 깎기 브러시의 한 드래그 전후 정점과 법선을 Undo에 맡기는 편집 전용 상태 </summary>
    public sealed class RockCarveUndoStateSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        [Serializable]
        private sealed class MeshSnapshot
        {
            public Mesh      _mesh     = null;
            public Vector3[] _vertices = null;
            public Vector3[] _normals  = null;
        }

        // 드래그마다 고밀도 LOD 사본을 새 에셋으로 남기면 디스크가 빠르게 차므로
        // 메시 에셋은 하나로 두고 되돌릴 값만 Undo가 직렬화하는 이 객체에 둔다
        [SerializeField] private List<MeshSnapshot> _snapshots = new List<MeshSnapshot>();

        /****************************************
        *                Methods
        ****************************************/

        public void Capture(IReadOnlyList<Mesh> meshes)
        {
            if (meshes == null)
            {
                return;
            }
            _snapshots.Clear();
            foreach (Mesh mesh in meshes)
            {
                if (mesh == null)
                {
                    continue;
                }
                _snapshots.Add(new MeshSnapshot { _mesh = mesh, _vertices = mesh.vertices, _normals = mesh.normals });
            }
        }

        /// <summary> 저장된 정점과 법선을 같은 메시에 되돌리고 바뀐 메시 수를 돌려준다 </summary>
        public int Restore()
        {
            int restored = 0;
            foreach (MeshSnapshot snapshot in _snapshots)
            {
                if ((snapshot._mesh == null) || (snapshot._vertices == null)
                    || (snapshot._vertices.Length != snapshot._mesh.vertexCount))
                {
                    continue;
                }
                snapshot._mesh.vertices = snapshot._vertices;
                if ((snapshot._normals != null) && (snapshot._normals.Length == snapshot._vertices.Length))
                {
                    snapshot._mesh.normals = snapshot._normals;
                }
                snapshot._mesh.RecalculateBounds();
                ++restored;
            }
            return restored;
        }
    }
}
#endif
