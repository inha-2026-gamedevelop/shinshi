// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 무철 패턴 하나의 VFX 재생 길이, 피해 판정 지속시간과는 다른 값이다 </summary>
    [CreateAssetMenu(fileName = "MucheolPatternVfxSettings", menuName = "Shinshi/VFX/Mucheol Pattern Timing")]
    public sealed class MucheolPatternVfxSettingsSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MIN_POSITIVE = 0.01f;

        [Header("표시용 이름")]
        [SerializeField, Tooltip("인스펙터 구분용 이름, 런타임 로직에 쓰지 않는다")]
        private string _patternLabel = "01 부채꼴";

        [Header("예고")]
        [SerializeField, Min(0f), Tooltip("경고 범위가 가득 표시된 채로 유지되는 시간(초), 0이면 즉시 타격")]
        private float _telegraphSeconds = 1.6667f;

        [Header("타격 확산")]
        [SerializeField, Min(MIN_POSITIVE), Tooltip("타격 확인 직후 충격파가 바깥으로 번지는 시간(초)")]
        private float _expansionSeconds = 0.1333f;

        [Header("유지")]
        [SerializeField, Min(0f), Tooltip("확산이 끝난 뒤 지면과 균열이 가득한 채로 유지되는 시간(초)")]
        private float _holdSeconds = 0.2333f;

        [Header("소멸")]
        [SerializeField, Min(MIN_POSITIVE), Tooltip("지면과 균열이 옅어지며 사라지는 시간(초)")]
        private float _fadeSeconds = 0.9333f;

        [Header("파편")]
        [SerializeField, Min(MIN_POSITIVE), Tooltip("바위 파편이 날아가 사라지기까지 걸리는 시간(초), 소멸 단계와 별도로 흐른다")]
        private float _debrisSeconds = 1.3f;

        /****************************************
        *              Properties
        ****************************************/

        public string PatternLabel     => _patternLabel;
        public float  TelegraphSeconds => _telegraphSeconds;
        public float  ExpansionSeconds => _expansionSeconds;
        public float  HoldSeconds      => _holdSeconds;
        public float  FadeSeconds      => _fadeSeconds;
        public float  DebrisSeconds    => _debrisSeconds;

        /// <summary> 예고부터 소멸까지 네 단계의 합, 파편 꼬리는 별도로 더 길게 이어질 수 있다 </summary>
        public float Duration => _telegraphSeconds + _expansionSeconds + _holdSeconds + _fadeSeconds;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnValidate()
        {
            _telegraphSeconds = ClampFinite(_telegraphSeconds, 0f);
            _expansionSeconds = ClampFinite(_expansionSeconds, MIN_POSITIVE);
            _holdSeconds      = ClampFinite(_holdSeconds, 0f);
            _fadeSeconds      = ClampFinite(_fadeSeconds, MIN_POSITIVE);
            _debrisSeconds    = ClampFinite(_debrisSeconds, MIN_POSITIVE);
        }

        /****************************************
        *                Methods
        ****************************************/

        // 인스펙터에 NaN이나 무한대를 입력해도 최소값으로 되돌린다
        private static float ClampFinite(float value, float min)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return min;
            }

            return Mathf.Max(min, value);
        }
    }
}
