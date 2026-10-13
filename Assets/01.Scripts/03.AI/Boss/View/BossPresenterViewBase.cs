// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 무철 전용 표시를 기존 예고 표현 진입점(BossTelegraphPresenter)의 부채꼴, 원형, 직사각형, 돌진 예고로 바꿔 부르는 어댑터의 공통 기반이다
    // 입력은 서버 알림 인자에서 클라이언트 안전한 값만 옮긴 것이다. 서버 어셈블리 타입이나 Net 계약 이름을 참조하거나 새로 정하지 않는다
    // 서버 시각은 수신부가 SetServerClock으로 넘기고, 위험 범위와 시각은 받은 값 그대로 그린다. 타격은 서버 확정이 있을 때만 그린다
    /// <summary> 예고 표현 진입점을 부리는 표시 뷰의 공통 기능, 진입점 연결과 서버 시각과 시전 묶음 정리를 맡는다 </summary>
    public abstract class BossPresenterViewBase : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private BossTelegraphPresenter _presenter;

        private readonly BossSubCastKeys _keys   = new BossSubCastKeys();
        private readonly List<int>       _groups = new List<int>();

        private Func<double> _serverClock;

        /****************************************
        *              Properties
        ****************************************/

        public BossTelegraphPresenter Presenter => _presenter;

        /// <summary> 예고별 번호 발급기, 시험과 진단용이다 </summary>
        public BossSubCastKeys Keys => _keys;

        /// <summary> 정리를 기다리는 시전 수, 시험과 진단용이다 </summary>
        public int TrackedCastCount => _groups.Count;

        /****************************************
        *              Unity Event
        ****************************************/

        // 꺼지거나 파괴될 때 진행 중인 시전을 모두 접는다
        private void OnDisable()
        {
            ClearAll();
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SetPresenter(BossTelegraphPresenter presenter)
        {
            _presenter = presenter;
        }

        /// <summary> 서버 시각을 읽는 함수를 정한다, 비우면 로컬 시각을 쓰며 시험용이다 </summary>
        public void SetServerClock(Func<double> serverClock)
        {
            _serverClock = serverClock;
        }

        /// <summary> 진행 중인 시전의 아직 발동하지 않은 예고를 모두 취소한다. 보스 사망과 전투 종료와 접속 종료 때 부른다 </summary>
        public virtual void ClearAll()
        {
            if (_presenter != null)
            {
                for (int i = 0; i < _groups.Count; ++i)
                {
                    _presenter.CancelGroup(_groups[i]);
                }
            }

            _groups.Clear();
            _keys.Clear();
        }

        /// <summary> 새 네트워크 관찰을 시작하기 전에 이전 연결의 기록을 비운다 </summary>
        public virtual void ResetNetworkSession()
        {
            ClearAll();
        }

        protected double ReadServerTime()
        {
            return (_serverClock != null) ? _serverClock() : Time.timeAsDouble;
        }

        // 예고 표현은 그릴 것이 없으면 스스로 꺼진다, 새 예고마다 다시 켠다. 진입점이 없으면 아무것도 그리지 않는다
        protected BossTelegraphPresenter PreparePresenter()
        {
            if (_presenter == null)
            {
                return null;
            }

            _presenter.enabled = true;
            return _presenter;
        }

        protected void TrackGroup(int castId)
        {
            if (!_groups.Contains(castId))
            {
                _groups.Add(castId);
            }
        }

        // 시전이 모두 끝났으면 번호를 잊는다
        protected void ForgetCast(int castId)
        {
            _groups.Remove(castId);
            _keys.Forget(castId);
        }

        /// <summary> 한 시전의 아직 발동하지 않은 예고를 취소하고 잊는다 </summary>
        protected bool CancelCast(int castId)
        {
            BossTelegraphPresenter presenter = _presenter;
            bool any = (presenter != null) && presenter.CancelGroup(castId);
            ForgetCast(castId);
            return any;
        }
    }
}
