// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 규칙
    //   1P P03, 이지 2P P06: 월백 중심 원 하나를 판정 시각에 한 번 터뜨린다
    //   하드 2P P06: 월백 중심 원에 더해 전장 중심 기준 사분면 원 12개를 같은 순간에 터뜨린다
    //     사분면 한 곳의 원 세 개는 (안쪽, 안쪽), (바깥쪽, 안쪽), (안쪽, 바깥쪽) 좌표이고 사분면 네 곳은 좌표의 부호 네 조합이다
    // 효과 크기는 기준 반경의 원에 맞춘 값이고 다른 반경의 원은 반경 비율만큼 키운다
    /// <summary> 패턴 판정 시각에 서버가 정한 원마다 폭발 효과를 만드는 확인용 도구 </summary>
    public sealed class CircleBlastCue : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int QUADRANT_SIGNS = 2;

        /****************************************
        *              Nested Types
        ****************************************/

        /// <summary> 패턴 하나의 보스 중심 원 반경과 사분면 원 사용 여부 </summary>
        [Serializable]
        public sealed class Cue
        {
            [SerializeField] private string _label;
            [SerializeField] private float  _bossRadius = 5f;
            [SerializeField] private bool   _includeSpots;
            [SerializeField] private float  _scaleMultiplier = 1f;

            public string Label           => _label;
            public float  BossRadius      => _bossRadius;
            public bool   IncludeSpots    => _includeSpots;
            public float  ScaleMultiplier => _scaleMultiplier;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PatternMotionPreview _pattern;
        [SerializeField] private Transform _origin;
        [SerializeField] private GameObject _prefab;
        [SerializeField] private float _heightOffset = 0.1f;
        [SerializeField] private float _lifetime = 4f;
        [SerializeField] private float _referenceRadius = 5f;
        [SerializeField] private float _referenceScale = 1.92f;
        [SerializeField] private float _spotRadius = 3f;
        [SerializeField] private float _nearOffset = 5f;
        [SerializeField] private float _farOffset = 11f;
        [SerializeField] private Cue[] _cues;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _pattern.Struck += OnStruck;
        }

        private void OnDisable()
        {
            _pattern.Struck -= OnStruck;
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnStruck(string label, int shotIndex)
        {
            Cue cue = FindCue(label);

            if (cue == null)
            {
                return;
            }

            Vector3 center = _origin.position;
            Spawn(center, cue.BossRadius, cue.ScaleMultiplier);

            if (!cue.IncludeSpots)
            {
                return;
            }

            // 사분면마다 안쪽과 바깥쪽 좌표를 섞어 세 곳에 원을 놓는다
            for (int x = 0; x < QUADRANT_SIGNS; ++x)
            {
                for (int z = 0; z < QUADRANT_SIGNS; ++z)
                {
                    float signX = (x == 0) ? 1f : -1f;
                    float signZ = (z == 0) ? 1f : -1f;
                    Spawn(center + new Vector3(signX * _nearOffset, 0f, signZ * _nearOffset), _spotRadius, cue.ScaleMultiplier);
                    Spawn(center + new Vector3(signX * _farOffset, 0f, signZ * _nearOffset), _spotRadius, cue.ScaleMultiplier);
                    Spawn(center + new Vector3(signX * _nearOffset, 0f, signZ * _farOffset), _spotRadius, cue.ScaleMultiplier);
                }
            }
        }

        private void Spawn(Vector3 groundPosition, float radius, float scaleMultiplier)
        {
            Vector3 position = groundPosition + (Vector3.up * _heightOffset);
            GameObject instance = Instantiate(_prefab, position, Quaternion.identity);
            instance.transform.localScale = _prefab.transform.localScale * (_referenceScale * radius / _referenceRadius * scaleMultiplier);
            Destroy(instance, _lifetime);
        }

        private Cue FindCue(string label)
        {
            for (int i = 0; i < _cues.Length; ++i)
            {
                if (_cues[i].Label == label)
                {
                    return _cues[i];
                }
            }

            return null;
        }
    }
}
