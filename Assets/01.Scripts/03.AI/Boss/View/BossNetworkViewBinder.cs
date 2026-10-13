// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    /// <summary> 서버 복제 상태와 공격 기록을 클라이언트 보스 표현에 연결한다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossNetworkViewBinder : ClientView, IBossStateListener
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private BossState _state;
        [SerializeField] private BossView _view;
        [SerializeField] private BossTelegraphPresenter _presenter;
        [SerializeField] private BossBarrierView _barrierView;
        [SerializeField] private BossHardPhaseView _hardPhaseView; // 월백 하드 2페이즈 표시, 비어 있으면 생략한다
        [SerializeField] private BossMucheolPhaseView _mucheolPhaseView; // 무철 전용 표시, 비어 있으면 생략한다
        [SerializeField] private Transform _effectsRoot;

        private BossState _boundState;
        private Transform _detachedEffectsRoot;
        private bool _wasDefeated;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if (_state == null)
            {
                _state = GetComponent<BossState>();
            }

            // 공격 예고는 서버의 월드 좌표다. 보스가 이동해도 풀에서 빌린 지면 표현은 따라가지 않는다.
            if ((_effectsRoot != null) && (_effectsRoot != transform) && (_effectsRoot.IsChildOf(transform)))
            {
                _detachedEffectsRoot = _effectsRoot;
                _effectsRoot.SetParent(null, true);
            }
        }

        private void Update()
        {
            if ((_state == null) || (!_state.isClient))
            {
                if (_boundState != null)
                {
                    Unbind();
                }
                return;
            }

            Bind(_state);
            bool isDefeated = _state.LifeState == EParticipantLifeState.Dead;
            if (_view != null)
            {
                _view.SetPose(_state.Position, _state.Forward);
                _view.SetDefeated(isDefeated);
            }

            // 서버가 사망 직전에 취소 사건을 모두 보내지 못해도 하드 2페이즈와 무철의 위험 구역이 남지 않게 한다
            if ((isDefeated) && (!_wasDefeated))
            {
                ClearPhaseViews();
            }

            _wasDefeated = isDefeated;
        }

        private void OnDisable()
        {
            Unbind();
            if (_detachedEffectsRoot != null)
            {
                _detachedEffectsRoot.gameObject.SetActive(false);
            }
        }

        private void OnEnable()
        {
            if (_detachedEffectsRoot != null)
            {
                _detachedEffectsRoot.gameObject.SetActive(true);
            }
        }

        private void OnDestroy()
        {
            // 외부에서 공유한 표현 루트는 소유하지 않는다. 이 개체가 분리한 자식만 함께 정리한다.
            if (_detachedEffectsRoot != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_detachedEffectsRoot.gameObject);
                }
                else
                {
                    DestroyImmediate(_detachedEffectsRoot.gameObject);
                }
                _detachedEffectsRoot = null;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이미 수신한 공격도 복원한다. 같은 상태의 매 프레임 연결은 다시 재생하지 않는다 </summary>
        public void Bind(BossState state)
        {
            if ((IsServerBuild) || (!isActiveAndEnabled) || (state == null) || (_boundState == state))
            {
                return;
            }

            Unbind();
            _boundState = state;
            if (_presenter != null)
            {
                // ClearAll은 같은 연결의 지연 사건을 막는 종료 기록을 보존한다.
                // 새 관찰의 스냅샷은 같은 번호를 다시 담으므로 연결 전에 명시적으로 초기화한다.
                _presenter.ResetNetworkSession();
                _presenter.SetServerClock(ReadServerTime);
            }
            if (_barrierView != null)
            {
                _barrierView.SetServerClock(ReadServerTime);
            }
            if (_hardPhaseView != null)
            {
                _hardPhaseView.ResetNetworkSession();
                _hardPhaseView.SetServerClock(ReadServerTime);
            }
            if (_mucheolPhaseView != null)
            {
                _mucheolPhaseView.ResetNetworkSession();
                _mucheolPhaseView.SetPresenter(_presenter);
                _mucheolPhaseView.SetServerClock(ReadServerTime);
            }
            _wasDefeated = false;
            if (_view != null)
            {
                _view.Bind(_presenter, ReadServerTime);
            }
            // 시계를 먼저 연결해야 늦은 접속 복원이 로컬 경과 시간으로 재생되지 않는다.
            state.SetListener(this);
        }

        /// <summary> 비활성화와 접속 종료 때 풀을 반환하고 다음 연결의 기록과 섞이지 않게 한다 </summary>
        public void Unbind()
        {
            if (_boundState != null)
            {
                _boundState.SetListener(null);
            }

            _boundState = null;
            if (_presenter != null)
            {
                _presenter.ClearAll();
            }
            if (_barrierView != null)
            {
                _barrierView.Clear();
            }
            ClearPhaseViews();
            if (_view != null)
            {
                _view.Bind(null, null);
                _view.ResetView();
            }
        }

        // 보스별 전용 표시를 모두 지운다
        private void ClearPhaseViews()
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.ClearAll();
            }
            if (_mucheolPhaseView != null)
            {
                _mucheolPhaseView.ClearAll();
            }
        }

        private static double ReadServerTime()
        {
            return NetworkTime.time;
        }

        private BossTelegraphPresenter PreparePresenter()
        {
            if ((_boundState == null) || (_presenter == null))
            {
                return null;
            }

            // Presenter는 표시할 공격이 없으면 스스로 꺼진다. 새 사건과 복원 모두 다시 켠다.
            _presenter.enabled = true;
            return _presenter;
        }

        public void OnTelegraphStarted(in BossTelegraphStart start)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.BeginTelegraph(in start);
            }
        }

        public void OnAimChanged(int castId, Vector3 direction, float length, double serverTime)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.UpdateAim(castId, direction, length, serverTime);
            }
        }

        public void OnDirectionLocked(int castId, Vector3 direction, float length)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.LockDirection(castId, direction, length);
            }
        }

        public void OnStruck(int castId, double serverTime)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.Strike(castId, serverTime);
            }
        }

        public void OnDashStarted(int castId, float distance, float seconds, float speed, double startServerTime)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.BeginDash(castId, distance, seconds, speed, startServerTime);
            }
        }

        public void OnDashStopped(int castId, Vector3 position, bool stoppedByWall)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.StopDash(castId, position, stoppedByWall);
            }
        }

        public void OnFinished(int castId)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.Finish(castId);
            }
        }

        public void OnCancelled(int castId)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.Cancel(castId);
            }
        }

        public void OnProjectileLaunched(in BossProjectileStart start)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.LaunchProjectile(in start);
            }
        }

        public void OnProjectileEnded(int projectileId, EBossProjectileFate fate, Vector3 position)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.EndProjectile(projectileId, fate, position);
            }
        }

        public void OnAreaActivated(in BossAreaStart start)
        {
            // 대상 추적 장판의 이동 예고는 같은 장판 번호가 활성되면 기존 장판 표현에 넘기고 지운다
            if ((_boundState != null) && (_hardPhaseView != null))
            {
                _hardPhaseView.NotifyAreaActivated(start.AreaId);
            }

            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.ActivateArea(in start);
            }
        }

        public void OnAreaTicked(int areaId, int hitIndex, double roundServerTime)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.TickArea(areaId, hitIndex, roundServerTime);
            }
        }

        public void OnAreaEnded(int areaId, EBossAreaFate fate)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.EndArea(areaId, fate);
            }
        }

        public void OnSweepStarted(in BossSweepStart start)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.BeginSweepCue(in start);
            }
        }

        public void OnSweepZoneScheduled(in BossTelegraphStart zone)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.BeginTelegraph(in zone);
            }
        }

        public void OnSweepZoneBlasted(int zoneId, double blastServerTime)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.Strike(zoneId, blastServerTime);
            }
        }

        public void OnBeamTelegraphStarted(in BossBeamStart start)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.BeginBeam(in start);
            }
        }

        public void OnBeamCancelled(int beamId)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.CancelBeam(beamId);
            }
        }

        public void OnBeamActivated(int beamId, double activateServerTime)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.ActivateBeam(beamId, activateServerTime);
            }
        }

        public void OnBeamTicked(int beamId, int hitIndex, double roundServerTime)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.TickBeam(beamId, hitIndex, roundServerTime);
            }
        }

        public void OnBeamEnded(int beamId, EBossAreaFate fate)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.EndBeam(beamId, fate);
            }
        }

        public void OnStarTelegraphStarted(in BossStarStart start)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.BeginStar(in start);
            }
        }

        public void OnStarCancelled(int starId)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.CancelStar(starId);
            }
        }

        public void OnStarActivated(int starId, double activateServerTime)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.ActivateStar(starId, activateServerTime);
            }
        }

        public void OnStarTicked(int starId, int hitIndex, double roundServerTime)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.TickStar(starId, hitIndex, roundServerTime);
            }
        }

        public void OnStarEnded(int starId, EBossAreaFate fate)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.EndStar(starId, fate);
            }
        }

        public void OnTelegraphRestored(in BossTelegraphSnapshot snapshot)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.Restore(in snapshot);
            }
        }

        public void OnProjectileRestored(in BossProjectileSnapshot snapshot)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.RestoreProjectile(in snapshot);
            }
        }

        public void OnAreaRestored(in BossAreaSnapshot snapshot)
        {
            if ((_boundState != null) && (_hardPhaseView != null))
            {
                _hardPhaseView.NotifyAreaActivated(snapshot.Start.AreaId);
            }

            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.RestoreArea(in snapshot);
            }
        }

        public void OnBeamRestored(in BossBeamSnapshot snapshot)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.RestoreBeam(in snapshot);
            }
        }

        public void OnStarRestored(in BossStarSnapshot snapshot)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.RestoreStar(in snapshot);
            }
        }

        public void OnSweepCancelled(int castId)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.CancelGroup(castId);
                presenter.ClearSweepCue(castId);
            }
        }

        public void OnBarrierStarted(in BossBarrierStart start)
        {
            if ((_boundState != null) && (_barrierView != null))
            {
                _barrierView.Begin(in start);
            }
        }

        public void OnBarrierRestored(in BossBarrierStart start)
        {
            OnBarrierStarted(in start);
        }

        // 결계 단계는 시작 기록의 서버 시각으로 표현한다. 완료는 위험 구역 제거가 아니다.
        public void OnBarrierActivated(int barrierId, double activateServerTime) { }

        public void OnBarrierCompleted(int barrierId, double completeServerTime) { }

        public void OnBarrierCleared(int barrierId)
        {
            if ((_boundState != null) && (_barrierView != null) && (_barrierView.BarrierId == barrierId))
            {
                _barrierView.Clear();
            }
        }
    }
}
