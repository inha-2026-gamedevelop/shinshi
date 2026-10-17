// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 규칙
    //   1P P02: 월백 정면으로 중심각 100도, 사거리 6m 부채꼴을 판정 시각에 한 번 터뜨린다
    //   이지 2P P03: 선택한 쪽 반원 180도를 45도 구역 네 개로 나눠 0.25초 간격으로 차례로 터뜨린다. 오른쪽을 고르면 정면에서 오른쪽으로 이어진다
    //   하드 2P P03: 피난 방향 45도를 비우고 나머지 315도를 45도 구역 일곱 개로 나눠 0.5초 간격으로 터뜨린다. 구역 0은 피난 방향의 시계 쪽 끝에 붙어 시계 방향으로 이어진다
    // 효과 프리팹의 부채꼴 각도는 하나뿐이라 100도 부채꼴은 좌우로 나눈 두 장을 겹쳐 덮는다. 방향은 월백 정면 기준 도(시계 방향이 양수)다
    /// <summary> 패턴 판정 시각마다 서버가 정한 구역 방향으로 부채꼴 효과를 만드는 확인용 도구 </summary>
    public sealed class FanBlastCue : MonoBehaviour
    {
        /****************************************
        *              Nested Types
        ****************************************/

        /// <summary> 패턴 하나의 구역 방향과 크기 </summary>
        [Serializable]
        public sealed class Cue
        {
            [SerializeField] private string  _label;
            [SerializeField] private float   _firstYaw;
            [SerializeField] private float   _stepYaw;
            [SerializeField] private float[] _spreadYaws = { 0f };
            [SerializeField] private float   _scale = 3f;

            public string  Label      => _label;
            public float   FirstYaw   => _firstYaw;
            public float   StepYaw    => _stepYaw;
            public float[] SpreadYaws => _spreadYaws;
            public float   Scale      => _scale;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PatternMotionPreview _pattern;
        [SerializeField] private Transform _origin;
        [SerializeField] private GameObject _prefab;
        [SerializeField] private float _heightOffset = 2f;
        [SerializeField] private float _prefabYawOffset = 180f;
        [SerializeField] private float _lifetime = 4f;
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

            Vector3 position = _origin.position + (Vector3.up * _heightOffset);

            for (int i = 0; i < cue.SpreadYaws.Length; ++i)
            {
                // 효과 프리팹은 로컬 -Z 방향으로 펼쳐지게 만들어져 있어 보정 각도를 더한다
                float yaw = cue.FirstYaw + (shotIndex * cue.StepYaw) + cue.SpreadYaws[i] + _prefabYawOffset;
                GameObject instance = Instantiate(_prefab, position, _origin.rotation * Quaternion.Euler(0f, yaw, 0f));
                instance.transform.localScale = _prefab.transform.localScale * cue.Scale;
                Destroy(instance, _lifetime);
            }
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
