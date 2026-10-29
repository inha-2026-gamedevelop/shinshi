// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Common;

namespace Shinshi.AI
{
    /// <summary> 서버 복제 상태와 공격 기록을 클라이언트 보스 표현에 연결한다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossNetworkViewBinder : ClientView, IBossStateListener, IBossHardPhaseStateListener,
        IBossMucheolStateListener, IBossWolbaekGimmickStateListener, IBossMucheolSealStateListener,
        IWolbaekPlayerHitEventListener
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
        [SerializeField] private MucheolSoundPlayer _mucheolSound; // 무철 효과음, 비어 있으면 같은 오브젝트에서 한 번 찾고 없으면 소리를 생략한다
        [SerializeField] private MucheolSealView _sealView; // 무철 방향 봉인 표식, 비어 있으면 연결할 때 하위 표현에서 찾는다
        [SerializeField] private WolbaekTalismanView _talismanView; // 부적진 복제 계약의 클라이언트 입력점
        [SerializeField] private WolbaekColorWardView _colorWardView; // 50% 복제 계약의 클라이언트 입력점
        [SerializeField] private WolbaekFoxfireSealView _foxfireSealView; // 25% 복제 계약의 클라이언트 입력점
        [SerializeField] private WolbaekFinalAreaView _finalAreaView; // 0% 복제 계약의 클라이언트 입력점
        [SerializeField] private WolbaekPhase2TransitionPlayer _phase2Transition; // 50% 뒤 2페이즈 전환 컷신, 비어 있으면 보스 표현에서 한 번 찾는다
        [SerializeField] private Transform _effectsRoot;
        [SerializeField] private BossHitFlashView _hitFlash; // 피격 번쩍임, 비어 있으면 보스 표현에서 한 번 찾고 없으면 생략한다
        [SerializeField] private WolbaekHitImpactView _hitImpact; // 플레이어 피격 임팩트, 비어 있으면 한 번 찾고 없으면 생략한다

        private BossState _boundState;

        // 무철 돌진 구조물 표현, 프리팹에 붙이지 않고 이 바인더가 소유한다. 연결을 끊을 때 임시 기둥을 지운다
        private readonly MucheolChargeStructureView _chargeStructures = new MucheolChargeStructureView();
        private readonly BossTrackingCastMap _trackingCasts = new BossTrackingCastMap();
        // 이지 2P 빔과 별의 취소 사건은 시전 번호를 함께 싣는다. 시전 번호가 없는(0) 사건만 시작 사건과 지은 짝으로 그 시전의 모션을 끝낸다
        private readonly BossTrackingCastMap _beamCasts = new BossTrackingCastMap();
        private readonly BossTrackingCastMap _starCasts = new BossTrackingCastMap();
        private Transform _detachedEffectsRoot;
        private bool _wasDefeated;
        private bool _justBound;
        private const string WOLBAEK_BOSS_ID = "boss_wolbaek";
        private const float CHARGE_SOUND_LATE_SECONDS = 0.4f; // 이보다 늦게 받은 충돌과 그로기 소리는 내지 않는다
        private const int NO_CAST_ID = 0; // 공용 시전 번호의 없음 값(CombatIds.NONE)과 같다
        private EBossFlowState _lastFlowState = EBossFlowState.Combat;

        // 50% 성공 뒤 그로기는 서버 페이즈 세대가 성공 순간의 세대보다 커질 때 끝낸다. 성공 Rpc와 세대 SyncVar 중 무엇이 먼저 와도 같다
        private BossParticipantAnchorTracker _anchorTracker;
        private ushort _pendingWardGeneration;
        private bool   _hasPendingWardGeneration;

        // 복제 상태로 받은 마지막 전환 컷신 구간 번호. 너무 이른 구간은 번호를 남기지 않아 다음 프레임에 다시 넘긴다
        private int _seenPhase2TransitionId;

