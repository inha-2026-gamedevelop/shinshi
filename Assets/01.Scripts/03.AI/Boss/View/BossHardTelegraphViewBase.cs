// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 임시 도형으로 그리는 위험 구역 표시의 공통 기반이다. 월백 하드 2페이즈와 무철 전장 공격, 충돌 결과가 쓴다.
    // 서버가 확정한 위치와 시각만 임시 도형으로 그리고, 클라이언트는 판정이나 패턴 선택을 하지 않는다
    // 서버 확정 알림이 오기 전에는 스스로 타격하지 않는다. 확정이 오지 않으면 예고 시간이 끝난 뒤 일정 시간만 기다리고 지운다
    // 서버 어셈블리와 Mirror에 의존하지 않는다. 서버 시각은 수신부가 SetServerClock으로 넘기고, 없으면 로컬 시각을 쓰며 시험용이다
    /// <summary> 하드 2페이즈 표시 뷰의 공통 기능, 서버 시각 읽기와 도형 풀과 색과 갱신 주기를 맡는다 </summary>
    public abstract class BossHardTelegraphViewBase : ClientView
    {
        /****************************************
        *              Constants
        ****************************************/

        // 표현 상수다, 밸런스 값이 아니다
        protected const float CONFIRM_GRACE_SECONDS = 0.5f;  // 예고가 끝난 뒤 서버 확정을 더 기다리는 시간
        protected const float FLASH_SECONDS         = 0.3f;  // 타격 순간 번쩍이는 시간
        protected const float FADE_SECONDS          = 0.3f;  // 사라질 때 옅어지는 시간
        protected const int   ENDED_HISTORY         = 32;    // 끝난 번호를 기억하는 수, 늦게 온 알림이 되살리지 못하게 한다
        protected const float BASE_ALPHA            = 0.22f; // 바닥 도형의 투명도
        protected const float FILL_ALPHA            = 0.55f; // 차오르는 채움의 투명도
        protected const float SAFE_ALPHA            = 0.3f;
        protected const float RESIDUAL_ALPHA        = 0.5f;
        protected const float FLASH_BASE_ALPHA      = 0.6f;
        protected const float FLASH_FILL_ALPHA      = 0.9f;
        protected const float MIN_SPAN              = 0.01f;

        private const int DEFAULT_MAX_SHAPES = 48;

        /****************************************
        *                Fields
        ****************************************/

        [Header("색")]
        [SerializeField] private Color _dangerColor = new Color(1f, 0.28f, 0.12f, 1f);
        [SerializeField] private Color _warnColor   = new Color(1f, 0.78f, 0.2f, 1f);
        [SerializeField] private Color _hitColor    = new Color(1f, 0.92f, 0.55f, 1f);
        [SerializeField] private Color _safeColor   = new Color(0.3f, 0.95f, 0.75f, 1f);

        [Header("도형")]
        [SerializeField, Min(1)] private int _maxShapes = DEFAULT_MAX_SHAPES;

        private Func<double>         _serverClock;
        private BossGroundShapePool  _shapes;
        private bool                 _ownsShapes;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 도형 풀, 시험과 진단용이다. 연결하지 않았으면 처음 쓸 때 직접 만든다 </summary>
        public BossGroundShapePool ShapePool
        {
            get
            {
                if (_shapes == null)
                {
                    _shapes     = new BossGroundShapePool(_maxShapes);
                    _ownsShapes = true;
                }

                return _shapes;
            }
        }

        /// <summary> 진행 중인 표현이 있는가 </summary>
        protected abstract bool HasEntries { get; }

        protected Color DangerColor => _dangerColor;

        protected Color WarnColor => _warnColor;

        protected Color HitColor => _hitColor;

        protected Color SafeColor => _safeColor;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            // 표시할 것이 생길 때만 갱신한다
            enabled = false;
        }

        private void Update()
        {
            Refresh();
        }

        // 꺼지거나 파괴될 때 진행 중인 표현을 모두 지운다. 빌린 도형을 돌려줘야 다음에 다시 쓸 수 있다
        private void OnDisable()
        {
            ClearAll();
        }

        private void OnDestroy()
        {
            if ((_ownsShapes) && (_shapes != null))
            {
                _shapes.Dispose();
                _shapes = null;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버 시각을 읽는 함수를 정한다, 네트워크 수신부가 서버 시각 동기화 값을 넘긴다. 비우면 로컬 시각을 쓰며 시험용이다 </summary>
        public void SetServerClock(Func<double> serverClock)
        {
            _serverClock = serverClock;
        }

        /// <summary> 여러 뷰가 한 풀을 나눠 쓰게 한다, 표현을 시작하기 전에 부른다. 넘긴 풀은 이 뷰가 정리하지 않는다 </summary>
        public void UseShapePool(BossGroundShapePool pool)
        {
            if ((_ownsShapes) && (_shapes != null))
            {
                _shapes.Dispose();
            }

            _shapes     = pool;
            _ownsShapes = false;
        }

        /// <summary> 현재 서버 시각으로 한 프레임을 그린다, Update가 매 프레임 부르며 시험은 직접 부른다 </summary>
        public void Refresh()
        {
            if (IsServerBuild)
            {
                return;
            }

            Tick(ReadServerTime());
            if (!HasEntries)
            {
                enabled = false;
            }
        }

        /// <summary> 진행 중인 표현을 모두 즉시 지우고 도형을 돌려준다. 보스 사망과 전투 종료와 접속 종료 때 부른다 </summary>
        public abstract void ClearAll();

        /// <summary> 네트워크 관찰을 다시 시작하기 전에 이전 연결의 표현과 중복 방지 기록을 비운다 </summary>
        public abstract void ResetNetworkSession();

        protected abstract void Tick(double now);

        protected double ReadServerTime()
        {
            return (_serverClock != null) ? _serverClock() : Time.timeAsDouble;
        }

        protected void Wake()
        {
            enabled = true;
        }

        protected void ReleaseShape(ref BossGroundShapeVFX shape)
        {
            if (shape == null)
            {
                return;
            }

            if (_shapes != null)
            {
                _shapes.Release(shape);
            }

            shape = null;
        }

        protected void ReleaseShapes(BossGroundShapeVFX[] shapes)
        {
            for (int i = 0; i < shapes.Length; ++i)
            {
                ReleaseShape(ref shapes[i]);
            }
        }

        /// <summary> from에서 to까지 now가 얼마나 왔는지 0~1, 구간이 없으면 1이다 </summary>
        protected static float Ratio(double now, double from, double to)
        {
            if (to <= from)
            {
                return 1f;
            }

            return Mathf.Clamp01((float)((now - from) / (to - from)));
        }

        protected static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        /// <summary> 서버 확정을 기다리며 차오르는 위험 구역 </summary>
        protected void DrawCharging(BossGroundShapeVFX shape, float charge, float fade)
        {
            shape.Render(charge, WithAlpha(_dangerColor, BASE_ALPHA * fade), WithAlpha(_dangerColor, FILL_ALPHA * fade));
        }

        /// <summary> 같은 모양을 다른 색으로 차오르게 한다 </summary>
        protected void DrawCharging(BossGroundShapeVFX shape, Color color, float charge, float fade)
        {
            shape.Render(charge, WithAlpha(color, BASE_ALPHA * fade), WithAlpha(color, FILL_ALPHA * fade));
        }

        /// <summary> 타격 순간의 번쩍임, age는 타격 뒤 흐른 시간이고 duration이 지나면 투명해진다 </summary>
        protected void DrawFlash(BossGroundShapeVFX shape, float age, float duration)
        {
            float remain = 1f - Mathf.Clamp01(age / duration);
            shape.Render(1f, WithAlpha(_hitColor, FLASH_BASE_ALPHA * remain), WithAlpha(_hitColor, FLASH_FILL_ALPHA * remain));
        }

        /// <summary> 타격이 끝나 안전해진 자리, 바닥만 옅게 그린다 </summary>
        protected void DrawSafe(BossGroundShapeVFX shape, float fade)
        {
            shape.Render(0f, WithAlpha(_safeColor, SAFE_ALPHA * fade), Color.clear);
        }

        /// <summary> 폭발한 뒤에도 남아 있는 위험 </summary>
        protected void DrawResidual(BossGroundShapeVFX shape, float fade)
        {
            shape.Render(1f, WithAlpha(_dangerColor, RESIDUAL_ALPHA * fade), WithAlpha(_dangerColor, RESIDUAL_ALPHA * fade));
        }
    }
}
