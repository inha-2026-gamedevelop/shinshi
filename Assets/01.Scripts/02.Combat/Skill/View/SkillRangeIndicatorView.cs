// Unity
using UnityEngine;
using UnityEngine.Rendering;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 지점 기술 지정 중 시전자 둘레의 최대 거리 고리와 지정점의 범위 원을 그린다, 소유 클라이언트 표시 전용 </summary>
    public sealed class SkillRangeIndicatorView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        // 원 하나의 꼭짓점 수. 반경 10m에서 한 변이 약 1m라 쿼터뷰 거리에서 각이 보이지 않는다
        private const int   RING_SEGMENTS = 64;
        private const float FULL_TURN_RAD = Mathf.PI * 2f;

        // 수평 고리가 되도록 선의 폭 방향을 지면에 눕힌다
        private static readonly Quaternion _flatRotation = Quaternion.Euler(90f, 0f, 0f);

        [Header("표시")]
        [SerializeField] private Material _material;
        [SerializeField] private Color    _rangeColor   = new Color(0.55f, 0.8f, 1f, 0.35f);
        [SerializeField] private Color    _validColor   = new Color(0.55f, 0.8f, 1f, 0.8f);
        [SerializeField] private Color    _invalidColor = new Color(1f, 0.25f, 0.2f, 0.8f);
        [SerializeField] private float    _lineWidth    = 0.08f;
        [SerializeField] private float    _groundLift   = 0.05f; // m, 지면과 겹쳐 깜빡이지 않을 만큼 띄운다

        private readonly float[]   _unitX           = new float[RING_SEGMENTS];
        private readonly float[]   _unitZ           = new float[RING_SEGMENTS];
        private readonly Vector3[] _rangePositions  = new Vector3[RING_SEGMENTS];
        private readonly Vector3[] _targetPositions = new Vector3[RING_SEGMENTS];

        private LineRenderer _rangeRing  = null;
        private LineRenderer _targetRing = null;
        private bool         _isShown    = false;
        private bool         _hasWarned  = false;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            for (int i = 0; i < RING_SEGMENTS; ++i)
            {
                float radians = (FULL_TURN_RAD / RING_SEGMENTS) * i;
                _unitX[i] = Mathf.Cos(radians);
                _unitZ[i] = Mathf.Sin(radians);
            }
        }

        private void OnDisable()
        {
            Hide();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 시전자 둘레 최대 거리 고리와 지정점 범위 원을 갱신한다, 지정 중 매 프레임 불러도 할당이 없다 </summary>
        public void Show(Vector3 origin, float castRange, Vector3 point, float radius, bool isValid)
        {
            if ((!enabled) || (!EnsureRenderers()))
            {
                return;
            }

            FillRing(_rangePositions, origin, castRange);
            FillRing(_targetPositions, point, radius);
            _rangeRing.SetPositions(_rangePositions);
            _targetRing.SetPositions(_targetPositions);

            Color targetColor = isValid ? _validColor : _invalidColor;
            _targetRing.startColor = targetColor;
            _targetRing.endColor   = targetColor;

            if (!_isShown)
            {
                _rangeRing.enabled  = true;
                _targetRing.enabled = true;
                _isShown            = true;
            }
        }

        public void Hide()
        {
            if (!_isShown)
            {
                return;
            }

            _isShown = false;
            if (_rangeRing != null)
            {
                _rangeRing.enabled = false;
            }

            if (_targetRing != null)
            {
                _targetRing.enabled = false;
            }
        }

        // 원격 플레이어는 지정을 하지 않으므로 처음 보일 때만 만든다
        private bool EnsureRenderers()
        {
            if ((_rangeRing != null) && (_targetRing != null))
            {
                return true;
            }

            if (_material == null)
            {
                if (!_hasWarned)
                {
                    _hasWarned = true;
                    Debug.LogError("[Skill] 범위 표시 재질이 비어 있어 지점 지정 범위를 그리지 않는다");
                }

                return false;
            }

            _rangeRing  = CreateRing("SkillRangeRing", _rangeColor);
            _targetRing = CreateRing("SkillTargetRing", _validColor);
            return true;
        }

        private LineRenderer CreateRing(string ringName, Color color)
        {
            var ring = new GameObject(ringName);
            ring.transform.SetParent(transform, false);
            ring.transform.rotation = _flatRotation;

            LineRenderer line = ring.AddComponent<LineRenderer>();
            line.sharedMaterial    = _material;
            line.useWorldSpace     = true;
            line.loop              = true;
            line.alignment         = LineAlignment.TransformZ;
            line.positionCount     = RING_SEGMENTS;
            line.widthMultiplier   = _lineWidth;
            line.startColor        = color;
            line.endColor          = color;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows    = false;
            line.enabled           = false;
            return line;
        }

        private void FillRing(Vector3[] positions, Vector3 center, float radius)
        {
            float y = center.y + _groundLift;
            for (int i = 0; i < RING_SEGMENTS; ++i)
            {
                positions[i] = new Vector3(center.x + (_unitX[i] * radius), y, center.z + (_unitZ[i] * radius));
            }
        }
    }
}
