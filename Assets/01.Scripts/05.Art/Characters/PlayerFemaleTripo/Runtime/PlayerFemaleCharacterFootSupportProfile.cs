// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 실제 메시 bindpose와 Humanoid 목표 좌표에서 생성한 로컬 표현 전용 발바닥 데이터 </summary>
    [Serializable]
    public sealed class PlayerFemaleCharacterFootSupportProfile
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int MAX_INFLUENCES = 4;
        public const int MAX_BONES      = 128;
        public const int MAX_SUPPORTS   = 8192;

        [SerializeField] private Avatar _avatar;
        [SerializeField] private Mesh _sourceMesh;
        [SerializeField] private int _sourceVertexCount;
        [SerializeField] private string _sourceAssetHash;
        [SerializeField] private string _sourceMetadataHash;
        [SerializeField] private string[] _bonePaths;
        [SerializeField] private GoalMapping _leftMapping;
        [SerializeField] private GoalMapping _rightMapping;
        [SerializeField] private SupportPoint[] _leftPoints;
        [SerializeField] private SupportPoint[] _rightPoints;

        /****************************************
        *              Properties
        ****************************************/

        public Avatar Avatar            => _avatar;
        public Mesh SourceMesh          => _sourceMesh;
        public int SourceVertexCount    => _sourceVertexCount;
        public string SourceAssetHash   => _sourceAssetHash;
        public string SourceMetadataHash => _sourceMetadataHash;
        public int BoneCount            => _bonePaths == null ? 0 : _bonePaths.Length;
        public int LeftSupportCount     => _leftPoints == null ? 0 : _leftPoints.Length;
        public int RightSupportCount    => _rightPoints == null ? 0 : _rightPoints.Length;
        public GoalMapping LeftMapping  => _leftMapping;
        public GoalMapping RightMapping => _rightMapping;

        internal SupportPoint[] LeftPoints  => _leftPoints;
        internal SupportPoint[] RightPoints => _rightPoints;

        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        public struct GoalMapping
        {
            [SerializeField] private Vector3 _goalLocalBoneOffset;
            [SerializeField] private Quaternion _goalLocalBoneRotation;

            public Vector3 GoalLocalBoneOffset     => _goalLocalBoneOffset;
            public Quaternion GoalLocalBoneRotation => _goalLocalBoneRotation;

            public GoalMapping(Vector3 offset, Quaternion rotation)
            {
                _goalLocalBoneOffset = offset;
                _goalLocalBoneRotation = rotation;
            }
        }

        [Serializable]
        public struct Influence
        {
            [SerializeField] private int _boneIndex;
            [SerializeField] private float _weight;
            [SerializeField] private Vector3 _boneLocalPoint;

            public int BoneIndex          => _boneIndex;
            public float Weight           => _weight;
            public Vector3 BoneLocalPoint => _boneLocalPoint;

            public Influence(int boneIndex, float weight, Vector3 boneLocalPoint)
            {
                _boneIndex = boneIndex;
                _weight = weight;
                _boneLocalPoint = boneLocalPoint;
            }
        }

        [Serializable]
        public struct SupportPoint
        {
            [SerializeField] private int _sourceVertex;
            [SerializeField] private int _influenceCount;
            [SerializeField] private Influence _first;
            [SerializeField] private Influence _second;
            [SerializeField] private Influence _third;
            [SerializeField] private Influence _fourth;

            public int SourceVertex    => _sourceVertex;
            public int InfluenceCount  => _influenceCount;
            public Influence First     => _first;
            public Influence Second    => _second;
            public Influence Third     => _third;
            public Influence Fourth    => _fourth;

            public SupportPoint(int sourceVertex, int influenceCount, in Influence first,
                in Influence second, in Influence third, in Influence fourth)
            {
                _sourceVertex = sourceVertex;
                _influenceCount = influenceCount;
                _first = first;
                _second = second;
                _third = third;
                _fourth = fourth;
            }

            public Influence GetInfluence(int index)
            {
                switch (index)
                {
                    case 0:
                        return _first;
                    case 1:
                        return _second;
                    case 2:
                        return _third;
                    case 3:
                        return _fourth;
                    default:
                        return default;
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> authoring에 사용한 bone 순서를 런타임 연결 검사에 제공한다 </summary>
        public string GetBonePath(int index)
        {
            return _bonePaths[index];
        }

        /// <summary> 검사 도구에서 원본 발바닥 정점 번호를 읽는다 </summary>
        public SupportPoint GetSupportPoint(bool left, int index)
        {
            return left ? _leftPoints[index] : _rightPoints[index];
        }

#if UNITY_EDITOR
        /// <summary> 에디터 생성기가 실제 source와 calibration으로 직렬화 데이터를 만든다 </summary>
        public void SetAuthoringData(Avatar avatar, Mesh sourceMesh, string sourceAssetHash, string sourceMetadataHash,
            string[] bonePaths, in GoalMapping leftMapping, in GoalMapping rightMapping,
            SupportPoint[] leftPoints, SupportPoint[] rightPoints)
        {
            _avatar = avatar;
            _sourceMesh = sourceMesh;
            _sourceVertexCount = sourceMesh == null ? 0 : sourceMesh.vertexCount;
            _sourceAssetHash = sourceAssetHash;
            _sourceMetadataHash = sourceMetadataHash;
            _bonePaths = bonePaths == null ? null : (string[])bonePaths.Clone();
            _leftMapping = leftMapping;
            _rightMapping = rightMapping;
            _leftPoints = leftPoints == null ? null : (SupportPoint[])leftPoints.Clone();
            _rightPoints = rightPoints == null ? null : (SupportPoint[])rightPoints.Clone();
        }
#endif
    }
}