        // 받아들인 전환 구간 동안 맵 붕괴를 서버 시각으로 평가하는 추적. 연결과 해제, 새 구간 수락에서만 바뀐다
        private double _arenaStartServerTime;
        private bool   _isArenaTracking;

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
            if ((_phase2Transition == null) && (_view != null))
            {
                _phase2Transition = _view.GetComponent<WolbaekPhase2TransitionPlayer>();
            }
            if (_hitFlash == null)
            {
                _hitFlash = (_view != null) ? _view.GetComponentInChildren<BossHitFlashView>(true) : GetComponentInChildren<BossHitFlashView>(true);
            }
            if (_hitImpact == null)
            {
                _hitImpact = (_view != null) ? _view.GetComponentInChildren<WolbaekHitImpactView>(true) : GetComponentInChildren<WolbaekHitImpactView>(true);
            }
            if (_mucheolMotion == null)
            {
                TryGetComponent(out _mucheolMotion);
            }
            if (_mucheolVfx == null)
            {
                TryGetComponent(out _mucheolVfx);
            }
            if (_mucheolSound == null)
            {
                TryGetComponent(out _mucheolSound);
            }

            _chargeStructures.Destroyed += OnChargeStructureDestroyedSound;

            _anchorTracker = new BossParticipantAnchorTracker(SetParticipantAnchor);

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
                _view.SetDefeated(isDefeated, _justBound && isDefeated, ElapsedSince(_state.DefeatServerTime));

