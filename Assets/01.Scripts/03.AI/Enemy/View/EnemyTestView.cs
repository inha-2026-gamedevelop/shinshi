// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 서버 값을 받아 그리기만 한다. 외형은 프리팹의 Visual 자식이고 모델이 생기면 그 자식만 바꾼다.
    // 콜라이더는 둘이고 용도가 다르다. 루트의 큰 Trigger는 감지와 피격 범위이자 서버 몸체 반경(돌진 벽 정지 거리 기준)이고,
    // SeparationBody 자식의 작은 비트리거는 적끼리 간격 반경이다. 어느 쪽이든 이 뷰가 읽는 값은 프리팹 크기 원본일 뿐이고
    // 위치 판정은 서버가 한다. 공격 범위는 서버 정의가 정하므로 외형 크기와 서로 따로다
    /// <summary> 일반 적 시험용 표현, 외형 색과 공격 범위 부채꼴을 상태에 맞춰 보여 준다 </summary>
    public sealed class EnemyTestView : ClientView
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    ARC_SEGMENTS   = 24;
        private const float  LINE_WIDTH     = 0.05f;
        private const float  RANGE_HEIGHT   = 0.05f;
        private const int    RECT_POINTS    = 5;
        private const float  HALF           = 0.5f;
        private const string LINE_SHADER    = "Sprites/Default";
        private const string COLOR_BASE     = "_BaseColor";
        private const string COLOR_LEGACY   = "_Color";

        // 돌진 구간 표시색, 예고의 노랑에서 빨강과 구분한다
        private static readonly Color DASH_COLOR = new Color(1f, 0.5f, 0f);

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Transform    _visual;    // 외형 루트, 이 아래 렌더러를 상태 색으로 칠한다
        [SerializeField] private LineRenderer _rangeLine; // 공격 범위 부채꼴

        [Header("콜라이더")]
        [SerializeField] private CapsuleCollider _detectionCollider;  // 루트의 큰 Trigger, 감지와 피격 범위
        [SerializeField] private CapsuleCollider _separationCollider; // SeparationBody 자식의 비트리거, 적끼리 간격

        private Renderer[]            _renderers;
        private MaterialPropertyBlock _block;
        private Material              _lineMaterial;

        /****************************************
        *                Methods
        ****************************************/

        protected override void OnViewAwake()
        {
            if ((_visual == null) || (_rangeLine == null))
            {
                Debug.LogError("[Enemy] 외형 또는 범위 선 참조가 비어 있다", this);
                enabled = false;
                return;
            }

            _renderers = _visual.GetComponentsInChildren<Renderer>();
            _block     = new MaterialPropertyBlock();

            _rangeLine.useWorldSpace   = true;
            _rangeLine.widthMultiplier = LINE_WIDTH;
            _rangeLine.positionCount   = ARC_SEGMENTS + 3;
            _lineMaterial              = new Material(Shader.Find(LINE_SHADER));
            _rangeLine.material        = _lineMaterial;
        }

        /// <summary> 프리팹이 정한 두 반경, 감지와 피격 반경이 서버 몸체 반경이고 간격 반경은 적끼리 간격이다. 콜라이더가 비면 false </summary>
        public bool TryGetBodyRadii(out float detectionRadius, out float separationRadius)
        {
            detectionRadius  = 0f;
            separationRadius = 0f;

            if ((_detectionCollider == null) || (_separationCollider == null))
            {
                return false;
            }

            detectionRadius  = _detectionCollider.radius;
            separationRadius = _separationCollider.radius;
            return true;
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null)
            {
                Destroy(_lineMaterial);
            }
        }

        /// <summary> 서버가 정한 자세와 상태를 그리고 공격 범위를 부채꼴로 보여 준다, 예고 진행도는 0에서 1 </summary>
        public void Apply(Vector3 position, Vector3 forward, EEnemyState state, float telegraphProgress,
            float range, float arcAngle)
        {
            if (!ApplyPose(position, forward, state, telegraphProgress, out Color color))
            {
                return;
            }

            _rangeLine.enabled = state != EEnemyState.Dead;
            DrawFan(position, forward, color, range, arcAngle);
        }

        /// <summary> 서버가 정한 자세와 상태를 그리고 예고와 돌진 중에만 고정된 직선 범위를 보여 준다 </summary>
        public void ApplyDash(Vector3 position, Vector3 forward, EEnemyState state, float telegraphProgress,
            Vector3 dashOrigin, Vector3 dashDirection, float length, float width)
        {
            if (!ApplyPose(position, forward, state, telegraphProgress, out Color color))
            {
                return;
            }

            bool show = (state == EEnemyState.Telegraph) || (state == EEnemyState.Dash);
            _rangeLine.enabled = show;
            if (show)
            {
                DrawRectangle(dashOrigin, dashDirection, color, length, width);
            }
        }

        private bool ApplyPose(Vector3 position, Vector3 forward, EEnemyState state, float telegraphProgress, out Color color)
        {
            color = Color.white;
            if (_renderers == null)
            {
                return false;
            }

            transform.position = position;
            if (forward.sqrMagnitude > 0f)
            {
                transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }

            color = ColorOf(state, telegraphProgress);
            _block.SetColor(COLOR_BASE, color);
            _block.SetColor(COLOR_LEGACY, color);
            for (int i = 0; i < _renderers.Length; ++i)
            {
                _renderers[i].SetPropertyBlock(_block);
            }

            _rangeLine.startColor = color;
            _rangeLine.endColor   = color;
            return true;
        }

        private void DrawFan(Vector3 position, Vector3 forward, Color color, float range, float arcAngle)
        {
            _rangeLine.positionCount = ARC_SEGMENTS + 3;

            Vector3 origin = position + (Vector3.up * RANGE_HEIGHT);
            float   half   = arcAngle * HALF;
            _rangeLine.SetPosition(0, origin);
            for (int i = 0; i <= ARC_SEGMENTS; ++i)
            {
                float angle = Mathf.Lerp(-half, half, (float)i / ARC_SEGMENTS);
                _rangeLine.SetPosition(i + 1, origin + (Quaternion.AngleAxis(angle, Vector3.up) * forward * range));
            }

            _rangeLine.SetPosition(ARC_SEGMENTS + 2, origin);
        }

        // 몸체 중심이 지나가는 직사각형, 시작점에서 방향으로 length, 좌우 각각 width의 절반
        private void DrawRectangle(Vector3 origin, Vector3 direction, Color color, float length, float width)
        {
            _rangeLine.positionCount = RECT_POINTS;

            Vector3 start = origin + (Vector3.up * RANGE_HEIGHT);
            Vector3 side  = new Vector3(direction.z, 0f, -direction.x) * (width * HALF);
            Vector3 end   = start + (direction * length);

            _rangeLine.SetPosition(0, start + side);
            _rangeLine.SetPosition(1, end + side);
            _rangeLine.SetPosition(2, end - side);
            _rangeLine.SetPosition(3, start - side);
            _rangeLine.SetPosition(4, start + side);
        }

        private static Color ColorOf(EEnemyState state, float progress)
        {
            switch (state)
            {
                case EEnemyState.Telegraph:
                    return Color.Lerp(Color.yellow, Color.red, progress);
                case EEnemyState.Dash:
                    return DASH_COLOR;
                case EEnemyState.Recover:
                    return Color.gray;
                case EEnemyState.Dead:
                    return Color.black;
                case EEnemyState.Idle:
                    return Color.white;
                default:
                    return Color.cyan;
            }
        }
    }
}
