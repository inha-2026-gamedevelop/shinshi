// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 월백 하드 2페이즈 P01~P06 표시 뷰 여섯 개를 한 컴포넌트로 묶는다. 보스 프리팹에는 이 컴포넌트 하나만 붙이면 되고, 비어 있는 뷰는 시작할 때 같은 오브젝트에 만든다
    // 여섯 뷰는 도형 풀 하나를 나눠 쓴다. 서버가 확정한 사건을 각 뷰의 공개 메서드로 바꿔 부르는 일은 네트워크 수신부가 한다. 이 클래스는 그 연결을 만들지 않는다
    /// <summary> 하드 2페이즈 표시 뷰 모음, 서버 시각과 도형 풀을 나눠 주고 정리를 한꺼번에 한다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossHardPhaseView : ClientView
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int DEFAULT_MAX_SHAPES = 96; // 격자 24줄과 부채꼴 8개와 원 13개와 난사 예고가 함께 쓴다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private BossStarStageView   _starStage;
        [SerializeField] private BossGridBeamView    _gridBeam;
        [SerializeField] private BossChainFanView    _chainFan;
        [SerializeField] private BossTrackingAreaView _trackingArea;
        [SerializeField] private BossBarrageView     _barrage;
        [SerializeField] private BossCircleBurstView _circleBurst;
        [SerializeField, Min(1)] private int _maxShapes = DEFAULT_MAX_SHAPES;

        private BossGroundShapePool _shapes;
        private BossHardTelegraphViewBase[] _views;

        /****************************************
        *              Properties
        ****************************************/

        public BossStarStageView StarStage => _starStage;

        public BossGridBeamView GridBeam => _gridBeam;

        public BossChainFanView ChainFan => _chainFan;

        public BossTrackingAreaView TrackingArea => _trackingArea;

        public BossBarrageView Barrage => _barrage;

        public BossCircleBurstView CircleBurst => _circleBurst;

        /// <summary> 여섯 뷰가 나눠 쓰는 도형 풀, 시험과 진단용이다 </summary>
        public BossGroundShapePool ShapePool => _shapes;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _shapes = new BossGroundShapePool(_maxShapes);

            _starStage    = EnsureView(_starStage);
            _gridBeam     = EnsureView(_gridBeam);
            _chainFan     = EnsureView(_chainFan);
            _trackingArea = EnsureView(_trackingArea);
            _barrage      = EnsureView(_barrage);
            _circleBurst  = EnsureView(_circleBurst);

            _views = new BossHardTelegraphViewBase[] { _starStage, _gridBeam, _chainFan, _trackingArea, _barrage, _circleBurst };
            for (int i = 0; i < _views.Length; ++i)
            {
                _views[i].UseShapePool(_shapes);
            }
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

        /// <summary> 서버 시각을 읽는 함수를 여섯 뷰에 넘긴다 </summary>
        public void SetServerClock(Func<double> serverClock)
        {
            if (_views == null)
            {
                return;
            }

            for (int i = 0; i < _views.Length; ++i)
            {
                _views[i].SetServerClock(serverClock);
            }
        }

        /// <summary> 진행 중인 표현을 모두 즉시 지운다, 보스 사망과 전투 종료와 접속 종료 때 부른다 </summary>
        public void ClearAll()
        {
            if (_views == null)
            {
                return;
            }

            for (int i = 0; i < _views.Length; ++i)
            {
                _views[i].ClearAll();
            }
        }

        /// <summary> 새 네트워크 관찰을 시작하기 전에 이전 연결의 표현과 중복 방지 기록을 비운다 </summary>
        public void ResetNetworkSession()
        {
            if (_views == null)
            {
                return;
            }

            for (int i = 0; i < _views.Length; ++i)
            {
                _views[i].ResetNetworkSession();
            }
        }

        /// <summary> 같은 번호의 장판이 활성됐다는 사실을 알린다, 추적 장판의 이동 예고를 기존 장판 표현에 넘기려는 것이다 </summary>
        public void NotifyAreaActivated(int areaId)
        {
            if (_trackingArea != null)
            {
                _trackingArea.Activated(areaId);
            }
        }

        private T EnsureView<T>(T view) where T : BossHardTelegraphViewBase
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
