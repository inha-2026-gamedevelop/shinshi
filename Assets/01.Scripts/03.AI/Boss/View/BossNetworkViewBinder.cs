// System
using System.Collections.Generic;

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
    public sealed class BossNetworkViewBinder : ClientView, IBossStateListener, IBossHardPhaseStateListener,
        IBossMucheolStateListener
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
        [SerializeField] private MucheolMotionView _mucheolMotion; // 무철 몸 동작, 비어 있으면 같은 오브젝트에서 한 번 찾는다
        [SerializeField] private MucheolPatternVfxView _mucheolVfx; // 무철 일반 패턴 VFX, 비어 있으면 같은 오브젝트에서 한 번 찾는다
        [SerializeField] private WolbaekTalismanView _talismanView; // 서버 복제 계약 연결 전에는 수신 준비만 한다
        [SerializeField] private WolbaekColorWardView _colorWardView; // 50% 복제 계약의 클라이언트 입력점
        [SerializeField] private WolbaekFoxfireSealView _foxfireSealView; // 25% 복제 계약의 클라이언트 입력점
        [SerializeField] private WolbaekFinalAreaView _finalAreaView; // 0% 복제 계약의 클라이언트 입력점
        [SerializeField] private Transform _effectsRoot;

        private BossState _boundState;
        private readonly BossTrackingCastMap _trackingCasts = new BossTrackingCastMap();
        private Transform _detachedEffectsRoot;
        private bool _wasDefeated;
        private bool _justBound;
        private const string WOLBAEK_BOSS_ID = "boss_wolbaek";
        private EBossFlowState _lastFlowState = EBossFlowState.Combat;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if (_state == null)
            {
                _state = GetComponent<BossState>();
            }
            if ((_talismanView == null) && (_view != null))
            {
                _talismanView = _view.GetComponent<WolbaekTalismanView>();
            }
            if ((_colorWardView == null) && (_view != null))
            {
                _colorWardView = _view.GetComponent<WolbaekColorWardView>();
            }
            if ((_foxfireSealView == null) && (_view != null))
            {
                _foxfireSealView = _view.GetComponent<WolbaekFoxfireSealView>();
            }
            if ((_finalAreaView == null) && (_view != null))
            {
                _finalAreaView = _view.GetComponent<WolbaekFinalAreaView>();
            }
            if (_mucheolMotion == null)
            {
                TryGetComponent(out _mucheolMotion);
            }
            if (_mucheolVfx == null)
            {
                TryGetComponent(out _mucheolVfx);
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
                _view.SetDefeated(isDefeated, _justBound && isDefeated);

                if (_state.FlowState != _lastFlowState)
                {
                    _lastFlowState = _state.FlowState;
                    _view.SetFlowState(_lastFlowState);
                }
            }

            if (_mucheolMotion != null)
            {
                _mucheolMotion.SetDefeated(isDefeated);
            }
            if (_mucheolVfx != null)
            {
                _mucheolVfx.SetDefeated(isDefeated);
            }

            // 서버가 사망 직전에 취소 사건을 모두 보내지 못해도 하드 2페이즈와 무철의 위험 구역이 남지 않게 한다
            if ((isDefeated) && (!_wasDefeated))
            {
                ClearPhaseViews();
            }

            _wasDefeated = isDefeated;
            _justBound = false;
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
            if (_mucheolMotion != null)
            {
                _mucheolMotion.ResetSession();
                _mucheolMotion.SetServerClock(ReadServerTime);
            }
            if (_mucheolVfx != null)
            {
                _mucheolVfx.ResetSession();
                _mucheolVfx.SetServerClock(ReadServerTime);
                _mucheolVfx.BindPresenter(_presenter);
            }
            if (_talismanView != null)
            {
                _talismanView.ResetNetworkSession();
                _talismanView.SetServerClock(ReadServerTime);
            }
            if (_colorWardView != null)
            {
                _colorWardView.ResetNetworkSession();
                _colorWardView.SetServerClock(ReadServerTime);
            }
            if (_foxfireSealView != null)
            {
                _foxfireSealView.ResetNetworkSession();
                _foxfireSealView.SetServerClock(ReadServerTime);
            }
            if (_finalAreaView != null)
            {
                _finalAreaView.ResetNetworkSession();
                _finalAreaView.SetServerClock(ReadServerTime);
            }
            _wasDefeated = false;
            _justBound = true;
            _trackingCasts.Clear();
            _lastFlowState = EBossFlowState.Combat;
            if (_view != null)
            {
                _view.Bind(_presenter, ReadServerTime);
            }
            state.PhaseStateChanged += OnPhaseStateChanged;
            // 시계를 먼저 연결해야 늦은 접속 복원이 로컬 경과 시간으로 재생되지 않는다.
            state.SetListener(this);
        }

        /// <summary> 비활성화와 접속 종료 때 풀을 반환하고 다음 연결의 기록과 섞이지 않게 한다 </summary>
        public void Unbind()
        {
            if (_boundState != null)
            {
                _boundState.PhaseStateChanged -= OnPhaseStateChanged;
                _boundState.SetListener(null);
            }

            _boundState = null;
            _justBound = false;
            _trackingCasts.Clear();
            if (_presenter != null)
            {
                _presenter.ClearAll();
            }
            if (_barrierView != null)
            {
                _barrierView.Clear();
            }
            ClearPhaseViews();
            if (_mucheolMotion != null)
            {
                _mucheolMotion.ResetSession();
            }
            if (_mucheolVfx != null)
            {
                _mucheolVfx.ResetSession();
            }
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
            if (_talismanView != null)
            {
                _talismanView.Clear();
            }
            if (_colorWardView != null)
            {
                _colorWardView.Clear();
            }
            if (_foxfireSealView != null)
            {
                _foxfireSealView.Clear();
            }
            if (_finalAreaView != null)
            {
                _finalAreaView.Clear();
            }
        }

        // BossState에 최후의 영역 복제 계약이 추가되면 아래 입력을 사건과 스냅샷에서 호출한다.
        // 서버 전용 IWolbaekFinalAreaListener를 클라이언트 어셈블리에서 직접 구현하지 않는다.
        public void OnFinalStarted(in WolbaekFinalStart start)
        {
            if (_finalAreaView != null) _finalAreaView.StartFinal(in start, ReadServerTime());
        }

        public void OnFinalRestored(in WolbaekFinalSnapshot snapshot)
        {
            if (_finalAreaView != null) _finalAreaView.Restore(in snapshot, ReadServerTime());
        }

        public void OnFinalOuterFloorRemoved(int castId, double serverTime)
        {
            if (_finalAreaView != null) _finalAreaView.OuterFloorRemoved(castId, serverTime, ReadServerTime());
        }

        public void OnFinalYellowsSpawned(int castId, IReadOnlyList<WolbaekFinalYellow> yellows)
        {
            if (_finalAreaView != null) _finalAreaView.YellowsSpawned(castId, yellows, ReadServerTime());
        }

        public void OnFinalYellowDestroyed(int castId, int yellowIndex, double serverTime)
        {
            if (_finalAreaView != null)
                _finalAreaView.YellowDestroyed(castId, yellowIndex, serverTime, ReadServerTime());
        }

        public void OnFinalBundleStarted(int castId, in WolbaekFinalBundle bundle)
        {
            if (_finalAreaView != null) _finalAreaView.BundleStarted(castId, in bundle, ReadServerTime());
        }

        public void OnFinalRedDetached(int castId, int bundleIndex, int redIndex, double serverTime, Vector3 position)
        {
            if (_finalAreaView != null)
                _finalAreaView.RedDetached(castId, bundleIndex, redIndex, serverTime, position, ReadServerTime());
        }

        public void OnFinalRedsLocked(int castId, int bundleIndex, double serverTime, IReadOnlyList<Vector3> centers)
        {
            if (_finalAreaView != null)
                _finalAreaView.LockReds(castId, bundleIndex, serverTime, centers, ReadServerTime());
        }

        public void OnFinalBundleResolved(int castId, int bundleIndex, double serverTime)
        {
            if (_finalAreaView != null)
                _finalAreaView.BundleResolved(castId, bundleIndex, serverTime, ReadServerTime());
        }

        public void OnFinalSucceeded(int castId, double serverTime)
        {
            if (_finalAreaView != null) _finalAreaView.Succeeded(castId, serverTime, ReadServerTime());
        }

        public void OnFinalFailed(int castId) { OnFinalCleared(castId); }
        public void OnFinalHalted(int castId) { OnFinalCleared(castId); }

        public void OnFinalCleared(int castId)
        {
            if (_finalAreaView != null) _finalAreaView.ClearCast(castId);
        }

        public void SetFinalParticipantAnchor(int combatId, Transform anchor)
        {
            if (_finalAreaView != null) _finalAreaView.SetParticipantAnchor(combatId, anchor);
        }

        // BossState에 색 결계 복제 사건과 스냅샷이 들어오면 아래 입력을 연결한다.
        // 서버 전용 IWolbaekColorWardListener를 클라이언트 어셈블리에서 직접 구현하지 않는다.
        public void OnWardStarted(in WolbaekWardStart start)
        {
            if (_colorWardView != null) _colorWardView.StartWard(in start, ReadServerTime());
        }

        public void OnWardRestored(in WolbaekWardSnapshot snapshot)
        {
            if (_colorWardView != null) _colorWardView.Restore(in snapshot, ReadServerTime());
        }

        public void OnGatherHoldStarted(int castId, double serverTime)
        {
            if (_colorWardView != null) _colorWardView.HoldStarted(castId, serverTime);
        }

        public void OnGatherHoldReset(int castId)
        {
            if (_colorWardView != null) _colorWardView.HoldReset(castId);
        }

        public void OnGatherCompleted(int castId)
        {
            if (_colorWardView != null) _colorWardView.GatherCompleted(castId);
        }

        public void OnStageStarted(int castId, in WolbaekWardStage stage)
        {
            if (_colorWardView != null) _colorWardView.StageStarted(castId, in stage, ReadServerTime());
        }

        public void OnStrikeTelegraphed(int castId, in WolbaekWardStrike strike)
        {
            if (_colorWardView != null) _colorWardView.StrikeTelegraphed(castId, in strike, ReadServerTime());
        }

        public void OnStrikeResolved(int castId, int stageIndex, int strikeIndex, double strikeServerTime)
        {
            if (_colorWardView != null) _colorWardView.StrikeResolved(castId, stageIndex, strikeIndex, strikeServerTime, ReadServerTime());
        }

        public void OnGraceStarted(int castId, int combatId, double graceEndServerTime)
        {
            if (_colorWardView != null) _colorWardView.GraceStarted(castId, combatId, graceEndServerTime);
        }

        public void OnGraceEnded(int castId, int combatId)
        {
            if (_colorWardView != null) _colorWardView.GraceEnded(castId, combatId);
        }

        public void OnLinkDamaged(int castId, double serverTime)
        {
            if (_colorWardView != null) _colorWardView.LinkDamaged(castId, serverTime);
        }

        public void OnReviveWindowOpened(int castId)
        {
            if (_colorWardView != null) _colorWardView.ReviveWindowOpenedAt(castId);
        }

        public void SetWardMemberAnchor(int combatId, Transform anchor)
        {
            if (_colorWardView != null) _colorWardView.SetMemberAnchor(combatId, anchor);
        }

        public void OnWardSucceeded(int castId)
        {
            if (_colorWardView == null) return;
            _colorWardView.Succeeded(castId);
            if ((_boundState != null) && (_boundState.PhaseState != EBossPhaseState.Active))
            {
                _colorWardView.PhaseTransitionStarted();
            }
        }
        public void OnWardFailed(int castId) { OnWardCleared(castId); }
        public void OnWardHalted(int castId) { OnWardCleared(castId); }

        public void OnWardCleared(int castId)
        {
            if (_colorWardView != null) _colorWardView.ClearCast(castId);
        }

        private void OnPhaseStateChanged(EBossPhaseState state)
        {
            if ((state != EBossPhaseState.Active) && (_colorWardView != null))
            {
                _colorWardView.PhaseTransitionStarted();
            }
        }

        // BossState에 25% 봉인 사건과 스냅샷이 들어오면 아래 입력을 연결한다.
        // 서버 전용 IWolbaekFoxfireSealListener를 클라이언트 어셈블리에서 직접 구현하지 않는다.
        public void OnSealStarted(in WolbaekSealStart start)
        {
            if (_foxfireSealView != null) _foxfireSealView.StartSeal(in start, ReadServerTime());
        }

        public void OnSealRestored(in WolbaekSealSnapshot snapshot)
        {
            if (_foxfireSealView != null) _foxfireSealView.Restore(in snapshot, ReadServerTime());
        }

        public void OnSealTurnStarted(int castId, in WolbaekSealTurn turn)
        {
            if (_foxfireSealView != null) _foxfireSealView.TurnStarted(castId, in turn, ReadServerTime());
        }

        public void OnSealTrackingStarted(int castId, int turnIndex, double serverTime)
        {
            if (_foxfireSealView != null)
                _foxfireSealView.TrackingStarted(castId, turnIndex, serverTime, ReadServerTime());
        }

        public void OnSealFoxfireDetached(int castId, int turnIndex, int foxfireIndex, double serverTime, Vector3 position)
        {
            if (_foxfireSealView != null)
                _foxfireSealView.FoxfireDetached(castId, turnIndex, foxfireIndex, serverTime, position, ReadServerTime());
        }

        public void OnSealSweepTelegraphed(int castId, in WolbaekSealSweep sweep)
        {
            if (_foxfireSealView != null) _foxfireSealView.SweepTelegraphed(castId, in sweep, ReadServerTime());
        }

        public void OnSealSweepResolved(int castId, int turnIndex, int sweepIndex, double serverTime, int hitCount)
        {
            if (_foxfireSealView != null) _foxfireSealView.SweepResolved(castId, turnIndex, sweepIndex, serverTime);
        }

        public void OnSealFoxfiresLocked(int castId, int turnIndex, double serverTime, IReadOnlyList<Vector3> centers)
        {
            if (_foxfireSealView != null)
                _foxfireSealView.FoxfiresLocked(castId, turnIndex, serverTime, centers, ReadServerTime());
        }

        public void OnSealTurnResolved(int castId, int turnIndex, double serverTime, int destroyedCores,
            int totalCores, int playerHits, bool succeeded, int successCount)
        {
            if (_foxfireSealView != null)
                _foxfireSealView.TurnResolved(castId, turnIndex, serverTime, successCount, ReadServerTime());
        }

        public void OnSealSucceeded(int castId, double serverTime, double counterEndServerTime)
        {
            if (_foxfireSealView != null)
                _foxfireSealView.Succeeded(castId, serverTime, counterEndServerTime, ReadServerTime());
        }

        public void OnSealCounterEnded(int castId, double serverTime, bool reachedNextBoundary)
        {
            if (_foxfireSealView != null) _foxfireSealView.CounterEnded(castId);
        }

        public void OnSealFailed(int castId, double serverTime, EWolbaekSealFail reason)
        {
            if (_foxfireSealView != null) _foxfireSealView.ClearCast(castId);
        }

        public void OnSealHalted(int castId, double serverTime, EWolbaekSealHaltReason reason)
        {
            if (_foxfireSealView != null) _foxfireSealView.ClearCast(castId);
        }

        // EBossInterruptReason은 서버 전용 어셈블리에 있으므로 복제 계층은 정리 사건에서 시전 번호만 넘긴다.
        public void OnSealCleared(int castId)
        {
            if (_foxfireSealView != null) _foxfireSealView.ClearCast(castId);
        }

        public void SetSealMemberAnchor(int combatId, Transform anchor)
        {
            if (_foxfireSealView != null) _foxfireSealView.SetMemberAnchor(combatId, anchor);
        }

        // BossState에 부적진 복제 계약이 추가되면 아래 입력을 해당 사건과 스냅샷에서 호출한다.
        // 현재 BossState에는 그 사건이 없어 이 메서드만으로 실제 서버 표시가 시작되지는 않는다.
        public void OnTalismanStarted(in WolbaekTalismanStart start)
        {
            if (_talismanView != null) _talismanView.StartFormation(in start, ReadServerTime());
        }

        public void OnTalismanRestored(in WolbaekTalismanSnapshot snapshot)
        {
            if (_talismanView != null) _talismanView.Restore(in snapshot, ReadServerTime());
        }

        public void OnTalismanHoldStarted(int castId, double serverTime)
        {
            if (_talismanView != null) _talismanView.HoldStarted(castId, serverTime, ReadServerTime());
        }

        public void OnTalismanHoldReset(int castId, double serverTime)
        {
            if (_talismanView != null) _talismanView.HoldReset(castId, serverTime, ReadServerTime());
        }

        public void OnTalismanAttackTelegraphed(int castId, in WolbaekTalismanAttack attack)
        {
            if (_talismanView != null) _talismanView.AttackTelegraphed(castId, in attack, ReadServerTime());
        }

        public void OnTalismanAttackStruck(int castId, int attackId, double strikeServerTime)
        {
            if (_talismanView != null) _talismanView.AttackStruck(castId, attackId, strikeServerTime, ReadServerTime());
        }

        public void OnTalismanAttackCancelled(int castId, int attackId)
        {
            if (_talismanView != null) _talismanView.AttackCancelled(castId, attackId);
        }

        public void OnTalismanSucceeded(int castId, double serverTime, double counterEndServerTime)
        {
            if (_talismanView != null) _talismanView.Succeeded(castId, serverTime, counterEndServerTime, ReadServerTime());
        }

        public void OnTalismanCounterEnded(int castId)
        {
            if (_talismanView != null) _talismanView.CounterEnded(castId);
        }

        public void OnTalismanCleared(int castId)
        {
            if (_talismanView != null) _talismanView.ClearCast(castId);
        }

        public void OnTalismanFailed(int castId)
        {
            OnTalismanCleared(castId);
        }

        public void OnTalismanHalted(int castId)
        {
            OnTalismanCleared(castId);
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

            if (IsMucheolMotion)
            {
                _mucheolMotion.BeginTelegraph(start.CastId, start.PatternId, start.StartServerTime, start.TelegraphSeconds);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.BeginBasic(in start);
            }
        }

        public void OnAimChanged(int castId, Vector3 direction, float length, double serverTime)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.UpdateAim(castId, direction, length, serverTime);
            }

            if (IsMucheolVfx)
            {
                _mucheolVfx.UpdateAim(castId, direction);
            }
        }

        public void OnDirectionLocked(int castId, Vector3 direction, float length)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.LockDirection(castId, direction, length);
            }

            if (IsMucheolVfx)
            {
                _mucheolVfx.UpdateAim(castId, direction);
            }
        }

        public void OnStruck(int castId, double serverTime)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.Strike(castId, serverTime);
            }

            if (IsMucheolVfx)
            {
                _mucheolVfx.ConfirmBasicStrike(castId, serverTime);
            }
        }

        public void OnDashStarted(int castId, float distance, float seconds, float speed, double startServerTime)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.BeginDash(castId, distance, seconds, speed, startServerTime);
            }

            if (IsMucheolMotion)
            {
                _mucheolMotion.BeginDash(castId, distance, seconds, speed, startServerTime);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.BeginBasicDash(castId, distance, seconds, speed, startServerTime);
            }
        }

        public void OnDashStopped(int castId, Vector3 position, bool stoppedByWall)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.StopDash(castId, position, stoppedByWall);
            }

            if (IsMucheolMotion)
            {
                _mucheolMotion.StopDash(castId, 0);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.StopBasicDash(castId, position);
            }
        }

        public void OnFinished(int castId)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.Finish(castId);
            }

            if (IsMucheolMotion)
            {
                _mucheolMotion.FinishCast(castId);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.FinishCast(castId);
            }
        }

        public void OnCancelled(int castId)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.Cancel(castId);
            }

            if (IsMucheolMotion)
            {
                _mucheolMotion.CancelCast(castId);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.CancelCast(castId);
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
            _trackingCasts.Remove(start.AreaId);
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
            BeginEventMotion(start.CastId, EWolbaekEventMotion.SweepHalfMoon, start.StartServerTime, start.TelegraphSeconds);
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
            BeginEventMotion(start.CastId, EWolbaekEventMotion.BeamPair, start.StartServerTime, start.TelegraphSeconds);
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
            BeginEventMotion(start.CastId, EWolbaekEventMotion.StarRotating, start.StartServerTime, start.TelegraphSeconds);
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

            if (IsMucheolMotion)
            {
                BossTelegraphStart start = snapshot.Start;
                _mucheolMotion.BeginTelegraph(start.CastId, start.PatternId, start.StartServerTime, start.TelegraphSeconds);
                if ((snapshot.Phase == EBossTelegraphPhase.Execute) && (start.Shape == EBossTelegraphShape.Dash))
                {
                    _mucheolMotion.BeginDash(start.CastId, snapshot.DashDistance, snapshot.DashSeconds, snapshot.DashSpeed,
                        snapshot.ExecuteServerTime);
                    if (snapshot.DashStoppedDistance > 0f)
                    {
                        _mucheolMotion.StopDash(start.CastId, 0);
                    }
                }
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.RestoreBasic(in snapshot);
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

        /****************************************
        *       Wolbaek Pattern Motion And VFX
        ****************************************/

        private bool IsWolbaek => (_state != null) && (_state.BossId == WOLBAEK_BOSS_ID);

        private WolbaekPatternVfxView PatternVfx => (_view != null) ? _view.PatternVfx : null;

        // 패턴 번호가 사건에 없는 월백 패턴은 사건 종류로 모션을 정한다, 첫 판정 시각에 힘 순간을 맞춘다
        private void BeginEventMotion(int castId, EWolbaekEventMotion kind, double castStartServerTime, float strikeSeconds)
        {
            if ((_view != null) && IsWolbaek)
            {
                _view.BeginEventMotion(castId, kind, castStartServerTime, strikeSeconds);
            }
        }

        private void EndEventMotion(int castId)
        {
            if ((_view != null) && IsWolbaek)
            {
                _view.EndEventMotion(castId, true);
            }

            if (PatternVfx != null)
            {
                PatternVfx.OnHardCancelled(castId);
            }
        }

        /****************************************
        *         Hard Phase State Listener
        ****************************************/

        public void OnStarStageStarted(in BossStarStageStart start)
        {
            if (_hardPhaseView != null)
            {
                BossStarShape shape = start.Shape;
                _hardPhaseView.StarStage.Begin(start.CastId, start.StarId, in shape, start.StepDegrees, start.StageCount,
                    start.CastStartServerTime, start.FirstStrikeSeconds, start.StrikeIntervalSeconds);
            }

            BeginEventMotion(start.CastId, EWolbaekEventMotion.StarStage, start.CastStartServerTime, start.FirstStrikeSeconds);

            if (PatternVfx != null)
            {
                PatternVfx.OnStarStageStarted(in start);
            }
        }

        public void OnStarStageStruck(int castId, int stageIndex, float rotationDegrees, double scheduledServerTime)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.StarStage.Struck(castId, stageIndex, rotationDegrees, scheduledServerTime);
            }

            if (PatternVfx != null)
            {
                PatternVfx.OnStarStageStruck(castId, stageIndex, scheduledServerTime);
            }
        }

        public void OnStarStageCancelled(int castId)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.StarStage.Cancel(castId);
            }

            EndEventMotion(castId);
        }

        public void OnGridBeamStarted(in BossGridBeamStart start)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.GridBeam.Begin(start.CastId, start.ArenaCenter, start.ArenaRadius, start.LineCount, start.LineWidth,
                    start.LineSpacing, start.CastStartServerTime, start.FirstStrikeSeconds, start.StrikeIntervalSeconds, start.AxisGapSeconds);
            }

            BeginEventMotion(start.CastId, EWolbaekEventMotion.GridBeam, start.CastStartServerTime, start.FirstStrikeSeconds);

            if (PatternVfx != null)
            {
                PatternVfx.OnGridBeamStarted(in start);
            }
        }

        public void OnGridBeamLineTelegraphed(int castId, int lineId, bool isHorizontal, int lineIndex, double telegraphServerTime,
            double strikeServerTime)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.GridBeam.Telegraph(castId, lineId, isHorizontal, lineIndex, telegraphServerTime, strikeServerTime);
            }

            if (PatternVfx != null)
            {
                PatternVfx.OnGridBeamLineTelegraphed(castId, lineId, isHorizontal, lineIndex, telegraphServerTime, strikeServerTime);
            }
        }

        public void OnGridBeamLineStruck(int castId, int lineId, bool isHorizontal, int lineIndex, double scheduledServerTime)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.GridBeam.Strike(castId, lineId, isHorizontal, lineIndex, scheduledServerTime);
            }

            if (PatternVfx != null)
            {
                PatternVfx.OnGridBeamLineStruck(castId, lineId, isHorizontal, lineIndex, scheduledServerTime);
            }
        }

        public void OnGridBeamCancelled(int castId)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.GridBeam.Cancel(castId);
            }

            EndEventMotion(castId);
        }

        public void OnChainFanStarted(in BossChainFanStart start)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.ChainFan.Begin(start.CastId, start.Origin, start.SafeDirection, start.Range, start.ZoneCount, start.ZoneArc,
                    start.CastStartServerTime, start.SafeRevealSeconds, start.FirstBlastSeconds, start.BlastIntervalSeconds,
                    start.LingerSeconds);
            }

            BeginEventMotion(start.CastId, EWolbaekEventMotion.ChainFan, start.CastStartServerTime, start.FirstBlastSeconds);

            if (PatternVfx != null)
            {
                PatternVfx.OnChainFanStarted(in start);
            }
        }

        public void OnChainFanBlasted(int castId, int zoneIndex, double scheduledServerTime)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.ChainFan.Blast(castId, zoneIndex, scheduledServerTime);
            }

            if (PatternVfx != null)
            {
                PatternVfx.OnChainFanBlasted(castId, zoneIndex, scheduledServerTime);
            }
        }

        public void OnChainFanDangerEnded(int castId, double scheduledServerTime)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.ChainFan.EndDanger(castId, scheduledServerTime);
            }
        }

        public void OnChainFanCancelled(int castId)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.ChainFan.Cancel(castId);
            }

            EndEventMotion(castId);
        }

        public void OnTrackingAreaStarted(in BossTrackingAreaStart start)
        {
            _trackingCasts.Add(start.AreaId, start.CastId);
            if (_hardPhaseView != null)
            {
                _hardPhaseView.TrackingArea.BeginMoving(start.CastId, start.AreaId, start.TargetCombatId, start.Center, start.Radius,
                    start.StartServerTime, start.FollowSeconds, start.FixedSeconds);
            }

            BeginEventMotion(start.CastId, EWolbaekEventMotion.TrackingArea, start.StartServerTime, start.FollowSeconds);
        }

        public void OnTrackingAreaMoved(int areaId, Vector3 center, double serverTime)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.TrackingArea.UpdateMoving(areaId, center, serverTime);
            }
        }

        public void OnTrackingAreaFixed(int areaId, Vector3 center, double fixedServerTime)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.TrackingArea.FixCenter(areaId, center, fixedServerTime);
            }
        }

        public void OnTrackingAreaCancelled(int areaId)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.TrackingArea.Cancel(areaId);
            }

            if (_trackingCasts.TryCancel(areaId, out int castId, out bool isLastOfCast) && isLastOfCast)
            {
                EndEventMotion(castId);
            }
        }

        public void OnBarrageStarted(in BossBarrageStart start)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.Barrage.Begin(start.CastId, start.Origin, start.CastStartServerTime, start.ShotCount, start.TelegraphSeconds,
                    start.ShotIntervalSeconds, start.Width);
            }

            BeginEventMotion(start.CastId, EWolbaekEventMotion.Barrage, start.CastStartServerTime, start.TelegraphSeconds);

            if (PatternVfx != null)
            {
                PatternVfx.OnBarrageStarted(in start);
            }
        }

        public void OnBarrageShotTelegraphed(in BossBarrageShot shot)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.Barrage.TelegraphShot(shot.CastId, shot.ShotIndex, shot.SectorIndex, shot.Direction, shot.PathLength,
                    shot.ScheduledServerTime);
            }
        }

        public void OnBarrageShotFired(int castId, int shotIndex, int projectileId, bool isLaunched, double scheduledServerTime)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.Barrage.FireShot(castId, shotIndex, isLaunched, scheduledServerTime);
            }
        }

        public void OnBarrageCancelled(int castId)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.Barrage.Cancel(castId);
            }

            EndEventMotion(castId);
        }

        public void OnCircleBurstStarted(in BossCircleBurstStart start)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.CircleBurst.Begin(start.CastId, start.Centers, start.Radii, start.CastStartServerTime, start.StrikeSeconds);
            }

            BeginEventMotion(start.CastId, EWolbaekEventMotion.CircleBurst, start.CastStartServerTime, start.StrikeSeconds);

            if (PatternVfx != null)
            {
                PatternVfx.OnCircleBurstStarted(in start);
            }
        }

        public void OnCircleBurstStruck(int castId, double scheduledServerTime)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.CircleBurst.Struck(castId, scheduledServerTime);
            }

            if (PatternVfx != null)
            {
                PatternVfx.OnCircleBurstStruck(castId, scheduledServerTime);
            }
        }

        public void OnCircleBurstCancelled(int castId)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.CircleBurst.Cancel(castId);
            }

            EndEventMotion(castId);
        }

        // 복원 사건은 시작 시각을 그대로 사용한다. 이미 지난 일회성 타격 효과는 호출하지 않는다.
        public void OnStarStageRestored(in BossStarStageSnapshot snapshot)
        {
            if (_hardPhaseView == null)
            {
                return;
            }

            BossStarStageStart start = snapshot.Start;
            BossStarShape shape = start.Shape;
            _hardPhaseView.StarStage.Begin(start.CastId, start.StarId, in shape, start.StepDegrees, start.StageCount,
                start.CastStartServerTime, start.FirstStrikeSeconds, start.StrikeIntervalSeconds, snapshot.StruckCount);

            BeginEventMotion(start.CastId, EWolbaekEventMotion.StarStage, start.CastStartServerTime, start.FirstStrikeSeconds);
            if (PatternVfx != null)
            {
                PatternVfx.RestoreStarStage(in snapshot);
            }
        }

        public void OnGridBeamRestored(in BossGridBeamSnapshot snapshot)
        {
            if (_hardPhaseView == null)
            {
                return;
            }

            BossGridBeamStart start = snapshot.Start;
            _hardPhaseView.GridBeam.Begin(start.CastId, start.ArenaCenter, start.ArenaRadius, start.LineCount, start.LineWidth,
                start.LineSpacing, start.CastStartServerTime, start.FirstStrikeSeconds, start.StrikeIntervalSeconds, start.AxisGapSeconds,
                snapshot.TelegraphedCount, snapshot.StruckCount);

            BeginEventMotion(start.CastId, EWolbaekEventMotion.GridBeam, start.CastStartServerTime, start.FirstStrikeSeconds);
        }

        public void OnChainFanRestored(in BossChainFanSnapshot snapshot)
        {
            if (_hardPhaseView == null)
            {
                return;
            }

            BossChainFanStart start = snapshot.Start;
            _hardPhaseView.ChainFan.Begin(start.CastId, start.Origin, start.SafeDirection, start.Range, start.ZoneCount, start.ZoneArc,
                start.CastStartServerTime, start.SafeRevealSeconds, start.FirstBlastSeconds, start.BlastIntervalSeconds, start.LingerSeconds,
                snapshot.BlastedCount, snapshot.IsDangerEnded);

            BeginEventMotion(start.CastId, EWolbaekEventMotion.ChainFan, start.CastStartServerTime, start.FirstBlastSeconds);
            if (PatternVfx != null)
            {
                PatternVfx.RestoreChainFan(in snapshot);
            }
        }

        public void OnTrackingAreaRestored(in BossTrackingAreaSnapshot snapshot)
        {
            if (_hardPhaseView == null)
            {
                return;
            }

            BossTrackingAreaStart start = snapshot.Start;
            _trackingCasts.Add(start.AreaId, start.CastId);
            BeginEventMotion(start.CastId, EWolbaekEventMotion.TrackingArea, start.StartServerTime, start.FollowSeconds);
            _hardPhaseView.TrackingArea.BeginMoving(start.CastId, start.AreaId, start.TargetCombatId, start.Center, start.Radius,
                start.StartServerTime, start.FollowSeconds, start.FixedSeconds);

            if (snapshot.IsFixed)
            {
                _hardPhaseView.TrackingArea.FixCenter(start.AreaId, snapshot.Center, snapshot.FixedServerTime);
            }
            else
            {
                _hardPhaseView.TrackingArea.UpdateMoving(start.AreaId, snapshot.Center, start.StartServerTime);
            }
        }

        public void OnBarrageRestored(in BossBarrageSnapshot snapshot)
        {
            if (_hardPhaseView == null)
            {
                return;
            }

            BossBarrageStart start = snapshot.Start;
            _hardPhaseView.Barrage.Begin(start.CastId, start.Origin, start.CastStartServerTime, start.ShotCount, start.TelegraphSeconds,
                start.ShotIntervalSeconds, start.Width, snapshot.FiredCount);

            BeginEventMotion(start.CastId, EWolbaekEventMotion.Barrage, start.CastStartServerTime, start.TelegraphSeconds);
            if (PatternVfx != null)
            {
                PatternVfx.RestoreBarrage(in snapshot);
            }

            for (int i = 0; (snapshot.Shots != null) && (i < snapshot.Shots.Length); ++i)
            {
                BossBarrageShot shot = snapshot.Shots[i];
                _hardPhaseView.Barrage.TelegraphShot(shot.CastId, shot.ShotIndex, shot.SectorIndex, shot.Direction, shot.PathLength,
                    shot.ScheduledServerTime);
            }
        }

        public void OnCircleBurstRestored(in BossCircleBurstSnapshot snapshot)
        {
            if (_hardPhaseView == null)
            {
                return;
            }

            BossCircleBurstStart start = snapshot.Start;
            _hardPhaseView.CircleBurst.Begin(start.CastId, start.Centers, start.Radii, start.CastStartServerTime, start.StrikeSeconds,
                snapshot.IsStruck);

            if (!snapshot.IsStruck)
            {
                BeginEventMotion(start.CastId, EWolbaekEventMotion.CircleBurst, start.CastStartServerTime, start.StrikeSeconds);
            }
            if (PatternVfx != null)
            {
                PatternVfx.RestoreCircleBurst(in snapshot);
            }
        }

        /****************************************
        *        Mucheol State Listener
        ****************************************/

        // 무철 전용 사건을 받으면 BossState가 이 사건과 복원을 넘긴다. 이 수신부는 몸 동작(MucheolMotionView)과
        // 일반 패턴 VFX(MucheolPatternVfxView)를 잇는다. VFX 원본이 바닥 예고까지 그리므로 단순 도형 표시(BossMucheolPhaseView)는 잇지 않는다.
        // 시각은 모두 서버 시각이고 시전 번호로 중복과 늦은 사건을 거른다. 같은 번호의 사건이 다시 오면 모션이 서버 시각 기준으로 다시 계산된다
        private bool IsMucheolMotion => (_boundState != null) && (_mucheolMotion != null);

        private bool IsMucheolVfx => (_boundState != null) && (_mucheolVfx != null);

        public void OnStrikeChainStarted(in BossStrikeChainStart start)
        {
            if (IsMucheolMotion)
            {
                _mucheolMotion.BeginStrikeChain(in start);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.BeginStrikeChain(in start);
            }
        }

        public void OnStrikeChainAimChanged(EBossStrikeChainKind kind, int castId, Vector3 direction, double serverTime)
        {
            if (IsMucheolVfx)
            {
                _mucheolVfx.UpdateAim(castId, direction);
            }
        }

        public void OnStrikeChainDirectionLocked(EBossStrikeChainKind kind, int castId, Vector3 direction)
        {
            if (IsMucheolVfx)
            {
                _mucheolVfx.UpdateAim(castId, direction);
            }
        }

        // 모션은 시작 사건이 알려 준 타 확정 시각을 이미 키에 넣었다. VFX는 이 확정 사건이 와야 터진다
        public void OnStrikeChainHit(EBossStrikeChainKind kind, int castId, int stepIndex, double serverTime)
        {
            if (IsMucheolVfx)
            {
                _mucheolVfx.ConfirmStrikeChainHit(castId, stepIndex, serverTime);
            }
        }

        public void OnStrikeChainCancelled(EBossStrikeChainKind kind, int castId)
        {
            if (IsMucheolMotion)
            {
                _mucheolMotion.CancelCast(castId);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.CancelCast(castId);
            }
        }

        public void OnMultiDashStarted(in BossMultiDashStart start)
        {
            if (IsMucheolMotion)
            {
                _mucheolMotion.BeginMultiDash(in start);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.BeginMultiDash(in start);
            }
        }

        public void OnMultiDashAimChanged(int castId, int dashIndex, Vector3 direction, float length, double serverTime)
        {
            if (IsMucheolVfx)
            {
                _mucheolVfx.UpdateMultiDashAim(castId, dashIndex, direction);
            }
        }

        public void OnMultiDashDirectionLocked(int castId, int dashIndex, Vector3 direction, float length)
        {
            if (IsMucheolVfx)
            {
                _mucheolVfx.UpdateMultiDashAim(castId, dashIndex, direction);
            }
        }

        public void OnMultiDashReaimStarted(in BossMultiDashReaim reaim)
        {
            if (IsMucheolMotion)
            {
                _mucheolMotion.BeginMultiDashReaim(in reaim);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.BeginMultiDashReaim(in reaim);
            }
        }

        public void OnMultiDashDashStarted(int castId, int dashIndex, float distance, float seconds, float speed, double startServerTime)
        {
            if (IsMucheolMotion)
            {
                _mucheolMotion.BeginMultiDashRun(castId, dashIndex, distance, seconds, speed, startServerTime);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.BeginMultiDashRun(castId, dashIndex, distance, seconds, speed, startServerTime);
            }
        }

        public void OnMultiDashDashStopped(int castId, int dashIndex, Vector3 position, bool stoppedByWall)
        {
            if (IsMucheolMotion)
            {
                _mucheolMotion.StopDash(castId, dashIndex);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.StopMultiDashRun(castId, dashIndex, position);
            }
        }

        public void OnMultiDashTruncated(int castId, int plannedDashCount, int completedDashCount)
        {
            if (IsMucheolMotion)
            {
                _mucheolMotion.EndMultiDashEarly(castId);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.EndMultiDashEarly(castId);
            }
        }

        public void OnMultiDashCancelled(int castId)
        {
            if (IsMucheolMotion)
            {
                _mucheolMotion.CancelCast(castId);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.CancelCast(castId);
            }
        }

        public void OnBattlefieldStarted(in BossBattlefieldStart start)
        {
            if (IsMucheolMotion)
            {
                _mucheolMotion.BeginBattlefield(in start);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.BeginBattlefield(in start);
            }
        }

        public void OnBattlefieldHit(int castId, double serverTime)
        {
            if (IsMucheolVfx)
            {
                _mucheolVfx.ConfirmBattlefield(castId, serverTime);
            }
        }

        public void OnBattlefieldCancelled(int castId)
        {
            if (IsMucheolMotion)
            {
                _mucheolMotion.CancelCast(castId);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.CancelCast(castId);
            }
        }

        // 충돌이 기믹 1의 마지막 구조물 파괴인지 서버가 아직 알리지 않아 모든 충돌을 중간 충돌 반동으로 보인다.
        // TODO(기웅): 서버가 마지막 충돌과 그로기 끝 시각을 보내면 그 충돌만 MucheolMotionView.BeginGroggy로 넘긴다
        public void OnChargeEnded(in BossChargeOutcome outcome)
        {
            if (IsMucheolMotion && outcome.IsCollision)
            {
                _mucheolMotion.PlayChargeImpact(outcome.CastId, outcome.ServerTime);
            }
        }

        public void OnStrikeChainRestored(in BossStrikeChainSnapshot snapshot)
        {
            if (IsMucheolMotion)
            {
                BossStrikeChainStart start = snapshot.Start;
                _mucheolMotion.BeginStrikeChain(in start);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.RestoreStrikeChain(in snapshot);
            }
        }

        // 지금 돌진 하나만 온다. 돌진 중이면 들이받는 구간을, 아니면 모으는 구간을 서버 시각부터 이어 간다
        public void OnMultiDashRestored(in BossMultiDashSnapshot snapshot)
        {
            if (IsMucheolVfx)
            {
                _mucheolVfx.RestoreMultiDash(in snapshot);
            }

            if (!IsMucheolMotion)
            {
                return;
            }

            BossMultiDashStart start = snapshot.Start;
            _mucheolMotion.BeginMultiDash(in start);
            if (snapshot.DashIndex > 0)
            {
                var reaim = new BossMultiDashReaim(start.CastId, snapshot.DashIndex, snapshot.Origin, snapshot.Direction,
                    snapshot.Length, snapshot.Width, snapshot.TelegraphStartServerTime, snapshot.TelegraphSeconds);
                _mucheolMotion.BeginMultiDashReaim(in reaim);
            }

            if (snapshot.HasDashStarted)
            {
                _mucheolMotion.BeginMultiDashRun(start.CastId, snapshot.DashIndex, snapshot.DashDistance, snapshot.DashSeconds,
                    snapshot.DashSpeed, snapshot.DashStartServerTime);
                if (snapshot.StoppedDistance > 0f)
                {
                    _mucheolMotion.StopDash(start.CastId, snapshot.DashIndex);
                }
            }

            if (snapshot.IsTruncated)
            {
                _mucheolMotion.EndMultiDashEarly(start.CastId);
            }
        }

        public void OnBattlefieldRestored(in BossBattlefieldSnapshot snapshot)
        {
            if (IsMucheolMotion)
            {
                BossBattlefieldStart start = snapshot.Start;
                _mucheolMotion.BeginBattlefield(in start);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.RestoreBattlefield(in snapshot);
            }
        }

        // 회복 중에 접속했으면 반동은 이미 끝났다. 반동 구간이 남아 있을 때만 이어 보인다
        public void OnChargeRestored(in BossChargeOutcome outcome)
        {
            OnChargeEnded(in outcome);
        }
    }
}
