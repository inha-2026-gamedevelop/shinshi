// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 규칙
    //   1P P05: 예고 1초 뒤 지정 위치에 반경 3m 장판을 6초 유지하고 1초 간격으로 지속 피해를 준다
    //   이지 2P P04: 같은 장판을 대상 두 곳에 놓는다
    //   하드 2P P04: 예고 4초 동안 대상을 추적한 뒤 반경 6m 장판을 두 곳에 놓고 오래 유지한다
    // 장판 위치는 서버가 정한 대상 위치라 이 도구는 월백 기준 고정 위치에 놓는다. 서버의 유지 시간 대신 효과 수명만큼 보여 준다
    /// <summary> 패턴 판정 시각에 서버가 정한 장판 위치마다 지속 효과를 만드는 확인용 도구 </summary>
    public sealed class ZoneCue : MonoBehaviour
    {
        /****************************************
        *              Nested Types
        ****************************************/

        /// <summary> 패턴 하나의 장판 위치와 효과 크기와 수명 </summary>
        [Serializable]
        public sealed class Cue
        {
            [SerializeField] private string    _label;
            [SerializeField] private float     _scale = 1f;
            [SerializeField] private float     _lifetime = 7f;
            [SerializeField] private Vector3[] _offsets;

            public string    Label    => _label;
            public float     Scale    => _scale;
            public float     Lifetime => _lifetime;
            public Vector3[] Offsets  => _offsets;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PatternMotionPreview _pattern;
        [SerializeField] private Transform _origin;
        [SerializeField] private GameObject _prefab;
        [SerializeField] private float _heightOffset = 0.1f;
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

            for (int i = 0; i < cue.Offsets.Length; ++i)
            {
                Vector3 position = _origin.position + (_origin.rotation * cue.Offsets[i]) + (Vector3.up * _heightOffset);
                GameObject instance = Instantiate(_prefab, position, Quaternion.identity);
                instance.transform.localScale = _prefab.transform.localScale * cue.Scale;
                Destroy(instance, cue.Lifetime);
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
