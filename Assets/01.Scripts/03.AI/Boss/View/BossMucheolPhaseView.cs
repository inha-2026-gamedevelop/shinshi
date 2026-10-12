// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 무철 전용 표시 뷰 다섯 개(콤보, 순차 타격, 연속 돌진, 전장 공격, 충돌 결과)를 한 컴포넌트로 묶는다. 보스 프리팹에는 이 컴포넌트 하나만 붙이면 되고, 비어 있는 뷰는 시작할 때 같은 오브젝트에 만든다
    // 콤보, 순차 타격, 연속 돌진, 전장 공격의 예고는 같은 오브젝트의 기존 예고 표현(BossTelegraphPresenter)으로 그린다. 서버 사건을 각 뷰의 공개 메서드로 바꿔 부르는 일은 네트워크 수신부가 하고 이 클래스는 그 연결을 만들지 않는다
    /// <summary> 무철 표시 뷰 모음, 예고 표현과 서버 시각과 도형 풀을 나눠 주고 정리를 한꺼번에 한다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossMucheolPhaseView : ClientView
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int DEFAULT_MAX_SHAPES = 24; // 전장 공격 안전 방향 하나와 충돌 표시 둘이면 충분하다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private BossTelegraphPresenter    _presenter;
        [SerializeField] private BossMucheolComboView      _combo;
        [SerializeField] private BossMucheolSequenceView   _sequence;
        [SerializeField] private BossMucheolMultiDashView  _multiDash;
        [SerializeField] private BossMucheolBattlefieldView _battlefield;
        [SerializeField] private BossMucheolChargeImpactView _chargeImpact;
        [SerializeField, Min(1)] private int _maxShapes = DEFAULT_MAX_SHAPES;

        private BossGroundShapePool _shapes;

        /****************************************
        *              Properties
        ****************************************/

        public BossMucheolComboView Combo => _combo;

        public BossMucheolSequenceView Sequence => _sequence;

        public BossMucheolMultiDashView MultiDash => _multiDash;

        public BossMucheolBattlefieldView Battlefield => _battlefield;

        public BossMucheolChargeImpactView ChargeImpact => _chargeImpact;

        /// <summary> 도형을 쓰는 두 뷰가 나눠 쓰는 풀, 시험과 진단용이다 </summary>
        public BossGroundShapePool ShapePool => _shapes;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if (_presenter == null)
            {
                TryGetComponent(out _presenter);
            }

            _shapes = new BossGroundShapePool(_maxShapes);

            _combo        = EnsureView(_combo);
            _sequence     = EnsureView(_sequence);
            _multiDash    = EnsureView(_multiDash);
            _battlefield  = EnsureView(_battlefield);
            _chargeImpact = EnsureView(_chargeImpact);

            _battlefield.UseShapePool(_shapes);
            _chargeImpact.UseShapePool(_shapes);
            SetPresenter(_presenter);
        }

        private void OnDestroy()
        {
            if (_shapes != null)
            {
                _shapes.Dispose();
                _shapes = null;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 예고를 그릴 표현 진입점을 네 뷰에 넘긴다 </summary>
        public void SetPresenter(BossTelegraphPresenter presenter)
        {
            _presenter = presenter;
            if (_combo == null)
            {
                return;
            }

            _combo.SetPresenter(presenter);
            _sequence.SetPresenter(presenter);
            _multiDash.SetPresenter(presenter);
            _battlefield.SetPresenter(presenter);
        }

        /// <summary> 서버 시각을 읽는 함수를 모든 뷰에 넘긴다 </summary>
        public void SetServerClock(Func<double> serverClock)
        {
            if (_combo == null)
            {
                return;
            }

            _combo.SetServerClock(serverClock);
            _sequence.SetServerClock(serverClock);
            _multiDash.SetServerClock(serverClock);
            _battlefield.SetServerClock(serverClock);
            _chargeImpact.SetServerClock(serverClock);
        }

        /// <summary> 진행 중인 표현을 모두 지운다, 보스 사망과 전투 종료와 접속 종료 때 부른다 </summary>
        public void ClearAll()
        {
            if (_combo == null)
            {
                return;
            }

            _combo.ClearAll();
            _sequence.ClearAll();
            _multiDash.ClearAll();
            _battlefield.ClearAll();
            _chargeImpact.ClearAll();
        }

        /// <summary> 새 네트워크 관찰을 시작하기 전에 이전 연결의 표현과 중복 방지 기록을 비운다 </summary>
        public void ResetNetworkSession()
        {
            if (_combo == null)
            {
                return;
            }

            _combo.ResetNetworkSession();
            _sequence.ResetNetworkSession();
            _multiDash.ResetNetworkSession();
            _battlefield.ResetNetworkSession();
            _chargeImpact.ResetNetworkSession();
        }

        private T EnsureView<T>(T view) where T : MonoBehaviour
        {
            if (view != null)
            {
                return view;
            }

            if (TryGetComponent(out T existing))
            {
                return existing;
            }

            return gameObject.AddComponent<T>();
        }
    }
}