                if (_state.FlowState != _lastFlowState)
                {
                    _lastFlowState = _state.FlowState;
                    _view.SetFlowState(_lastFlowState, Mathf.Max(0f, ElapsedSince(_state.AppearStartServerTime)));
                }
            }

            // 서버가 복제한 체력이 줄어든 프레임에 몸체를 번쩍인다
            if (_hitFlash != null)
            {
                _hitFlash.Observe(_state.Health);
            }

            if ((IsWolbaek) && (_anchorTracker != null))
            {
                _anchorTracker.Tick(Time.unscaledTimeAsDouble);
            }

            if (IsWolbaek)
            {
                ReceivePhase2Transition(_state);
                TickArenaShatter(ReadServerTime());
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
            _chargeStructures.Destroyed -= OnChargeStructureDestroyedSound;

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
            ResolveSealView();
            if (_sealView != null)
            {
                _sealView.OnSealStopped();
                _sealView.SetServerClock(ReadServerTime);
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
            if (_phase2Transition != null)
            {
                // 전환 번호 기록은 전투 번호 기준이다. 새 연결은 지난 연결의 번호를 이어 쓰지 않는다
                _phase2Transition.SetServerClock(ReadServerTime);
                _phase2Transition.ResetSession(state.CombatId);
            }
            ResetArenaShatter();
            _wasDefeated = false;
            _justBound = true;
            _trackingCasts.Clear();
            _beamCasts.Clear();
            _starCasts.Clear();
            _lastFlowState = EBossFlowState.Combat;
            _hasPendingWardGeneration = false;
            _seenPhase2TransitionId = 0;
            if (_anchorTracker != null)
            {
                _anchorTracker.Clear();
                if (IsWolbaek)
                {
                    _anchorTracker.Tick(Time.unscaledTimeAsDouble);
                }
            }
            if (_view != null)
            {
                _view.Bind(_presenter, ReadServerTime);
            }
            state.PhaseGenerationChanged += OnPhaseGenerationChanged;
            // 시계를 먼저 연결해야 늦은 접속 복원이 로컬 경과 시간으로 재생되지 않는다.
            state.SetListener(this);
        }

        /// <summary> 비활성화와 접속 종료 때 풀을 반환하고 다음 연결의 기록과 섞이지 않게 한다 </summary>
        public void Unbind()
        {
            if (_boundState != null)
            {
                _boundState.PhaseGenerationChanged -= OnPhaseGenerationChanged;
                _boundState.SetListener(null);
            }

            _boundState = null;
            _justBound = false;
            _hasPendingWardGeneration = false;
            if (_hitFlash != null)
            {
                _hitFlash.ResetBaseline();
            }
            if (_anchorTracker != null)
            {
                _anchorTracker.Clear();
            }
            _trackingCasts.Clear();
            _beamCasts.Clear();
            _starCasts.Clear();
            if (_presenter != null)
            {
                _presenter.ClearAll();
            }
            if (_barrierView != null)
            {
                _barrierView.Clear();
            }
            ClearPhaseViews();
            _chargeStructures.Clear();
            if (_mucheolMotion != null)
            {
                _mucheolMotion.ResetSession();
            }
            if (_mucheolVfx != null)
            {
                _mucheolVfx.ResetSession();
            }
            if (_phase2Transition != null)
            {
                _phase2Transition.EndSession();
            }
            ResetArenaShatter();
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
            if (_sealView != null)
            {
                _sealView.OnSealStopped();
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

        /****************************************
        *        Wolbaek Gimmick Listener
        ****************************************/

        // BossState가 월백 기믹 사건(IBossWolbaekGimmickStateListener)을 넘긴다. 서버 전용 기믹 리스너를 클라이언트 어셈블리에서 직접 구현하지 않는다.
        // 표현은 기믹 시작과 복원 때 참가자 앵커를 비우므로 그 직후 추적기가 아는 앵커를 모두 다시 넣는다
        public void OnFinalStarted(in WolbaekFinalStart start)
        {
            if (_finalAreaView == null)
            {
                return;
            }

            _finalAreaView.StartFinal(in start, ReadServerTime());
            InjectAnchors();
        }

        public void OnFinalRestored(in WolbaekFinalSnapshot snapshot)
        {
            if (_finalAreaView != null) _finalAreaView.Restore(in snapshot, ReadServerTime());
        }

        public void OnFinalRestored(in BossWolbaekFinalRestore restore)
        {
            if (_finalAreaView == null)
            {
                return;
            }

            WolbaekFinalSnapshot snapshot = restore.Snapshot;
            _finalAreaView.Restore(in snapshot, ReadServerTime());
            InjectAnchors();
        }

        public void OnFinalOuterFloorRemoved(int castId, double serverTime)
        {
            if (_finalAreaView != null) _finalAreaView.OuterFloorRemoved(castId, serverTime, ReadServerTime());
        }

        public void OnFinalYellowsSpawned(int castId, WolbaekFinalYellow[] yellows)
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

        public void OnFinalRedsLocked(int castId, int bundleIndex, double serverTime, Vector3[] centers)
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

        public void OnWardStarted(in WolbaekWardStart start)
        {
            if (_colorWardView == null)
            {
                return;
            }

            _colorWardView.StartWard(in start, ReadServerTime());
            InjectAnchors();
        }

        public void OnWardRestored(in WolbaekWardSnapshot snapshot)
        {
            if (_colorWardView != null) _colorWardView.Restore(in snapshot, ReadServerTime());
        }

        // 진행 중인 광역 예고는 규칙 스냅샷에 없어 복원 기록이 따로 준다. 예고는 결계 복원 뒤에 다시 그린다
        public void OnWardRestored(in BossWolbaekWardRestore restore)
        {
            if (_colorWardView == null)
            {
                return;
            }

            WolbaekWardSnapshot snapshot = restore.Snapshot;
            double              now      = ReadServerTime();
            _colorWardView.Restore(in snapshot, now);
            if (restore.HasPendingStrike)
            {
                WolbaekWardStrike strike = restore.PendingStrike;
                _colorWardView.StrikeTelegraphed(snapshot.Start.CastInstanceId, in strike, now);
            }

            InjectAnchors();
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

        // 서버는 성공 틱이 끝난 뒤 세대를 올린다. 세대가 이미 성공 값보다 크면 Rpc가 SyncVar보다 늦은 것이라 바로 그로기를 끝낸다
        public void OnWardSucceeded(int castId, ushort phaseGenerationAtSuccess)
        {
            if (_colorWardView == null)
            {
                return;
            }

            _colorWardView.Succeeded(castId);
            if (_boundState == null)
            {
                return;
            }

            if (_boundState.PhaseGeneration > phaseGenerationAtSuccess)
            {
                _hasPendingWardGeneration = false;
                _colorWardView.PhaseTransitionStarted();
                return;
            }

            _pendingWardGeneration    = phaseGenerationAtSuccess;
            _hasPendingWardGeneration = true;
        }
        public void OnWardFailed(int castId) { OnWardCleared(castId); }
        public void OnWardHalted(int castId) { OnWardCleared(castId); }

        public void OnWardCleared(int castId)
        {
            if (_colorWardView != null) _colorWardView.ClearCast(castId);
        }

        // 페이즈 상태가 아니라 세대로 본다. 결계 전개가 없으면 전환 대기가 한 틱이라 10Hz 상태 복제에서 Active 아닌 값이 보이지 않을 수 있다
        private void OnPhaseGenerationChanged(ushort generation)
        {
            if ((!_hasPendingWardGeneration) || (generation <= _pendingWardGeneration))
            {
                return;
            }

            _hasPendingWardGeneration = false;
            if (_colorWardView != null)
            {
                _colorWardView.PhaseTransitionStarted();
            }
        }

        // 서버 전환 구간(BossState.Phase2Transition*)을 ReceivePhase2Transition이 아래 둘로 넘긴다.
        // OnWardSucceeded나 PhaseState 변화로는 컷신을 시작하지 않는다. 그 둘에는 구간 시각과 번호가 없다.
        // 서버는 결계를 대신하는 기믹 묶음이 있을 때만 구간을 열고, 그동안 보스를 무적으로 두고 인계를 미룬다
        /// <summary> 서버 전환 구간 사건, 같은 구간이 복원으로 다시 와도 한 번만 재생한다 </summary>
        public EWolbaekPhase2TransitionDecision OnPhase2TransitionStarted(in WolbaekPhase2TransitionCue cue)
        {
            if (_phase2Transition == null)
            {
                return EWolbaekPhase2TransitionDecision.Invalid;
            }

            double serverNow = ReadServerTime();
            EWolbaekPhase2TransitionDecision decision = _phase2Transition.Receive(in cue, serverNow);
            PresentArenaShatter(decision, in cue, serverNow);
            return decision;
        }

        // 맵 붕괴는 컷신과 같은 서버 구간 신호에만 맞춘다. 서버가 정한 구간 시작에서 지난 시간만큼 건너뛰어 재생하므로
        // 늦은 접속도 같은 장면을 보고, 이미 끝난 구간은 떨어진 뒤의 모습으로 둔다. 결과 판정은 하지 않는다
        // 새로 받아들인 구간만 추적을 시작하고 중복, 지난 세션, 너무 이른 사건은 건드리지 않는다. Scheduled는 서버 시작 시각이 될 때까지 기다린다.
        // 이후 위치는 매 프레임 서버 절대 경과 시간으로 평가한다(TickArenaShatter)
        private void PresentArenaShatter(EWolbaekPhase2TransitionDecision decision, in WolbaekPhase2TransitionCue cue, double serverNow)
        {
            BossArenaShatterView arena = BossArenaShatterView.Current;
            if (arena == null)
            {
                return;
            }

            if ((decision == EWolbaekPhase2TransitionDecision.Play) || (decision == EWolbaekPhase2TransitionDecision.Scheduled))
            {
                arena.ResetToIntact();
                _arenaStartServerTime = cue.StartServerTime;
                _isArenaTracking      = true;
                TickArenaShatter(serverNow);
            }
            else if (decision == EWolbaekPhase2TransitionDecision.Ended)
            {
                arena.ResetToIntact();
                _isArenaTracking = false;
                arena.EvaluateAt(float.MaxValue);
            }
        }

        private void TickArenaShatter(double serverNow)
        {
            if ((!_isArenaTracking) || (serverNow < _arenaStartServerTime))
            {
                return;
            }

            BossArenaShatterView arena = BossArenaShatterView.Current;
            if (arena == null)
            {
                return;
            }

            if (!arena.EvaluateAt((float)(serverNow - _arenaStartServerTime)))
            {
                _isArenaTracking = false;
            }
        }

        private void ResetArenaShatter()
        {
            _isArenaTracking = false;
            if (BossArenaShatterView.Current != null)
            {
                BossArenaShatterView.Current.ResetToIntact();
            }
        }

        // 서버 전환 구간은 복제 상태로 온다. 새 번호만 재생기에 넘기고, 스폰 초기 상태로 받은 진행 중 구간은 복원으로 넘긴다
        private void ReceivePhase2Transition(BossState state)
        {
            int transitionId = state.Phase2TransitionId;
            if ((transitionId <= _seenPhase2TransitionId) || (_phase2Transition == null))
            {
                return;
            }

            var cue = new WolbaekPhase2TransitionCue(state.CombatId, transitionId, state.Phase2TransitionStartServerTime,
                state.Phase2TransitionEndServerTime);
            EWolbaekPhase2TransitionDecision decision = _justBound
                ? OnPhase2TransitionRestored(in cue)
                : OnPhase2TransitionStarted(in cue);
            if (decision != EWolbaekPhase2TransitionDecision.TooEarly)
            {
                _seenPhase2TransitionId = transitionId;
            }
        }

        /// <summary> 늦은 접속, 재접속 스냅샷의 진행 중 전환 구간, 서버 경과 시간부터 재생하고 끝난 구간은 재생하지 않는다 </summary>
        public EWolbaekPhase2TransitionDecision OnPhase2TransitionRestored(in WolbaekPhase2TransitionCue cue)
        {
            return OnPhase2TransitionStarted(in cue);
        }

        public void OnSealStarted(in WolbaekSealStart start)
        {
            if (_foxfireSealView == null)
            {
                return;
            }

            _foxfireSealView.StartSeal(in start, ReadServerTime());
            InjectAnchors();
        }

        public void OnSealRestored(in WolbaekSealSnapshot snapshot)
        {
            if (_foxfireSealView != null) _foxfireSealView.Restore(in snapshot, ReadServerTime());
        }

        public void OnSealRestored(in BossWolbaekSealRestore restore)
        {
            if (_foxfireSealView == null)
            {
                return;
            }

            WolbaekSealSnapshot snapshot = restore.Snapshot;
            _foxfireSealView.Restore(in snapshot, ReadServerTime());
            InjectAnchors();
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

        public void OnSealSweepResolved(int castId, int turnIndex, int sweepIndex, double serverTime)
        {
            if (_foxfireSealView != null) _foxfireSealView.SweepResolved(castId, turnIndex, sweepIndex, serverTime);
        }

        public void OnSealFoxfiresLocked(int castId, int turnIndex, double serverTime, Vector3[] centers)
        {
            if (_foxfireSealView != null)
                _foxfireSealView.FoxfiresLocked(castId, turnIndex, serverTime, centers, ReadServerTime());
        }

        public void OnSealTurnResolved(int castId, int turnIndex, double serverTime, int successCount, byte destroyedCoreMask)
        {
            if (_foxfireSealView != null)
            {
                _foxfireSealView.TurnResolved(castId, turnIndex, serverTime, successCount, destroyedCoreMask, ReadServerTime());
            }
        }

        public void OnSealSucceeded(int castId, double serverTime, double counterEndServerTime)
        {
            if (_foxfireSealView != null)
                _foxfireSealView.Succeeded(castId, serverTime, counterEndServerTime, ReadServerTime());
        }

        public void OnSealCounterEnded(int castId, double serverTime, bool reachedNextBoundary)
        {
            OnSealCounterEnded(castId);
        }

        public void OnSealCounterEnded(int castId)
        {
            if (_foxfireSealView != null)
            {
                _foxfireSealView.CounterEnded(castId);
            }
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

        public void OnTalismanStarted(in WolbaekTalismanStart start)
        {
            if (_talismanView != null) _talismanView.StartFormation(in start, ReadServerTime());
        }

        public void OnTalismanRestored(in WolbaekTalismanSnapshot snapshot)
        {
            if (_talismanView != null) _talismanView.Restore(in snapshot, ReadServerTime());
        }

        // 성공 시각과 마지막 유지 초기화는 규칙 스냅샷에 없어 복원 기록이 따로 준다
        public void OnTalismanRestored(in BossWolbaekTalismanRestore restore)
        {
            if (_talismanView == null)
            {
                return;
            }

            WolbaekTalismanSnapshot snapshot = restore.Snapshot;
            double                  now      = ReadServerTime();
            if (!_talismanView.Restore(in snapshot, now, restore.SuccessServerTime))
            {
                return;
            }

            if ((restore.HasLastReset) && (!snapshot.HasHold) && (snapshot.State != EWolbaekTalismanState.Counter))
            {
                _talismanView.HoldReset(snapshot.Start.CastInstanceId, restore.LastResetServerTime, now);
            }
        }

        public void OnTalismanHoldStarted(int castId, double serverTime)
        {
            if (_talismanView != null) _talismanView.HoldStarted(castId, serverTime, ReadServerTime());
        }

        public void OnTalismanHoldReset(int castId, double serverTime)
        {
            if (_talismanView != null) _talismanView.HoldReset(castId, serverTime, ReadServerTime());
        }

        public void OnTalismanHoldReset(int castId, double serverTime, EWolbaekHoldReset reason, int combatId)
        {
            OnTalismanHoldReset(castId, serverTime);
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

        public void OnWarpDeparted(int castId, Vector3 departure, double serverTime)
        {
            if ((_view != null) && (IsWolbaek))
            {
                _view.SetWarpDeparture(castId, departure);
            }
        }

        public void OnWarpRestored(int castId, Vector3 departure, double serverTime)
        {
            OnWarpDeparted(castId, departure, serverTime);
        }

        // 추적기가 찾은 참가자 몸체를 앵커가 필요한 네 표현에 함께 넣는다. null이면 지운다
        private void SetParticipantAnchor(int combatId, Transform anchor)
        {
            SetWardMemberAnchor(combatId, anchor);
            SetSealMemberAnchor(combatId, anchor);
            SetFinalParticipantAnchor(combatId, anchor);
            if (_talismanView != null)
            {
                _talismanView.SetMemberAnchor(combatId, anchor);
            }
        }

        private void InjectAnchors()
        {
            if (_anchorTracker != null)
            {
                _anchorTracker.InjectAll();
            }
        }

        // 서버 시각을 모르면(NaN) 처치 연출의 모름 값(음수)을 준다
        private static float ElapsedSince(double serverTime)
        {
            if (double.IsNaN(serverTime))
            {
                return WolbaekDeathTimeline.UNKNOWN_ELAPSED;
            }

            return (float)Math.Max(0d, ReadServerTime() - serverTime);
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

        /// <summary> 서버가 확정한 플레이어 피격 한 건을 임팩트로 보인다. 복제 계층이 맞은 대상마다 부른다 </summary>
        public bool OnPlayerHit(in WolbaekPlayerHit hit)
        {
            return (_hitImpact != null) && _hitImpact.Play(in hit, ReadServerTime());
        }

        // 복제 상태(BossState)가 받은 피격 Rpc를 넘기는 자리다. 재생 여부는 진단에만 쓰여 버린다
        void IWolbaekPlayerHitEventListener.OnPlayerHit(in WolbaekPlayerHit hit)
        {
            OnPlayerHit(in hit);
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

        public void OnDashStopped(int castId, Vector3 position, bool stoppedByWall, double stopServerTime)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.StopDash(castId, position, stoppedByWall);
            }

            if (IsMucheolMotion)
            {
                _mucheolMotion.StopDash(castId, 0, stopServerTime);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.StopBasicDash(castId, position, stopServerTime);
            }
        }

        public void OnFinished(int castId)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.Finish(castId);
            }

            // 지면 예고가 먼저 끝나 표현 기록이 지워졌어도 서버 시전 종료(회복 끝)는 월백 모션에 직접 알린다
            if ((_view != null) && (IsWolbaek))
            {
                _view.EndServerCast(castId);
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
            _beamCasts.Add(start.BeamId, start.CastId);
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.BeginBeam(in start);
            }
        }

        // 지난 시전의 빔이면 모션의 시전 번호와 달라 무시된다
        public void OnBeamCancelled(int castId, int beamId)
        {
            EndPairedEventMotion(_beamCasts, castId, beamId);
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
            _beamCasts.Remove(beamId);
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.EndBeam(beamId, fate);
            }
        }

        public void OnStarTelegraphStarted(in BossStarStart start)
        {
            BeginEventMotion(start.CastId, EWolbaekEventMotion.StarRotating, start.StartServerTime, start.TelegraphSeconds);
            _starCasts.Add(start.StarId, start.CastId);
            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter != null)
            {
                presenter.BeginStar(in start);
            }
        }

        public void OnStarCancelled(int castId, int starId)
        {
            EndPairedEventMotion(_starCasts, castId, starId);
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
            _starCasts.Remove(starId);
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
                    if (snapshot.DashStoppedDistance >= 0f)
                    {
                        _mucheolMotion.StopDash(start.CastId, 0, snapshot.DashStoppedServerTime);
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
            EndEventMotion(castId);
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

        // 시전 번호가 있으면 그 시전의 모션을 바로 끝내고, 없으면 시작 사건에서 지은 짝으로 찾는다
        private void EndPairedEventMotion(BossTrackingCastMap casts, int castId, int itemId)
        {
            bool isPaired = casts.TryCancel(itemId, out int pairedCastId, out _);
            if (castId != NO_CAST_ID)
            {
                EndEventMotion(castId);
            }
            else if (isPaired)
            {
                EndEventMotion(pairedCastId);
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

        // 한 시전이 장판을 여럿 깔 수 있어 그 시전의 마지막 장판이 취소될 때만 모션을 끝낸다. 짝을 모르는 장판도 시전 번호가 있으면
        // 그 시전의 남은 장판이 없을 때 끝낸다
        public void OnTrackingAreaCancelled(int castId, int areaId)
        {
            if (_hardPhaseView != null)
            {
                _hardPhaseView.TrackingArea.Cancel(areaId);
            }

            bool isPaired = _trackingCasts.TryCancel(areaId, out int pairedCastId, out bool isLastOfCast);
            int  endCast  = (castId != NO_CAST_ID) ? castId : pairedCastId;
            bool isLast   = isPaired ? isLastOfCast : ((castId != NO_CAST_ID) && (!_trackingCasts.HasCast(castId)));
            if ((isPaired || (castId != NO_CAST_ID)) && isLast)
            {
                EndEventMotion(endCast);
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
                snapshot.TelegraphedCount, snapshot.StruckCount, start.FirstLineId);

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

        public void OnMultiDashDashStopped(int castId, int dashIndex, Vector3 position, bool stoppedByWall, double stopServerTime)
        {
            if (IsMucheolMotion)
            {
                _mucheolMotion.StopDash(castId, dashIndex, stopServerTime);
            }
            if (IsMucheolVfx)
            {
                _mucheolVfx.StopMultiDashRun(castId, dashIndex, position, stopServerTime);
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

        // 충돌은 우선 중간 충돌 반동으로 보인다. 기믹 1의 마지막 구조물 파괴면 서버가 같은 시전의 결과 뒤에 그로기 대기를 보내
        // 그로기가 반동을 대신한다(그로기 VFX는 아직 없다)
        public void OnChargeEnded(in BossChargeOutcome outcome)
        {
            ShowChargeEnded(in outcome);

            // 정상 이동 종료는 소리가 없고 벽이나 구조물에 부딪힌 충돌만 낸다. 늦게 받은 결과는 내지 않는다
            if (outcome.IsCollision && (_mucheolSound != null) && (ReadServerTime() - outcome.ServerTime <= CHARGE_SOUND_LATE_SECONDS))
            {
                _mucheolSound.Play(EMucheolSound.WallCrash, outcome.Position, Mathf.Max(0f, (float)(outcome.ServerTime - ReadServerTime())));
            }
        }

        private void ShowChargeEnded(in BossChargeOutcome outcome)
        {
            if (IsMucheolMotion && outcome.IsCollision)
            {
                _mucheolMotion.PlayChargeImpact(outcome.CastId, outcome.ServerTime);
            }
        }

        // 그로기 시작은 충돌한 시전의 회복이 끝난 뒤라 대기 때는 끝 시각을 모른다. 충돌 시각부터 내려앉아 시작 사건을 기다린다
        public void OnChargeGroggyPending(in BossChargeGroggy groggy)
        {
            if (IsMucheolMotion)
            {
                _mucheolMotion.BeginGroggy(groggy.CastId, groggy.CollisionServerTime, groggy.GroggyEndServerTime);
            }
        }

        // 대기로 이미 내려앉았으면 끝 시각만 잇고, 대기를 받지 못했으면 충돌 시각부터 끝까지 새로 재생한다
        public void OnChargeGroggyStarted(in BossChargeGroggy groggy)
        {
            // 구조물 파괴 순간이 아니라 실제 그로기가 시작됐을 때만 낸다
            double groggyStart = double.IsNaN(groggy.GroggyStartServerTime) ? ReadServerTime() : groggy.GroggyStartServerTime;
            if ((_mucheolSound != null) && (ReadServerTime() - groggyStart <= CHARGE_SOUND_LATE_SECONDS))
            {
                _mucheolSound.Play(EMucheolSound.GroggyEnter, _mucheolSound.transform.position,
                    Mathf.Max(0f, (float)(groggyStart - ReadServerTime())));
            }

            if (!IsMucheolMotion)
            {
                return;
            }

            MucheolMotionDirector director = _mucheolMotion.Director;
            if ((director != null) && (director.IsGroggy) && (director.CastId == groggy.CastId))
            {
                _mucheolMotion.SetGroggyEnd(groggy.CastId, groggy.GroggyEndServerTime);
                return;
            }

            _mucheolMotion.BeginGroggy(groggy.CastId, groggy.CollisionServerTime, groggy.GroggyEndServerTime);
        }

        // 봉인 표식은 보스 표현 프리팹(PF_MucheolBoss)에 붙어 있어 표현이 늦게 붙어도 찾도록 연결할 때와 사건마다 확인한다
        private bool ResolveSealView()
        {
            if ((_sealView == null) && (_view != null))
            {
                _sealView = _view.GetComponentInChildren<MucheolSealView>(true);
            }
            if (_sealView == null)
            {
                _sealView = GetComponentInChildren<MucheolSealView>(true);
            }
            return _sealView != null;
        }

        public void OnSealRevealed(int requiredHits)
        {
            if (ResolveSealView())
            {
                _sealView.OnSealRevealed(requiredHits);
            }
        }

        public void OnSealHitCounted(EMucheolSealSide side, int hitCount, int requiredHits)
        {
            if (ResolveSealView())
            {
                _sealView.OnSealHitCounted(side, hitCount, requiredHits);
            }
        }

        public void OnSealDirectionCleared(EMucheolSealSide side, int requiredHits)
        {
            if (ResolveSealView())
            {
                _sealView.OnSealDirectionCleared(side, requiredHits);
            }
        }

        public void OnSealBroken(int requiredHits)
        {
            if (ResolveSealView())
            {
                _sealView.OnSealBroken(requiredHits);
            }
        }

        public void OnSealStopped()
        {
            if (ResolveSealView())
            {
                _sealView.OnSealStopped();
            }
        }

        public void OnSealRestored(EMucheolSealSide side, int hitCount, int requiredHits)
        {
            if (ResolveSealView())
            {
                _sealView.OnSealRestored(side, hitCount, requiredHits);
            }
        }

        public void OnChargeGroggyCancelled(int castId)
        {
            if (IsMucheolMotion)
            {
                _mucheolMotion.CancelGroggy(castId);
            }
        }

        // 연결 중일 때만 받는다. 복원으로 배치를 받기 전의 사건은 모르는 구조물이라 표현이 무시한다
        public void OnChargeStructureDestroyed(int castId, int structureId, double serverTime)
        {
            if (_boundState == null)
            {
                return;
            }

            _chargeStructures.ApplyDestroyed(structureId);
        }

        // 구조물 표현이 파괴 사건에서 처음 부술 때만 부른다, 복원은 부르지 않는다
        private void OnChargeStructureDestroyedSound(int structureId, Vector3 center)
        {
            if (_mucheolSound != null)
            {
                _mucheolSound.Play(EMucheolSound.PillarBreak, center);
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
                if (snapshot.StoppedDistance >= 0f)
                {
                    _mucheolMotion.StopDash(start.CastId, snapshot.DashIndex, snapshot.StoppedServerTime);
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
            ShowChargeEnded(in outcome);
        }

        // 끝 시각이 지난 그로기는 모션이 그리지 않는다. 대기 중이면 충돌 시각부터 낮은 자세로 이어 간다
        public void OnChargeGroggyRestored(in BossChargeGroggy groggy)
        {
            if (IsMucheolMotion)
            {
                _mucheolMotion.BeginGroggy(groggy.CastId, groggy.CollisionServerTime, groggy.GroggyEndServerTime);
            }
        }

        public void OnChargeStructuresRestored(in BossChargeStructureSnapshot snapshot)
        {
            if (_boundState == null)
            {
                return;
            }

            _chargeStructures.Restore(in snapshot);
        }
    }
}
