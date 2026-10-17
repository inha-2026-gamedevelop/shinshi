// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    /// <summary> 보스 네트워크 개체의 복제 상태와 공격 사건 전달, 판정은 서버의 BossServer가 한다 </summary>
    public class BossState : NetworkBehaviour, IBossEventListener, IBossMucheolEventListener, IBossHardPhaseEventListener,
        IBossWolbaekGimmickEventListener
    {
        // 서버 스포너(BossNetworkSpawner)가 보스전 시작 틱에 서버 소유로 스폰한다. Command가 없어 클라이언트는 보스 상태를 바꿀 수 없다.
        //
        // 위치는 NetworkTransform 대신 SyncVar 하나로 보낸다. 돌진 중에는 표현이 돌진 사건(시작 위치, 방향, 거리, 속도, 시작 시각)으로
        // 몸체를 그리고 그 밖에는 이 위치를 따른다. 같은 프레임에 두 계층이 몸체를 밀지 않도록 루트 Transform은 상태가 움직이지 않고
        // 몸체를 옮기는 일은 표현 한 곳만 한다. 방향은 플레이어, 동료와 같이 yaw 하나로 보낸다.
        //
        // 공격 사건은 ClientRpc로 보낸다. ClientRpc는 늦게 들어온 관찰자에게 닿지 않으므로 서버는 진행 중인 것만 기록(BossEffectLedger)에
        // 남겨 두고, 새 관찰자에게 보내는 스폰 초기 상태(OnSerialize의 initialState)에 싣는다. 재접속도 새 연결의 스폰이라 같은 경로다.
        // 스폰 메시지가 그 연결의 첫 Rpc보다 먼저 가므로 복원과 이후 사건 사이에 빈틈이나 순서 뒤바뀜이 없다.
        // 클라이언트도 받은 사건으로 같은 기록을 이어 가 수신부가 늦게 연결돼도 지금 상태를 복원으로 넘긴다
        //
        // 무철과 월백 하드 2페이즈 전용 사건은 선택 계약(IBossMucheolEventListener, IBossHardPhaseEventListener)으로 받아 같은 방식으로 보낸다.
        // 기록은 따로 두고(BossPhaseEffectLedger) 스폰 초기 상태에는 기본 기록 뒤에 싣는다. 클라이언트 수신부가 전용 계약을 구현하지 않으면
        // 사건은 기록에만 쌓이고 표현으로 가지 않는다. 사망과 전투 종료에는 중단 사건을 다 받지 못해도 전용 기록을 비운다
        //
        // 월백 체력 기믹 4종과 P07 워프 출발도 선택 계약(IBossWolbaekGimmickEventListener)으로 같은 방식을 따른다. 기록(BossWolbaekGimmickLedger)은
        // 하드 페이즈 기록 뒤에 싣고 일회성 효과(판정, 폭발)는 기록하지 않아 재접속에서 다시 재생되지 않는다.
        // 등장 시작, 처치, 페이즈 세대와 전환 시각은 서버가 정한 시각이라 표현이 추정하지 않도록 SyncVar로 보낸다.
        // 페이즈 세대는 50% 성공 Rpc와 페이즈 SyncVar의 도착 순서와 상관없이 그로기 끝을 정하는 데 쓴다

        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 서버가 아직 정하지 않은 시각, double.IsNaN으로 확인한다 </summary>
        public const double NO_SERVER_TIME = double.NaN;

        // 이 프리팹이 표현하는 보스 정의 ID(예: boss_mucheol), 서버 스포너가 보스 ID로 프리팹을 고르는 데만 쓴다.
        // 비우면 맞는 프리팹이 없는 보스가 쓰는 기본 프리팹이다
        [SerializeField] private string _prefabBossId = string.Empty;

        [SyncVar] private int     _combatId  = CombatIds.NONE;
        [SyncVar] private string  _bossId    = string.Empty;
        [SyncVar] private Vector3 _position;
        [SyncVar] private ushort  _facingYaw;
        [SyncVar] private float   _health;
        [SyncVar] private float   _maxHealth;

        [SyncVar(hook = nameof(OnLifeStateSynced))]
        private EParticipantLifeState _lifeState = EParticipantLifeState.Alive;

        [SyncVar(hook = nameof(OnFlowStateSynced))]
        private EBossFlowState _flowState = EBossFlowState.Inactive;

        [SyncVar(hook = nameof(OnPhaseStateSynced))]
        private EBossPhaseState _phaseState = EBossPhaseState.Active;

        [SyncVar] private double _appearStartServerTime     = NO_SERVER_TIME;
        [SyncVar] private double _defeatServerTime          = NO_SERVER_TIME;
        [SyncVar] private double _phaseTransitionServerTime = NO_SERVER_TIME;

        [SyncVar(hook = nameof(OnPhaseGenerationSynced))]
        private ushort _phaseGeneration;

        // 서버 기록과 클라이언트 기록을 나눠 호스트에서 같은 사건이 두 번 쌓이지 않게 한다
        private readonly BossEffectLedger _serverLedger = new BossEffectLedger();
        private readonly BossEffectLedger _clientLedger = new BossEffectLedger();

        private readonly BossPhaseEffectLedger _serverPhaseLedger = new BossPhaseEffectLedger();
        private readonly BossPhaseEffectLedger _clientPhaseLedger = new BossPhaseEffectLedger();

        private readonly BossWolbaekGimmickLedger _serverWolbaekLedger = new BossWolbaekGimmickLedger();
        private readonly BossWolbaekGimmickLedger _clientWolbaekLedger = new BossWolbaekGimmickLedger();

        private IBossStateListener               _listener;
        private IBossMucheolStateListener        _mucheolListener; // 수신부가 구현했을 때만 있다
        private IBossHardPhaseStateListener      _hardListener;    // 수신부가 구현했을 때만 있다
        private IBossWolbaekGimmickStateListener _wolbaekListener; // 수신부가 구현했을 때만 있다
        private bool                             _clientStarted = false;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 서버 전투 대상 번호, 사건의 BossId와 같다 </summary>
        public int CombatId => _combatId;

        /// <summary> 보스 정의 ID(예: boss_wolbaek), 전투 번호와 다르다 </summary>
        public string BossId => _bossId;

        /// <summary> 서버가 확정한 몸체 위치, 돌진 중에는 복제 주기만큼 늦으므로 표현은 돌진 사건으로 그린다 </summary>
        public Vector3 Position => _position;

        public ushort  FacingYaw => _facingYaw;
        public Vector3 Forward   => FacingRules.DecodeYaw(_facingYaw);

        public float Health     => _health;
        public float MaxHealth  => _maxHealth;
        public float HealthRate => (_maxHealth > 0f) ? Mathf.Clamp01(_health / _maxHealth) : 0f;

        public EParticipantLifeState LifeState  => _lifeState;
        public EBossFlowState        FlowState  => _flowState;
        public EBossPhaseState       PhaseState => _phaseState;

        /// <summary> 클라이언트가 받은 진행 중 공격 기록, 표현 확인과 진단용이다 </summary>
        public BossEffectLedger ClientLedger => _clientLedger;

        /// <summary> 클라이언트가 받은 무철, 월백 하드 2페이즈 전용 공격 기록, 표현 확인과 진단용이다 </summary>
        public BossPhaseEffectLedger ClientPhaseLedger => _clientPhaseLedger;

        /// <summary> 클라이언트가 받은 월백 체력 기믹과 워프 출발 기록, 표현 확인과 진단용이다 </summary>
        public BossWolbaekGimmickLedger ClientWolbaekLedger => _clientWolbaekLedger;

        /// <summary> 이 프리팹이 표현하는 보스 정의 ID, 비어 있으면 기본 프리팹이다 </summary>
        public string PrefabBossId => _prefabBossId;

        /// <summary> 서버가 전투를 연 시각(등장 대기 시작), 아직 없으면 NaN이다 </summary>
        public double AppearStartServerTime => _appearStartServerTime;

        /// <summary> 서버가 보스 사망을 확정한 시각, 살아 있으면 NaN이다 </summary>
        public double DefeatServerTime => _defeatServerTime;

        /// <summary> 페이즈 번호가 바뀔 때마다 1씩 오르는 세대, 전투 시작은 0이다 </summary>
        public ushort PhaseGeneration => _phaseGeneration;

        /// <summary> 마지막 페이즈 전환 시각, 전환이 없었으면 NaN이다 </summary>
        public double PhaseTransitionServerTime => _phaseTransitionServerTime;

        public event Action<EParticipantLifeState> LifeStateChanged;
        public event Action<EBossFlowState>        FlowStateChanged;
        public event Action<EBossPhaseState>       PhaseStateChanged;
        public event Action<ushort>                PhaseGenerationChanged;

        /****************************************
        *            Client Callbacks
        ****************************************/

        public override void OnStartClient()
        {
            _clientStarted = true;
            _clientLedger.Replay(_listener);
            _clientPhaseLedger.Replay(_mucheolListener, _hardListener);
            _clientWolbaekLedger.Replay(_wolbaekListener);
        }

        public override void OnStopClient()
        {
            _clientStarted = false;
            _clientLedger.Clear();
            _clientPhaseLedger.Clear();
            _clientWolbaekLedger.Clear();
        }

        /****************************************
        *                Methods
        ****************************************/

        // 프리팹 값과 상관없이 계약값 주기를 쓴다. Hybrid 기준선은 initialState 참으로 OnSerialize를 불러 복원 기록을
        // 주기마다 다시 실으므로 Reliable로 고정한다
        private void Awake()
        {
            syncInterval = Constants.Network.BOSS_SYNC_INTERVAL_SECONDS;
            syncMethod   = SyncMethod.Reliable;
        }

        // 표현 연결부가 부른다. 이미 스폰된 뒤면 지금 진행 중인 공격을 바로 복원으로 넘긴다. 비우면 연결을 끊는다
        /// <summary> 클라이언트 사건 수신부를 연결한다 </summary>
        public void SetListener(IBossStateListener listener)
        {
            _listener        = listener;
            _mucheolListener = listener as IBossMucheolStateListener;
            _hardListener    = listener as IBossHardPhaseStateListener;
            _wolbaekListener = listener as IBossWolbaekGimmickStateListener;
            if (_clientStarted)
            {
                _clientLedger.Replay(_listener);
                _clientPhaseLedger.Replay(_mucheolListener, _hardListener);
                _clientWolbaekLedger.Replay(_wolbaekListener);
            }
        }

        /****************************************
        *                Server
        ****************************************/

        // SyncVar는 같은 값이면 더럽히지 않으므로 매 틱 불러도 바뀐 값만 전송된다. 스폰 전에 한 번 불러 첫 값을 싣는다
        [Server]
        public void SetCombatSnapshot(int combatId, string bossId, Vector3 position, ushort facingYaw, float health,
            float maxHealth, EParticipantLifeState lifeState, EBossFlowState flowState, EBossPhaseState phaseState)
        {
            _combatId   = combatId;
            _bossId     = bossId;
            _position   = position;
            _facingYaw  = facingYaw;
            _health     = health;
            _maxHealth  = maxHealth;
            _lifeState  = lifeState;
            _flowState  = flowState;
            _phaseState = phaseState;

            // 사망과 전투 종료 틱에 패턴이 중단 사건을 모두 보내지 못해도 늦게 들어온 관찰자가 끝난 공격을 복원하지 않게 한다
            if (!IsFighting(lifeState, flowState))
            {
                _serverPhaseLedger.Clear();
                _serverWolbaekLedger.Clear();
            }
        }

        // 시각을 모르면 NaN을 넘긴다. 같은 값이면 SyncVar가 더럽혀지지 않으므로 매 틱 불러도 된다
        /// <summary> 등장 시작, 처치, 페이즈 세대와 전환 시각을 싣는다 </summary>
        [Server]
        public void SetTimelineSnapshot(double appearStartServerTime, double defeatServerTime, ushort phaseGeneration,
            double phaseTransitionServerTime)
        {
            _appearStartServerTime     = appearStartServerTime;
            _defeatServerTime          = defeatServerTime;
            _phaseGeneration           = phaseGeneration;
            _phaseTransitionServerTime = phaseTransitionServerTime;
        }

        /// <summary> 다음 스폰에 다시 쓸 때 이전 전투의 기록을 지운다 </summary>
        [Server]
        public void ResetServerRecord()
        {
            _serverLedger.Clear();
            _serverPhaseLedger.Clear();
            _serverWolbaekLedger.Clear();
        }

        [Server]
        public void OnTelegraphStarted(in BossTelegraphStart start)
        {
            _serverLedger.OnTelegraphStarted(in start);
            _serverPhaseLedger.OnCastStarted(start.CastId);
            RpcTelegraphStarted(start);
        }

        [Server]
        public void OnAimChanged(int castId, Vector3 direction, float length, double serverTime)
        {
            _serverLedger.OnAimChanged(castId, direction, length, serverTime);
            RpcAimChanged(castId, direction, length, serverTime);
        }

        [Server]
        public void OnDirectionLocked(int castId, Vector3 direction, float length)
        {
            _serverLedger.OnDirectionLocked(castId, direction, length);
            RpcDirectionLocked(castId, direction, length);
        }

        [Server]
        public void OnStruck(int castId, double serverTime)
        {
            _serverLedger.OnStruck(castId, serverTime);
            RpcStruck(castId, serverTime);
        }

        [Server]
        public void OnDashStarted(int castId, float distance, float seconds, float speed, double startServerTime)
        {
            _serverLedger.OnDashStarted(castId, distance, seconds, speed, startServerTime);
            RpcDashStarted(castId, distance, seconds, speed, startServerTime);
        }

        [Server]
        public void OnDashStopped(int castId, Vector3 position, bool stoppedByWall, double stopServerTime)
        {
            _serverLedger.OnDashStopped(castId, position, stoppedByWall, stopServerTime);
            RpcDashStopped(castId, position, stoppedByWall, stopServerTime);
        }

        [Server]
        public void OnFinished(int castId)
        {
            _serverLedger.OnFinished(castId);
            _serverPhaseLedger.OnCastEnded(castId);
            _serverWolbaekLedger.OnCastEnded(castId);
            RpcFinished(castId);
        }

        [Server]
        public void OnCancelled(int castId)
        {
            _serverLedger.OnCancelled(castId);
            _serverPhaseLedger.OnCastEnded(castId);
            _serverWolbaekLedger.OnCastEnded(castId);
            RpcCancelled(castId);
        }

        [Server]
        public void OnProjectileLaunched(in BossProjectileStart start)
        {
            _serverLedger.OnProjectileLaunched(in start);
            RpcProjectileLaunched(start);
        }

        [Server]
        public void OnProjectileEnded(int projectileId, EBossProjectileFate fate, Vector3 position)
        {
            _serverLedger.OnProjectileEnded(projectileId, fate, position);
            RpcProjectileEnded(projectileId, fate, position);
        }

        [Server]
        public void OnAreaActivated(in BossAreaStart start)
        {
            _serverLedger.OnAreaActivated(in start);
            _serverPhaseLedger.OnAreaActivated(start.AreaId);
            RpcAreaActivated(start);
        }

        [Server]
        public void OnAreaTicked(int areaId, int hitIndex, double roundServerTime)
        {
            _serverLedger.OnAreaTicked(areaId, hitIndex, roundServerTime);
            RpcAreaTicked(areaId, hitIndex, roundServerTime);
        }

        [Server]
        public void OnAreaEnded(int areaId, EBossAreaFate fate)
        {
            _serverLedger.OnAreaEnded(areaId, fate);
            RpcAreaEnded(areaId, fate);
        }

        [Server]
        public void OnSweepStarted(in BossSweepStart start)
        {
            _serverLedger.OnSweepStarted(in start);
            RpcSweepStarted(start);
        }

        [Server]
        public void OnSweepZoneScheduled(in BossTelegraphStart zone)
        {
            _serverLedger.OnSweepZoneScheduled(in zone);
            RpcSweepZoneScheduled(zone);
        }

        [Server]
        public void OnSweepZoneBlasted(int zoneId, double blastServerTime)
        {
            _serverLedger.OnSweepZoneBlasted(zoneId, blastServerTime);
            RpcSweepZoneBlasted(zoneId, blastServerTime);
        }

        [Server]
        public void OnSweepCancelled(int castId)
        {
            _serverLedger.OnSweepCancelled(castId);
            RpcSweepCancelled(castId);
        }

        [Server]
        public void OnBeamTelegraphStarted(in BossBeamStart start)
        {
            _serverLedger.OnBeamTelegraphStarted(in start);
            RpcBeamTelegraphStarted(start);
        }

        [Server]
        public void OnBeamCancelled(int castId, int beamId)
        {
            _serverLedger.OnBeamCancelled(castId, beamId);
            RpcBeamCancelled(castId, beamId);
        }

        [Server]
        public void OnBeamActivated(int beamId, double activateServerTime)
        {
            _serverLedger.OnBeamActivated(beamId, activateServerTime);
            RpcBeamActivated(beamId, activateServerTime);
        }

        [Server]
        public void OnBeamTicked(int beamId, int hitIndex, double roundServerTime)
        {
            _serverLedger.OnBeamTicked(beamId, hitIndex, roundServerTime);
            RpcBeamTicked(beamId, hitIndex, roundServerTime);
        }

        [Server]
        public void OnBeamEnded(int beamId, EBossAreaFate fate)
        {
            _serverLedger.OnBeamEnded(beamId, fate);
            RpcBeamEnded(beamId, fate);
        }

        [Server]
        public void OnStarTelegraphStarted(in BossStarStart start)
        {
            _serverLedger.OnStarTelegraphStarted(in start);
            RpcStarTelegraphStarted(start);
        }

        [Server]
        public void OnStarCancelled(int castId, int starId)
        {
            _serverLedger.OnStarCancelled(castId, starId);
            RpcStarCancelled(castId, starId);
        }

        [Server]
        public void OnStarActivated(int starId, double activateServerTime)
        {
            _serverLedger.OnStarActivated(starId, activateServerTime);
            RpcStarActivated(starId, activateServerTime);
        }

        [Server]
        public void OnStarTicked(int starId, int hitIndex, double roundServerTime)
        {
            _serverLedger.OnStarTicked(starId, hitIndex, roundServerTime);
            RpcStarTicked(starId, hitIndex, roundServerTime);
        }

        [Server]
        public void OnStarEnded(int starId, EBossAreaFate fate)
        {
            _serverLedger.OnStarEnded(starId, fate);
            RpcStarEnded(starId, fate);
        }

        [Server]
        public void OnBarrierStarted(in BossBarrierStart start)
        {
            _serverLedger.OnBarrierStarted(in start);
            RpcBarrierStarted(start);
        }

        [Server]
        public void OnBarrierActivated(int barrierId, double activateServerTime)
        {
            _serverLedger.OnBarrierActivated(barrierId, activateServerTime);
            RpcBarrierActivated(barrierId, activateServerTime);
        }

        [Server]
        public void OnBarrierCompleted(int barrierId, double completeServerTime)
        {
            _serverLedger.OnBarrierCompleted(barrierId, completeServerTime);
            RpcBarrierCompleted(barrierId, completeServerTime);
        }

        [Server]
        public void OnBarrierCleared(int barrierId)
        {
            _serverLedger.OnBarrierCleared(barrierId);
            RpcBarrierCleared(barrierId);
        }

        /****************************************
        *         Server (Phase Events)
        ****************************************/

        [Server]
        public void OnStrikeChainStarted(in BossStrikeChainStart start)
        {
            _serverPhaseLedger.OnStrikeChainStarted(in start);
            RpcStrikeChainStarted(start);
        }

        [Server]
        public void OnStrikeChainAimChanged(EBossStrikeChainKind kind, int castId, Vector3 direction, double serverTime)
        {
            _serverPhaseLedger.OnStrikeChainAimChanged(kind, castId, direction, serverTime);
            RpcStrikeChainAimChanged(kind, castId, direction, serverTime);
        }

        [Server]
        public void OnStrikeChainDirectionLocked(EBossStrikeChainKind kind, int castId, Vector3 direction)
        {
            _serverPhaseLedger.OnStrikeChainDirectionLocked(kind, castId, direction);
            RpcStrikeChainDirectionLocked(kind, castId, direction);
        }

        [Server]
        public void OnStrikeChainHit(EBossStrikeChainKind kind, int castId, int stepIndex, double serverTime)
        {
            _serverPhaseLedger.OnStrikeChainHit(kind, castId, stepIndex, serverTime);
            RpcStrikeChainHit(kind, castId, stepIndex, serverTime);
        }

        [Server]
        public void OnStrikeChainCancelled(EBossStrikeChainKind kind, int castId)
        {
            _serverPhaseLedger.OnStrikeChainCancelled(kind, castId);
            RpcStrikeChainCancelled(kind, castId);
        }

        [Server]
        public void OnMultiDashStarted(in BossMultiDashStart start)
        {
            _serverPhaseLedger.OnMultiDashStarted(in start);
            RpcMultiDashStarted(start);
        }

        [Server]
        public void OnMultiDashAimChanged(int castId, int dashIndex, Vector3 direction, float length, double serverTime)
        {
            _serverPhaseLedger.OnMultiDashAimChanged(castId, dashIndex, direction, length, serverTime);
            RpcMultiDashAimChanged(castId, dashIndex, direction, length, serverTime);
        }

        [Server]
        public void OnMultiDashDirectionLocked(int castId, int dashIndex, Vector3 direction, float length)
        {
            _serverPhaseLedger.OnMultiDashDirectionLocked(castId, dashIndex, direction, length);
            RpcMultiDashDirectionLocked(castId, dashIndex, direction, length);
        }

        [Server]
        public void OnMultiDashReaimStarted(in BossMultiDashReaim reaim)
        {
            _serverPhaseLedger.OnMultiDashReaimStarted(in reaim);
            RpcMultiDashReaimStarted(reaim);
        }

        [Server]
        public void OnMultiDashDashStarted(int castId, int dashIndex, float distance, float seconds, float speed,
            double startServerTime)
        {
            _serverPhaseLedger.OnMultiDashDashStarted(castId, dashIndex, distance, seconds, speed, startServerTime);
            RpcMultiDashDashStarted(castId, dashIndex, distance, seconds, speed, startServerTime);
        }

        [Server]
        public void OnMultiDashDashStopped(int castId, int dashIndex, Vector3 position, bool stoppedByWall,
            double stopServerTime)
        {
            _serverPhaseLedger.OnMultiDashDashStopped(castId, dashIndex, position, stoppedByWall, stopServerTime);
            RpcMultiDashDashStopped(castId, dashIndex, position, stoppedByWall, stopServerTime);
        }

        [Server]
        public void OnMultiDashTruncated(int castId, int plannedDashCount, int completedDashCount)
        {
            _serverPhaseLedger.OnMultiDashTruncated(castId, plannedDashCount, completedDashCount);
            RpcMultiDashTruncated(castId, plannedDashCount, completedDashCount);
        }

        [Server]
        public void OnMultiDashCancelled(int castId)
        {
            _serverPhaseLedger.OnMultiDashCancelled(castId);
            RpcMultiDashCancelled(castId);
        }

        [Server]
        public void OnBattlefieldStarted(in BossBattlefieldStart start)
        {
            _serverPhaseLedger.OnBattlefieldStarted(in start);
            RpcBattlefieldStarted(start);
        }

        [Server]
        public void OnBattlefieldHit(int castId, double serverTime)
        {
            _serverPhaseLedger.OnBattlefieldHit(castId, serverTime);
            RpcBattlefieldHit(castId, serverTime);
        }

        [Server]
        public void OnBattlefieldCancelled(int castId)
        {
            _serverPhaseLedger.OnBattlefieldCancelled(castId);
            RpcBattlefieldCancelled(castId);
        }

        [Server]
        public void OnChargeEnded(in BossChargeOutcome outcome)
        {
            _serverPhaseLedger.OnChargeEnded(in outcome);
            RpcChargeEnded(outcome);
        }

        [Server]
        public void OnChargeGroggyPending(in BossChargeGroggy groggy)
        {
            _serverPhaseLedger.OnChargeGroggyPending(in groggy);
            RpcChargeGroggyPending(groggy);
        }

        [Server]
        public void OnChargeGroggyStarted(in BossChargeGroggy groggy)
        {
            _serverPhaseLedger.OnChargeGroggyStarted(in groggy);
            RpcChargeGroggyStarted(groggy);
        }

        [Server]
        public void OnChargeGroggyCancelled(int castId)
        {
            _serverPhaseLedger.OnChargeGroggyCancelled(castId);
            RpcChargeGroggyCancelled(castId);
        }

        [Server]
        public void OnStarStageStarted(in BossStarStageStart start)
        {
            _serverPhaseLedger.OnStarStageStarted(in start);
            RpcStarStageStarted(start);
        }

        [Server]
        public void OnStarStageStruck(int castId, int stageIndex, float rotationDegrees, double scheduledServerTime)
        {
            _serverPhaseLedger.OnStarStageStruck(castId, stageIndex, rotationDegrees, scheduledServerTime);
            RpcStarStageStruck(castId, stageIndex, rotationDegrees, scheduledServerTime);
        }

        [Server]
        public void OnStarStageCancelled(int castId)
        {
            _serverPhaseLedger.OnStarStageCancelled(castId);
            RpcStarStageCancelled(castId);
        }

        [Server]
        public void OnGridBeamStarted(in BossGridBeamStart start)
        {
            _serverPhaseLedger.OnGridBeamStarted(in start);
            RpcGridBeamStarted(start);
        }

        [Server]
        public void OnGridBeamLineTelegraphed(int castId, int lineId, bool isHorizontal, int lineIndex,
            double telegraphServerTime, double strikeServerTime)
        {
            _serverPhaseLedger.OnGridBeamLineTelegraphed(castId, lineId, isHorizontal, lineIndex,
                telegraphServerTime, strikeServerTime);
            RpcGridBeamLineTelegraphed(castId, lineId, isHorizontal, lineIndex, telegraphServerTime, strikeServerTime);
        }

        [Server]
        public void OnGridBeamLineStruck(int castId, int lineId, bool isHorizontal, int lineIndex,
            double scheduledServerTime)
        {
            _serverPhaseLedger.OnGridBeamLineStruck(castId, lineId, isHorizontal, lineIndex, scheduledServerTime);
            RpcGridBeamLineStruck(castId, lineId, isHorizontal, lineIndex, scheduledServerTime);
        }

        [Server]
        public void OnGridBeamCancelled(int castId)
        {
            _serverPhaseLedger.OnGridBeamCancelled(castId);
            RpcGridBeamCancelled(castId);
        }

        [Server]
        public void OnChainFanStarted(in BossChainFanStart start)
        {
            _serverPhaseLedger.OnChainFanStarted(in start);
            RpcChainFanStarted(start);
        }

        [Server]
        public void OnChainFanBlasted(int castId, int zoneIndex, double scheduledServerTime)
        {
            _serverPhaseLedger.OnChainFanBlasted(castId, zoneIndex, scheduledServerTime);
            RpcChainFanBlasted(castId, zoneIndex, scheduledServerTime);
        }

        [Server]
        public void OnChainFanDangerEnded(int castId, double scheduledServerTime)
        {
            _serverPhaseLedger.OnChainFanDangerEnded(castId, scheduledServerTime);
            RpcChainFanDangerEnded(castId, scheduledServerTime);
        }

        [Server]
        public void OnChainFanCancelled(int castId)
        {
            _serverPhaseLedger.OnChainFanCancelled(castId);
            RpcChainFanCancelled(castId);
        }

        [Server]
        public void OnTrackingAreaStarted(in BossTrackingAreaStart start)
        {
            _serverPhaseLedger.OnTrackingAreaStarted(in start);
            RpcTrackingAreaStarted(start);
        }

        [Server]
        public void OnTrackingAreaMoved(int areaId, Vector3 center, double serverTime)
        {
            _serverPhaseLedger.OnTrackingAreaMoved(areaId, center, serverTime);
            RpcTrackingAreaMoved(areaId, center, serverTime);
        }

        [Server]
        public void OnTrackingAreaFixed(int areaId, Vector3 center, double fixedServerTime)
        {
            _serverPhaseLedger.OnTrackingAreaFixed(areaId, center, fixedServerTime);
            RpcTrackingAreaFixed(areaId, center, fixedServerTime);
        }

        [Server]
        public void OnTrackingAreaCancelled(int castId, int areaId)
        {
            _serverPhaseLedger.OnTrackingAreaCancelled(castId, areaId);
            RpcTrackingAreaCancelled(castId, areaId);
        }

        [Server]
        public void OnBarrageStarted(in BossBarrageStart start)
        {
            _serverPhaseLedger.OnBarrageStarted(in start);
            RpcBarrageStarted(start);
        }

        [Server]
        public void OnBarrageShotTelegraphed(in BossBarrageShot shot)
        {
            _serverPhaseLedger.OnBarrageShotTelegraphed(in shot);
            RpcBarrageShotTelegraphed(shot);
        }

        [Server]
        public void OnBarrageShotFired(int castId, int shotIndex, int projectileId, bool isLaunched,
            double scheduledServerTime)
        {
            _serverPhaseLedger.OnBarrageShotFired(castId, shotIndex, projectileId, isLaunched, scheduledServerTime);
            RpcBarrageShotFired(castId, shotIndex, projectileId, isLaunched, scheduledServerTime);
        }

        [Server]
        public void OnBarrageCancelled(int castId)
        {
            _serverPhaseLedger.OnBarrageCancelled(castId);
            RpcBarrageCancelled(castId);
        }

        [Server]
        public void OnCircleBurstStarted(in BossCircleBurstStart start)
        {
            _serverPhaseLedger.OnCircleBurstStarted(in start);
            RpcCircleBurstStarted(start);
        }

        [Server]
        public void OnCircleBurstStruck(int castId, double scheduledServerTime)
        {
            _serverPhaseLedger.OnCircleBurstStruck(castId, scheduledServerTime);
            RpcCircleBurstStruck(castId, scheduledServerTime);
        }

        [Server]
        public void OnCircleBurstCancelled(int castId)
        {
            _serverPhaseLedger.OnCircleBurstCancelled(castId);
            RpcCircleBurstCancelled(castId);
        }

        /****************************************
        *     Server (Wolbaek Gimmick Events)
        ****************************************/

        // 목록이 상한을 넘는 값은 Rpc 쓰기에서 예외가 나 서버 틱을 멈추므로 보내기 전에 버리고 남긴다

        [Server]
        public void OnTalismanStarted(in WolbaekTalismanStart start)
        {
            if (!BossWolbaekNetSerializer.CanWrite(in start))
            {
                LogWolbaekDrop("부적진 시작", start.CastInstanceId);
                return;
            }

            _serverWolbaekLedger.OnTalismanStarted(in start);
            RpcTalismanStarted(start);
        }

        [Server]
        public void OnTalismanHoldStarted(int castId, double serverTime)
        {
            _serverWolbaekLedger.OnTalismanHoldStarted(castId, serverTime);
            RpcTalismanHoldStarted(castId, serverTime);
        }

        [Server]
        public void OnTalismanHoldReset(int castId, double serverTime, EWolbaekHoldReset reason, int combatId)
        {
            _serverWolbaekLedger.OnTalismanHoldReset(castId, serverTime, reason, combatId);
            RpcTalismanHoldReset(castId, serverTime, reason, combatId);
        }

        [Server]
        public void OnTalismanAttackTelegraphed(int castId, in WolbaekTalismanAttack attack)
        {
            _serverWolbaekLedger.OnTalismanAttackTelegraphed(castId, in attack);
            RpcTalismanAttackTelegraphed(castId, attack);
        }

        [Server]
        public void OnTalismanAttackStruck(int castId, int attackId, double strikeServerTime)
        {
            _serverWolbaekLedger.OnTalismanAttackStruck(castId, attackId, strikeServerTime);
            RpcTalismanAttackStruck(castId, attackId, strikeServerTime);
        }

        [Server]
        public void OnTalismanAttackCancelled(int castId, int attackId)
        {
            _serverWolbaekLedger.OnTalismanAttackCancelled(castId, attackId);
            RpcTalismanAttackCancelled(castId, attackId);
        }

        [Server]
        public void OnTalismanSucceeded(int castId, double serverTime, double counterEndServerTime)
        {
            _serverWolbaekLedger.OnTalismanSucceeded(castId, serverTime, counterEndServerTime);
            RpcTalismanSucceeded(castId, serverTime, counterEndServerTime);
        }

        [Server]
        public void OnTalismanCounterEnded(int castId)
        {
            _serverWolbaekLedger.OnTalismanCounterEnded(castId);
            RpcTalismanCounterEnded(castId);
        }

        [Server]
        public void OnTalismanCleared(int castId)
        {
            _serverWolbaekLedger.OnTalismanCleared(castId);
            RpcTalismanCleared(castId);
        }

        [Server]
        public void OnWardStarted(in WolbaekWardStart start)
        {
            if (!BossWolbaekNetSerializer.CanWrite(in start))
            {
                LogWolbaekDrop("색 결계 시작", start.CastInstanceId);
                return;
            }

            _serverWolbaekLedger.OnWardStarted(in start);
            RpcWardStarted(start);
        }

        [Server]
        public void OnGatherHoldStarted(int castId, double serverTime)
        {
            _serverWolbaekLedger.OnGatherHoldStarted(castId, serverTime);
            RpcGatherHoldStarted(castId, serverTime);
        }

        [Server]
        public void OnGatherHoldReset(int castId)
        {
            _serverWolbaekLedger.OnGatherHoldReset(castId);
            RpcGatherHoldReset(castId);
        }

        [Server]
        public void OnGatherCompleted(int castId)
        {
            _serverWolbaekLedger.OnGatherCompleted(castId);
            RpcGatherCompleted(castId);
        }

        [Server]
        public void OnStageStarted(int castId, in WolbaekWardStage stage)
        {
            _serverWolbaekLedger.OnStageStarted(castId, in stage);
            RpcWardStageStarted(castId, stage);
        }

        [Server]
        public void OnStrikeTelegraphed(int castId, in WolbaekWardStrike strike)
        {
            _serverWolbaekLedger.OnStrikeTelegraphed(castId, in strike);
            RpcWardStrikeTelegraphed(castId, strike);
        }

        [Server]
        public void OnStrikeResolved(int castId, int stageIndex, int strikeIndex, double strikeServerTime)
        {
            _serverWolbaekLedger.OnStrikeResolved(castId, stageIndex, strikeIndex, strikeServerTime);
            RpcWardStrikeResolved(castId, stageIndex, strikeIndex, strikeServerTime);
        }

        [Server]
        public void OnLinkDamaged(int castId, double serverTime)
        {
            RpcWardLinkDamaged(castId, serverTime);
        }

        [Server]
        public void OnGraceStarted(int castId, int combatId, double graceEndServerTime)
        {
            _serverWolbaekLedger.OnGraceStarted(castId, combatId, graceEndServerTime);
            RpcWardGraceStarted(castId, combatId, graceEndServerTime);
        }

        [Server]
        public void OnGraceEnded(int castId, int combatId)
        {
            _serverWolbaekLedger.OnGraceEnded(castId, combatId);
            RpcWardGraceEnded(castId, combatId);
        }

        [Server]
        public void OnReviveWindowOpened(int castId)
        {
            RpcWardReviveWindowOpened(castId);
        }

        [Server]
        public void OnWardSucceeded(int castId, ushort phaseGenerationAtSuccess)
        {
            _serverWolbaekLedger.OnWardSucceeded(castId, phaseGenerationAtSuccess);
            RpcWardSucceeded(castId, phaseGenerationAtSuccess);
        }

        [Server]
        public void OnWardCleared(int castId)
        {
            _serverWolbaekLedger.OnWardCleared(castId);
            RpcWardCleared(castId);
        }

        [Server]
        public void OnSealStarted(in WolbaekSealStart start)
        {
            _serverWolbaekLedger.OnSealStarted(in start);
            RpcSealStarted(start);
        }

        [Server]
        public void OnSealTurnStarted(int castId, in WolbaekSealTurn turn)
        {
            if (!BossWolbaekNetSerializer.CanWrite(in turn))
            {
                LogWolbaekDrop("봉인 차례", castId);
                return;
            }

            _serverWolbaekLedger.OnSealTurnStarted(castId, in turn);
            RpcSealTurnStarted(castId, turn);
        }

        [Server]
        public void OnSealTrackingStarted(int castId, int turnIndex, double serverTime)
        {
            _serverWolbaekLedger.OnSealTrackingStarted(castId, turnIndex, serverTime);
            RpcSealTrackingStarted(castId, turnIndex, serverTime);
        }

        [Server]
        public void OnSealFoxfireDetached(int castId, int turnIndex, int foxfireIndex, double serverTime, Vector3 position)
        {
            _serverWolbaekLedger.OnSealFoxfireDetached(castId, turnIndex, foxfireIndex, serverTime, position);
            RpcSealFoxfireDetached(castId, turnIndex, foxfireIndex, serverTime, position);
        }

        [Server]
        public void OnSealSweepTelegraphed(int castId, in WolbaekSealSweep sweep)
        {
            _serverWolbaekLedger.OnSealSweepTelegraphed(castId, in sweep);
            RpcSealSweepTelegraphed(castId, sweep);
        }

        [Server]
        public void OnSealSweepResolved(int castId, int turnIndex, int sweepIndex, double serverTime)
        {
            _serverWolbaekLedger.OnSealSweepResolved(castId, turnIndex, sweepIndex, serverTime);
            RpcSealSweepResolved(castId, turnIndex, sweepIndex, serverTime);
        }

        [Server]
        public void OnSealFoxfiresLocked(int castId, int turnIndex, double serverTime, Vector3[] centers)
        {
            if (!BossWolbaekNetSerializer.IsWithin(centers, Constants.Network.BOSS_WOLBAEK_MAX_FOXFIRES))
            {
                LogWolbaekDrop("봉인 여우불 고정", castId);
                return;
            }

            _serverWolbaekLedger.OnSealFoxfiresLocked(castId, turnIndex, serverTime, centers);
            RpcSealFoxfiresLocked(castId, turnIndex, serverTime, centers);
        }

        [Server]
        public void OnSealTurnResolved(int castId, int turnIndex, double serverTime, int successCount, byte destroyedCoreMask)
        {
            _serverWolbaekLedger.OnSealTurnResolved(castId, turnIndex, serverTime, successCount, destroyedCoreMask);
            RpcSealTurnResolved(castId, turnIndex, serverTime, successCount, destroyedCoreMask);
        }

        [Server]
        public void OnSealSucceeded(int castId, double serverTime, double counterEndServerTime)
        {
            _serverWolbaekLedger.OnSealSucceeded(castId, serverTime, counterEndServerTime);
            RpcSealSucceeded(castId, serverTime, counterEndServerTime);
        }

        [Server]
        public void OnSealCounterEnded(int castId)
        {
            _serverWolbaekLedger.OnSealCounterEnded(castId);
            RpcSealCounterEnded(castId);
        }

        [Server]
        public void OnSealCleared(int castId)
        {
            _serverWolbaekLedger.OnSealCleared(castId);
            RpcSealCleared(castId);
        }

        [Server]
        public void OnFinalStarted(in WolbaekFinalStart start)
        {
            _serverWolbaekLedger.OnFinalStarted(in start);
            RpcFinalStarted(start);
        }

        [Server]
        public void OnFinalOuterFloorRemoved(int castId, double serverTime)
        {
            _serverWolbaekLedger.OnFinalOuterFloorRemoved(castId, serverTime);
            RpcFinalOuterFloorRemoved(castId, serverTime);
        }

        [Server]
        public void OnFinalYellowsSpawned(int castId, WolbaekFinalYellow[] yellows)
        {
            if (!BossWolbaekNetSerializer.CanWrite(yellows))
            {
                LogWolbaekDrop("노란 여우불 생성", castId);
                return;
            }

            _serverWolbaekLedger.OnFinalYellowsSpawned(castId, yellows);
            RpcFinalYellowsSpawned(castId, yellows);
        }

        [Server]
        public void OnFinalYellowDestroyed(int castId, int yellowIndex, double serverTime)
        {
            _serverWolbaekLedger.OnFinalYellowDestroyed(castId, yellowIndex, serverTime);
            RpcFinalYellowDestroyed(castId, yellowIndex, serverTime);
        }

        [Server]
        public void OnFinalBundleStarted(int castId, in WolbaekFinalBundle bundle)
        {
            if (!BossWolbaekNetSerializer.CanWrite(in bundle))
            {
                LogWolbaekDrop("빨간 여우불 묶음", castId);
                return;
            }

            _serverWolbaekLedger.OnFinalBundleStarted(castId, in bundle);
            RpcFinalBundleStarted(castId, bundle);
        }

        [Server]
        public void OnFinalRedDetached(int castId, int bundleIndex, int redIndex, double serverTime, Vector3 position)
        {
            _serverWolbaekLedger.OnFinalRedDetached(castId, bundleIndex, redIndex, serverTime, position);
            RpcFinalRedDetached(castId, bundleIndex, redIndex, serverTime, position);
        }

        [Server]
        public void OnFinalRedsLocked(int castId, int bundleIndex, double serverTime, Vector3[] centers)
        {
            if (!BossWolbaekNetSerializer.IsWithin(centers, Constants.Network.BOSS_WOLBAEK_MAX_REDS))
            {
                LogWolbaekDrop("빨간 여우불 고정", castId);
                return;
            }

            _serverWolbaekLedger.OnFinalRedsLocked(castId, bundleIndex, serverTime, centers);
            RpcFinalRedsLocked(castId, bundleIndex, serverTime, centers);
        }

        [Server]
        public void OnFinalBundleResolved(int castId, int bundleIndex, double serverTime)
        {
            _serverWolbaekLedger.OnFinalBundleResolved(castId, bundleIndex, serverTime);
            RpcFinalBundleResolved(castId, bundleIndex, serverTime);
        }

        [Server]
        public void OnFinalSucceeded(int castId, double serverTime)
        {
            _serverWolbaekLedger.OnFinalSucceeded(castId, serverTime);
            RpcFinalSucceeded(castId, serverTime);
        }

        [Server]
        public void OnFinalCleared(int castId)
        {
            _serverWolbaekLedger.OnFinalCleared(castId);
            RpcFinalCleared(castId);
        }

        [Server]
        public void OnWarpDeparted(int castId, Vector3 departure, double serverTime)
        {
            _serverWolbaekLedger.OnWarpDeparted(castId, departure, serverTime);
            RpcWarpDeparted(castId, departure, serverTime);
        }

        private static void LogWolbaekDrop(string name, int castId)
        {
            Debug.LogError($"[Boss] 월백 기믹 {name} 목록이 상한을 넘어 보내지 않는다, castId={castId}");
        }

        /****************************************
        *               ClientRpc
        ****************************************/

        [ClientRpc]
        private void RpcTelegraphStarted(BossTelegraphStart start)
        {
            _clientLedger.OnTelegraphStarted(in start);
            _clientPhaseLedger.OnCastStarted(start.CastId);
            _listener?.OnTelegraphStarted(in start);
        }

        [ClientRpc]
        private void RpcAimChanged(int castId, Vector3 direction, float length, double serverTime)
        {
            _clientLedger.OnAimChanged(castId, direction, length, serverTime);
            _listener?.OnAimChanged(castId, direction, length, serverTime);
        }

        [ClientRpc]
        private void RpcDirectionLocked(int castId, Vector3 direction, float length)
        {
            _clientLedger.OnDirectionLocked(castId, direction, length);
            _listener?.OnDirectionLocked(castId, direction, length);
        }

        [ClientRpc]
        private void RpcStruck(int castId, double serverTime)
        {
            _clientLedger.OnStruck(castId, serverTime);
            _listener?.OnStruck(castId, serverTime);
        }

        [ClientRpc]
        private void RpcDashStarted(int castId, float distance, float seconds, float speed, double startServerTime)
        {
            _clientLedger.OnDashStarted(castId, distance, seconds, speed, startServerTime);
            _listener?.OnDashStarted(castId, distance, seconds, speed, startServerTime);
        }

        [ClientRpc]
        private void RpcDashStopped(int castId, Vector3 position, bool stoppedByWall, double stopServerTime)
        {
            _clientLedger.OnDashStopped(castId, position, stoppedByWall, stopServerTime);
            _listener?.OnDashStopped(castId, position, stoppedByWall, stopServerTime);
        }

        [ClientRpc]
        private void RpcFinished(int castId)
        {
            _clientLedger.OnFinished(castId);
            _clientPhaseLedger.OnCastEnded(castId);
            _clientWolbaekLedger.OnCastEnded(castId);
            _listener?.OnFinished(castId);
        }

        [ClientRpc]
        private void RpcCancelled(int castId)
        {
            _clientLedger.OnCancelled(castId);
            _clientPhaseLedger.OnCastEnded(castId);
            _clientWolbaekLedger.OnCastEnded(castId);
            _listener?.OnCancelled(castId);
        }

        [ClientRpc]
        private void RpcProjectileLaunched(BossProjectileStart start)
        {
            _clientLedger.OnProjectileLaunched(in start);
            _listener?.OnProjectileLaunched(in start);
        }

        [ClientRpc]
        private void RpcProjectileEnded(int projectileId, EBossProjectileFate fate, Vector3 position)
        {
            _clientLedger.OnProjectileEnded(projectileId, fate, position);
            _listener?.OnProjectileEnded(projectileId, fate, position);
        }

        [ClientRpc]
        private void RpcAreaActivated(BossAreaStart start)
        {
            _clientLedger.OnAreaActivated(in start);
            _clientPhaseLedger.OnAreaActivated(start.AreaId);
            _listener?.OnAreaActivated(in start);
        }

        [ClientRpc]
        private void RpcAreaTicked(int areaId, int hitIndex, double roundServerTime)
        {
            _clientLedger.OnAreaTicked(areaId, hitIndex, roundServerTime);
            _listener?.OnAreaTicked(areaId, hitIndex, roundServerTime);
        }

        [ClientRpc]
        private void RpcAreaEnded(int areaId, EBossAreaFate fate)
        {
            _clientLedger.OnAreaEnded(areaId, fate);
            _listener?.OnAreaEnded(areaId, fate);
        }

        [ClientRpc]
        private void RpcSweepStarted(BossSweepStart start)
        {
            _clientLedger.OnSweepStarted(in start);
            _listener?.OnSweepStarted(in start);
        }

        [ClientRpc]
        private void RpcSweepZoneScheduled(BossTelegraphStart zone)
        {
            _clientLedger.OnSweepZoneScheduled(in zone);
            _listener?.OnSweepZoneScheduled(in zone);
        }

        [ClientRpc]
        private void RpcSweepZoneBlasted(int zoneId, double blastServerTime)
        {
            _clientLedger.OnSweepZoneBlasted(zoneId, blastServerTime);
            _listener?.OnSweepZoneBlasted(zoneId, blastServerTime);
        }

        [ClientRpc]
        private void RpcSweepCancelled(int castId)
        {
            _clientLedger.OnSweepCancelled(castId);
            _listener?.OnSweepCancelled(castId);
        }

        [ClientRpc]
        private void RpcBeamTelegraphStarted(BossBeamStart start)
        {
            _clientLedger.OnBeamTelegraphStarted(in start);
            _listener?.OnBeamTelegraphStarted(in start);
        }

        [ClientRpc]
        private void RpcBeamCancelled(int castId, int beamId)
        {
            _clientLedger.OnBeamCancelled(castId, beamId);
            _listener?.OnBeamCancelled(castId, beamId);
        }

        [ClientRpc]
        private void RpcBeamActivated(int beamId, double activateServerTime)
        {
            _clientLedger.OnBeamActivated(beamId, activateServerTime);
            _listener?.OnBeamActivated(beamId, activateServerTime);
        }

        [ClientRpc]
        private void RpcBeamTicked(int beamId, int hitIndex, double roundServerTime)
        {
            _clientLedger.OnBeamTicked(beamId, hitIndex, roundServerTime);
            _listener?.OnBeamTicked(beamId, hitIndex, roundServerTime);
        }

        [ClientRpc]
        private void RpcBeamEnded(int beamId, EBossAreaFate fate)
        {
            _clientLedger.OnBeamEnded(beamId, fate);
            _listener?.OnBeamEnded(beamId, fate);
        }

        [ClientRpc]
        private void RpcStarTelegraphStarted(BossStarStart start)
        {
            _clientLedger.OnStarTelegraphStarted(in start);
            _listener?.OnStarTelegraphStarted(in start);
        }

        [ClientRpc]
        private void RpcStarCancelled(int castId, int starId)
        {
            _clientLedger.OnStarCancelled(castId, starId);
            _listener?.OnStarCancelled(castId, starId);
        }

        [ClientRpc]
        private void RpcStarActivated(int starId, double activateServerTime)
        {
            _clientLedger.OnStarActivated(starId, activateServerTime);
            _listener?.OnStarActivated(starId, activateServerTime);
        }

        [ClientRpc]
        private void RpcStarTicked(int starId, int hitIndex, double roundServerTime)
        {
            _clientLedger.OnStarTicked(starId, hitIndex, roundServerTime);
            _listener?.OnStarTicked(starId, hitIndex, roundServerTime);
        }

        [ClientRpc]
        private void RpcStarEnded(int starId, EBossAreaFate fate)
        {
            _clientLedger.OnStarEnded(starId, fate);
            _listener?.OnStarEnded(starId, fate);
        }

        [ClientRpc]
        private void RpcBarrierStarted(BossBarrierStart start)
        {
            _clientLedger.OnBarrierStarted(in start);
            _listener?.OnBarrierStarted(in start);
        }

        [ClientRpc]
        private void RpcBarrierActivated(int barrierId, double activateServerTime)
        {
            _clientLedger.OnBarrierActivated(barrierId, activateServerTime);
            _listener?.OnBarrierActivated(barrierId, activateServerTime);
        }

        [ClientRpc]
        private void RpcBarrierCompleted(int barrierId, double completeServerTime)
        {
            _clientLedger.OnBarrierCompleted(barrierId, completeServerTime);
            _listener?.OnBarrierCompleted(barrierId, completeServerTime);
        }

        [ClientRpc]
        private void RpcBarrierCleared(int barrierId)
        {
            _clientLedger.OnBarrierCleared(barrierId);
            _listener?.OnBarrierCleared(barrierId);
        }

        /****************************************
        *        ClientRpc (Phase Events)
        ****************************************/

        [ClientRpc]
        private void RpcStrikeChainStarted(BossStrikeChainStart start)
        {
            _clientPhaseLedger.OnStrikeChainStarted(in start);
            _mucheolListener?.OnStrikeChainStarted(in start);
        }

        [ClientRpc]
        private void RpcStrikeChainAimChanged(EBossStrikeChainKind kind, int castId, Vector3 direction, double serverTime)
        {
            _clientPhaseLedger.OnStrikeChainAimChanged(kind, castId, direction, serverTime);
            _mucheolListener?.OnStrikeChainAimChanged(kind, castId, direction, serverTime);
        }

        [ClientRpc]
        private void RpcStrikeChainDirectionLocked(EBossStrikeChainKind kind, int castId, Vector3 direction)
        {
            _clientPhaseLedger.OnStrikeChainDirectionLocked(kind, castId, direction);
            _mucheolListener?.OnStrikeChainDirectionLocked(kind, castId, direction);
        }

        [ClientRpc]
        private void RpcStrikeChainHit(EBossStrikeChainKind kind, int castId, int stepIndex, double serverTime)
        {
            _clientPhaseLedger.OnStrikeChainHit(kind, castId, stepIndex, serverTime);
            _mucheolListener?.OnStrikeChainHit(kind, castId, stepIndex, serverTime);
        }

        [ClientRpc]
        private void RpcStrikeChainCancelled(EBossStrikeChainKind kind, int castId)
        {
            _clientPhaseLedger.OnStrikeChainCancelled(kind, castId);
            _mucheolListener?.OnStrikeChainCancelled(kind, castId);
        }

        [ClientRpc]
        private void RpcMultiDashStarted(BossMultiDashStart start)
        {
            _clientPhaseLedger.OnMultiDashStarted(in start);
            _mucheolListener?.OnMultiDashStarted(in start);
        }

        [ClientRpc]
        private void RpcMultiDashAimChanged(int castId, int dashIndex, Vector3 direction, float length, double serverTime)
        {
            _clientPhaseLedger.OnMultiDashAimChanged(castId, dashIndex, direction, length, serverTime);
            _mucheolListener?.OnMultiDashAimChanged(castId, dashIndex, direction, length, serverTime);
        }

        [ClientRpc]
        private void RpcMultiDashDirectionLocked(int castId, int dashIndex, Vector3 direction, float length)
        {
            _clientPhaseLedger.OnMultiDashDirectionLocked(castId, dashIndex, direction, length);
            _mucheolListener?.OnMultiDashDirectionLocked(castId, dashIndex, direction, length);
        }

        [ClientRpc]
        private void RpcMultiDashReaimStarted(BossMultiDashReaim reaim)
        {
            _clientPhaseLedger.OnMultiDashReaimStarted(in reaim);
            _mucheolListener?.OnMultiDashReaimStarted(in reaim);
        }

        [ClientRpc]
        private void RpcMultiDashDashStarted(int castId, int dashIndex, float distance, float seconds, float speed,
            double startServerTime)
        {
            _clientPhaseLedger.OnMultiDashDashStarted(castId, dashIndex, distance, seconds, speed, startServerTime);
            _mucheolListener?.OnMultiDashDashStarted(castId, dashIndex, distance, seconds, speed, startServerTime);
        }

        [ClientRpc]
        private void RpcMultiDashDashStopped(int castId, int dashIndex, Vector3 position, bool stoppedByWall,
            double stopServerTime)
        {
            _clientPhaseLedger.OnMultiDashDashStopped(castId, dashIndex, position, stoppedByWall, stopServerTime);
            _mucheolListener?.OnMultiDashDashStopped(castId, dashIndex, position, stoppedByWall, stopServerTime);
        }

        [ClientRpc]
        private void RpcMultiDashTruncated(int castId, int plannedDashCount, int completedDashCount)
        {
            _clientPhaseLedger.OnMultiDashTruncated(castId, plannedDashCount, completedDashCount);
            _mucheolListener?.OnMultiDashTruncated(castId, plannedDashCount, completedDashCount);
        }

        [ClientRpc]
        private void RpcMultiDashCancelled(int castId)
        {
            _clientPhaseLedger.OnMultiDashCancelled(castId);
            _mucheolListener?.OnMultiDashCancelled(castId);
        }

        [ClientRpc]
        private void RpcBattlefieldStarted(BossBattlefieldStart start)
        {
            _clientPhaseLedger.OnBattlefieldStarted(in start);
            _mucheolListener?.OnBattlefieldStarted(in start);
        }

        [ClientRpc]
        private void RpcBattlefieldHit(int castId, double serverTime)
        {
            _clientPhaseLedger.OnBattlefieldHit(castId, serverTime);
            _mucheolListener?.OnBattlefieldHit(castId, serverTime);
        }

        [ClientRpc]
        private void RpcBattlefieldCancelled(int castId)
        {
            _clientPhaseLedger.OnBattlefieldCancelled(castId);
            _mucheolListener?.OnBattlefieldCancelled(castId);
        }

        [ClientRpc]
        private void RpcChargeEnded(BossChargeOutcome outcome)
        {
            _clientPhaseLedger.OnChargeEnded(in outcome);
            _mucheolListener?.OnChargeEnded(in outcome);
        }

        [ClientRpc]
        private void RpcChargeGroggyPending(BossChargeGroggy groggy)
        {
            _clientPhaseLedger.OnChargeGroggyPending(in groggy);
            _mucheolListener?.OnChargeGroggyPending(in groggy);
        }

        [ClientRpc]
        private void RpcChargeGroggyStarted(BossChargeGroggy groggy)
        {
            _clientPhaseLedger.OnChargeGroggyStarted(in groggy);
            _mucheolListener?.OnChargeGroggyStarted(in groggy);
        }

        [ClientRpc]
        private void RpcChargeGroggyCancelled(int castId)
        {
            _clientPhaseLedger.OnChargeGroggyCancelled(castId);
            _mucheolListener?.OnChargeGroggyCancelled(castId);
        }

        [ClientRpc]
        private void RpcStarStageStarted(BossStarStageStart start)
        {
            _clientPhaseLedger.OnStarStageStarted(in start);
            _hardListener?.OnStarStageStarted(in start);
        }

        [ClientRpc]
        private void RpcStarStageStruck(int castId, int stageIndex, float rotationDegrees, double scheduledServerTime)
        {
            _clientPhaseLedger.OnStarStageStruck(castId, stageIndex, rotationDegrees, scheduledServerTime);
            _hardListener?.OnStarStageStruck(castId, stageIndex, rotationDegrees, scheduledServerTime);
        }

        [ClientRpc]
        private void RpcStarStageCancelled(int castId)
        {
            _clientPhaseLedger.OnStarStageCancelled(castId);
            _hardListener?.OnStarStageCancelled(castId);
        }

        [ClientRpc]
        private void RpcGridBeamStarted(BossGridBeamStart start)
        {
            _clientPhaseLedger.OnGridBeamStarted(in start);
            _hardListener?.OnGridBeamStarted(in start);
        }

        [ClientRpc]
        private void RpcGridBeamLineTelegraphed(int castId, int lineId, bool isHorizontal, int lineIndex,
            double telegraphServerTime, double strikeServerTime)
        {
            _clientPhaseLedger.OnGridBeamLineTelegraphed(castId, lineId, isHorizontal, lineIndex,
                telegraphServerTime, strikeServerTime);
            _hardListener?.OnGridBeamLineTelegraphed(castId, lineId, isHorizontal, lineIndex,
                telegraphServerTime, strikeServerTime);
        }

        [ClientRpc]
        private void RpcGridBeamLineStruck(int castId, int lineId, bool isHorizontal, int lineIndex,
            double scheduledServerTime)
        {
            _clientPhaseLedger.OnGridBeamLineStruck(castId, lineId, isHorizontal, lineIndex, scheduledServerTime);
            _hardListener?.OnGridBeamLineStruck(castId, lineId, isHorizontal, lineIndex, scheduledServerTime);
        }

        [ClientRpc]
        private void RpcGridBeamCancelled(int castId)
        {
            _clientPhaseLedger.OnGridBeamCancelled(castId);
            _hardListener?.OnGridBeamCancelled(castId);
        }

        [ClientRpc]
        private void RpcChainFanStarted(BossChainFanStart start)
        {
            _clientPhaseLedger.OnChainFanStarted(in start);
            _hardListener?.OnChainFanStarted(in start);
        }

        [ClientRpc]
        private void RpcChainFanBlasted(int castId, int zoneIndex, double scheduledServerTime)
        {
            _clientPhaseLedger.OnChainFanBlasted(castId, zoneIndex, scheduledServerTime);
            _hardListener?.OnChainFanBlasted(castId, zoneIndex, scheduledServerTime);
        }

        [ClientRpc]
        private void RpcChainFanDangerEnded(int castId, double scheduledServerTime)
        {
            _clientPhaseLedger.OnChainFanDangerEnded(castId, scheduledServerTime);
            _hardListener?.OnChainFanDangerEnded(castId, scheduledServerTime);
        }

        [ClientRpc]
        private void RpcChainFanCancelled(int castId)
        {
            _clientPhaseLedger.OnChainFanCancelled(castId);
            _hardListener?.OnChainFanCancelled(castId);
        }

        [ClientRpc]
        private void RpcTrackingAreaStarted(BossTrackingAreaStart start)
        {
            _clientPhaseLedger.OnTrackingAreaStarted(in start);
            _hardListener?.OnTrackingAreaStarted(in start);
        }

        [ClientRpc]
        private void RpcTrackingAreaMoved(int areaId, Vector3 center, double serverTime)
        {
            _clientPhaseLedger.OnTrackingAreaMoved(areaId, center, serverTime);
            _hardListener?.OnTrackingAreaMoved(areaId, center, serverTime);
        }

        [ClientRpc]
        private void RpcTrackingAreaFixed(int areaId, Vector3 center, double fixedServerTime)
        {
            _clientPhaseLedger.OnTrackingAreaFixed(areaId, center, fixedServerTime);
            _hardListener?.OnTrackingAreaFixed(areaId, center, fixedServerTime);
        }

        [ClientRpc]
        private void RpcTrackingAreaCancelled(int castId, int areaId)
        {
            _clientPhaseLedger.OnTrackingAreaCancelled(castId, areaId);
            _hardListener?.OnTrackingAreaCancelled(castId, areaId);
        }

        [ClientRpc]
        private void RpcBarrageStarted(BossBarrageStart start)
        {
            _clientPhaseLedger.OnBarrageStarted(in start);
            _hardListener?.OnBarrageStarted(in start);
        }

        [ClientRpc]
        private void RpcBarrageShotTelegraphed(BossBarrageShot shot)
        {
            _clientPhaseLedger.OnBarrageShotTelegraphed(in shot);
            _hardListener?.OnBarrageShotTelegraphed(in shot);
        }

        [ClientRpc]
        private void RpcBarrageShotFired(int castId, int shotIndex, int projectileId, bool isLaunched,
            double scheduledServerTime)
        {
            _clientPhaseLedger.OnBarrageShotFired(castId, shotIndex, projectileId, isLaunched, scheduledServerTime);
            _hardListener?.OnBarrageShotFired(castId, shotIndex, projectileId, isLaunched, scheduledServerTime);
        }

        [ClientRpc]
        private void RpcBarrageCancelled(int castId)
        {
            _clientPhaseLedger.OnBarrageCancelled(castId);
            _hardListener?.OnBarrageCancelled(castId);
        }

        [ClientRpc]
        private void RpcCircleBurstStarted(BossCircleBurstStart start)
        {
            _clientPhaseLedger.OnCircleBurstStarted(in start);
            _hardListener?.OnCircleBurstStarted(in start);
        }

        [ClientRpc]
        private void RpcCircleBurstStruck(int castId, double scheduledServerTime)
        {
            _clientPhaseLedger.OnCircleBurstStruck(castId, scheduledServerTime);
            _hardListener?.OnCircleBurstStruck(castId, scheduledServerTime);
        }

        [ClientRpc]
        private void RpcCircleBurstCancelled(int castId)
        {
            _clientPhaseLedger.OnCircleBurstCancelled(castId);
            _hardListener?.OnCircleBurstCancelled(castId);
        }

        /****************************************
        *   ClientRpc (Wolbaek Gimmick Events)
        ****************************************/

        [ClientRpc]
        private void RpcTalismanStarted(WolbaekTalismanStart start)
        {
            _clientWolbaekLedger.OnTalismanStarted(in start);
            _wolbaekListener?.OnTalismanStarted(in start);
        }

        [ClientRpc]
        private void RpcTalismanHoldStarted(int castId, double serverTime)
        {
            _clientWolbaekLedger.OnTalismanHoldStarted(castId, serverTime);
            _wolbaekListener?.OnTalismanHoldStarted(castId, serverTime);
        }

        [ClientRpc]
        private void RpcTalismanHoldReset(int castId, double serverTime, EWolbaekHoldReset reason, int combatId)
        {
            _clientWolbaekLedger.OnTalismanHoldReset(castId, serverTime, reason, combatId);
            _wolbaekListener?.OnTalismanHoldReset(castId, serverTime, reason, combatId);
        }

        [ClientRpc]
        private void RpcTalismanAttackTelegraphed(int castId, WolbaekTalismanAttack attack)
        {
            _clientWolbaekLedger.OnTalismanAttackTelegraphed(castId, in attack);
            _wolbaekListener?.OnTalismanAttackTelegraphed(castId, in attack);
        }

        [ClientRpc]
        private void RpcTalismanAttackStruck(int castId, int attackId, double strikeServerTime)
        {
            _clientWolbaekLedger.OnTalismanAttackStruck(castId, attackId, strikeServerTime);
            _wolbaekListener?.OnTalismanAttackStruck(castId, attackId, strikeServerTime);
        }

        [ClientRpc]
        private void RpcTalismanAttackCancelled(int castId, int attackId)
        {
            _clientWolbaekLedger.OnTalismanAttackCancelled(castId, attackId);
            _wolbaekListener?.OnTalismanAttackCancelled(castId, attackId);
        }

        [ClientRpc]
        private void RpcTalismanSucceeded(int castId, double serverTime, double counterEndServerTime)
        {
            _clientWolbaekLedger.OnTalismanSucceeded(castId, serverTime, counterEndServerTime);
            _wolbaekListener?.OnTalismanSucceeded(castId, serverTime, counterEndServerTime);
        }

        [ClientRpc]
        private void RpcTalismanCounterEnded(int castId)
        {
            _clientWolbaekLedger.OnTalismanCounterEnded(castId);
            _wolbaekListener?.OnTalismanCounterEnded(castId);
        }

        [ClientRpc]
        private void RpcTalismanCleared(int castId)
        {
            _clientWolbaekLedger.OnTalismanCleared(castId);
            _wolbaekListener?.OnTalismanCleared(castId);
        }

        [ClientRpc]
        private void RpcWardStarted(WolbaekWardStart start)
        {
            _clientWolbaekLedger.OnWardStarted(in start);
            _wolbaekListener?.OnWardStarted(in start);
        }

        [ClientRpc]
        private void RpcGatherHoldStarted(int castId, double serverTime)
        {
            _clientWolbaekLedger.OnGatherHoldStarted(castId, serverTime);
            _wolbaekListener?.OnGatherHoldStarted(castId, serverTime);
        }

        [ClientRpc]
        private void RpcGatherHoldReset(int castId)
        {
            _clientWolbaekLedger.OnGatherHoldReset(castId);
            _wolbaekListener?.OnGatherHoldReset(castId);
        }

        [ClientRpc]
        private void RpcGatherCompleted(int castId)
        {
            _clientWolbaekLedger.OnGatherCompleted(castId);
            _wolbaekListener?.OnGatherCompleted(castId);
        }

        [ClientRpc]
        private void RpcWardStageStarted(int castId, WolbaekWardStage stage)
        {
            _clientWolbaekLedger.OnStageStarted(castId, in stage);
            _wolbaekListener?.OnStageStarted(castId, in stage);
        }

        [ClientRpc]
        private void RpcWardStrikeTelegraphed(int castId, WolbaekWardStrike strike)
        {
            _clientWolbaekLedger.OnStrikeTelegraphed(castId, in strike);
            _wolbaekListener?.OnStrikeTelegraphed(castId, in strike);
        }

        [ClientRpc]
        private void RpcWardStrikeResolved(int castId, int stageIndex, int strikeIndex, double strikeServerTime)
        {
            _clientWolbaekLedger.OnStrikeResolved(castId, stageIndex, strikeIndex, strikeServerTime);
            _wolbaekListener?.OnStrikeResolved(castId, stageIndex, strikeIndex, strikeServerTime);
        }

        [ClientRpc]
        private void RpcWardLinkDamaged(int castId, double serverTime)
        {
            _wolbaekListener?.OnLinkDamaged(castId, serverTime);
        }

        [ClientRpc]
        private void RpcWardGraceStarted(int castId, int combatId, double graceEndServerTime)
        {
            _clientWolbaekLedger.OnGraceStarted(castId, combatId, graceEndServerTime);
            _wolbaekListener?.OnGraceStarted(castId, combatId, graceEndServerTime);
        }

        [ClientRpc]
        private void RpcWardGraceEnded(int castId, int combatId)
        {
            _clientWolbaekLedger.OnGraceEnded(castId, combatId);
            _wolbaekListener?.OnGraceEnded(castId, combatId);
        }

        [ClientRpc]
        private void RpcWardReviveWindowOpened(int castId)
        {
            _wolbaekListener?.OnReviveWindowOpened(castId);
        }

        [ClientRpc]
        private void RpcWardSucceeded(int castId, ushort phaseGenerationAtSuccess)
        {
            _clientWolbaekLedger.OnWardSucceeded(castId, phaseGenerationAtSuccess);
            _wolbaekListener?.OnWardSucceeded(castId, phaseGenerationAtSuccess);
        }

        [ClientRpc]
        private void RpcWardCleared(int castId)
        {
            _clientWolbaekLedger.OnWardCleared(castId);
            _wolbaekListener?.OnWardCleared(castId);
        }

        [ClientRpc]
        private void RpcSealStarted(WolbaekSealStart start)
        {
            _clientWolbaekLedger.OnSealStarted(in start);
            _wolbaekListener?.OnSealStarted(in start);
        }

        [ClientRpc]
        private void RpcSealTurnStarted(int castId, WolbaekSealTurn turn)
        {
            _clientWolbaekLedger.OnSealTurnStarted(castId, in turn);
            _wolbaekListener?.OnSealTurnStarted(castId, in turn);
        }

        [ClientRpc]
        private void RpcSealTrackingStarted(int castId, int turnIndex, double serverTime)
        {
            _clientWolbaekLedger.OnSealTrackingStarted(castId, turnIndex, serverTime);
            _wolbaekListener?.OnSealTrackingStarted(castId, turnIndex, serverTime);
        }

        [ClientRpc]
        private void RpcSealFoxfireDetached(int castId, int turnIndex, int foxfireIndex, double serverTime, Vector3 position)
        {
            _clientWolbaekLedger.OnSealFoxfireDetached(castId, turnIndex, foxfireIndex, serverTime, position);
            _wolbaekListener?.OnSealFoxfireDetached(castId, turnIndex, foxfireIndex, serverTime, position);
        }

        [ClientRpc]
        private void RpcSealSweepTelegraphed(int castId, WolbaekSealSweep sweep)
        {
            _clientWolbaekLedger.OnSealSweepTelegraphed(castId, in sweep);
            _wolbaekListener?.OnSealSweepTelegraphed(castId, in sweep);
        }

        [ClientRpc]
        private void RpcSealSweepResolved(int castId, int turnIndex, int sweepIndex, double serverTime)
        {
            _clientWolbaekLedger.OnSealSweepResolved(castId, turnIndex, sweepIndex, serverTime);
            _wolbaekListener?.OnSealSweepResolved(castId, turnIndex, sweepIndex, serverTime);
        }

        // 배열은 Weaver 기본 읽기라 상한을 여기서 본다. 넘으면 프로토콜 불일치로 보고 버린다
        [ClientRpc]
        private void RpcSealFoxfiresLocked(int castId, int turnIndex, double serverTime, Vector3[] centers)
        {
            if (!BossWolbaekNetSerializer.IsWithin(centers, Constants.Network.BOSS_WOLBAEK_MAX_FOXFIRES))
            {
                return;
            }

            _clientWolbaekLedger.OnSealFoxfiresLocked(castId, turnIndex, serverTime, centers);
            _wolbaekListener?.OnSealFoxfiresLocked(castId, turnIndex, serverTime, centers);
        }

        [ClientRpc]
        private void RpcSealTurnResolved(int castId, int turnIndex, double serverTime, int successCount, byte destroyedCoreMask)
        {
            _clientWolbaekLedger.OnSealTurnResolved(castId, turnIndex, serverTime, successCount, destroyedCoreMask);
            _wolbaekListener?.OnSealTurnResolved(castId, turnIndex, serverTime, successCount, destroyedCoreMask);
        }

        [ClientRpc]
        private void RpcSealSucceeded(int castId, double serverTime, double counterEndServerTime)
        {
            _clientWolbaekLedger.OnSealSucceeded(castId, serverTime, counterEndServerTime);
            _wolbaekListener?.OnSealSucceeded(castId, serverTime, counterEndServerTime);
        }

        [ClientRpc]
        private void RpcSealCounterEnded(int castId)
        {
            _clientWolbaekLedger.OnSealCounterEnded(castId);
            _wolbaekListener?.OnSealCounterEnded(castId);
        }

        [ClientRpc]
        private void RpcSealCleared(int castId)
        {
            _clientWolbaekLedger.OnSealCleared(castId);
            _wolbaekListener?.OnSealCleared(castId);
        }

        [ClientRpc]
        private void RpcFinalStarted(WolbaekFinalStart start)
        {
            _clientWolbaekLedger.OnFinalStarted(in start);
            _wolbaekListener?.OnFinalStarted(in start);
        }

        [ClientRpc]
        private void RpcFinalOuterFloorRemoved(int castId, double serverTime)
        {
            _clientWolbaekLedger.OnFinalOuterFloorRemoved(castId, serverTime);
            _wolbaekListener?.OnFinalOuterFloorRemoved(castId, serverTime);
        }

        [ClientRpc]
        private void RpcFinalYellowsSpawned(int castId, WolbaekFinalYellow[] yellows)
        {
            _clientWolbaekLedger.OnFinalYellowsSpawned(castId, yellows);
            _wolbaekListener?.OnFinalYellowsSpawned(castId, yellows);
        }

        [ClientRpc]
        private void RpcFinalYellowDestroyed(int castId, int yellowIndex, double serverTime)
        {
            _clientWolbaekLedger.OnFinalYellowDestroyed(castId, yellowIndex, serverTime);
            _wolbaekListener?.OnFinalYellowDestroyed(castId, yellowIndex, serverTime);
        }

        [ClientRpc]
        private void RpcFinalBundleStarted(int castId, WolbaekFinalBundle bundle)
        {
            _clientWolbaekLedger.OnFinalBundleStarted(castId, in bundle);
            _wolbaekListener?.OnFinalBundleStarted(castId, in bundle);
        }

        [ClientRpc]
        private void RpcFinalRedDetached(int castId, int bundleIndex, int redIndex, double serverTime, Vector3 position)
        {
            _clientWolbaekLedger.OnFinalRedDetached(castId, bundleIndex, redIndex, serverTime, position);
            _wolbaekListener?.OnFinalRedDetached(castId, bundleIndex, redIndex, serverTime, position);
        }

        [ClientRpc]
        private void RpcFinalRedsLocked(int castId, int bundleIndex, double serverTime, Vector3[] centers)
        {
            if (!BossWolbaekNetSerializer.IsWithin(centers, Constants.Network.BOSS_WOLBAEK_MAX_REDS))
            {
                return;
            }

            _clientWolbaekLedger.OnFinalRedsLocked(castId, bundleIndex, serverTime, centers);
            _wolbaekListener?.OnFinalRedsLocked(castId, bundleIndex, serverTime, centers);
        }

        [ClientRpc]
        private void RpcFinalBundleResolved(int castId, int bundleIndex, double serverTime)
        {
            _clientWolbaekLedger.OnFinalBundleResolved(castId, bundleIndex, serverTime);
            _wolbaekListener?.OnFinalBundleResolved(castId, bundleIndex, serverTime);
        }

        [ClientRpc]
        private void RpcFinalSucceeded(int castId, double serverTime)
        {
            _clientWolbaekLedger.OnFinalSucceeded(castId, serverTime);
            _wolbaekListener?.OnFinalSucceeded(castId, serverTime);
        }

        [ClientRpc]
        private void RpcFinalCleared(int castId)
        {
            _clientWolbaekLedger.OnFinalCleared(castId);
            _wolbaekListener?.OnFinalCleared(castId);
        }

        [ClientRpc]
        private void RpcWarpDeparted(int castId, Vector3 departure, double serverTime)
        {
            _clientWolbaekLedger.OnWarpDeparted(castId, departure, serverTime);
            _wolbaekListener?.OnWarpDeparted(castId, departure, serverTime);
        }

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        private void OnLifeStateSynced(EParticipantLifeState previous, EParticipantLifeState next)
        {
            if (!IsFighting(next, _flowState))
            {
                _clientPhaseLedger.Clear();
                _clientWolbaekLedger.Clear();
            }

            LifeStateChanged?.Invoke(next);
        }

        private void OnFlowStateSynced(EBossFlowState previous, EBossFlowState next)
        {
            if (!IsFighting(_lifeState, next))
            {
                _clientPhaseLedger.Clear();
                _clientWolbaekLedger.Clear();
            }

            FlowStateChanged?.Invoke(next);
        }

        private void OnPhaseStateSynced(EBossPhaseState previous, EBossPhaseState next)
        {
            PhaseStateChanged?.Invoke(next);
        }

        private void OnPhaseGenerationSynced(ushort previous, ushort next)
        {
            PhaseGenerationChanged?.Invoke(next);
        }

        // 등장 대기와 전투 중에만 전용 공격이 진행된다
        private static bool IsFighting(EParticipantLifeState lifeState, EBossFlowState flowState)
        {
            return (lifeState == EParticipantLifeState.Alive)
                && ((flowState == EBossFlowState.Appearing) || (flowState == EBossFlowState.Combat));
        }

        /****************************************
        *             Serialization
        ****************************************/

        // 스폰 초기 상태에만 기록을 덧붙인다. 이후 변경분(initialState 거짓)에는 쓰지 않으므로 읽는 쪽도 초기 상태에서만 읽는다
        public override void OnSerialize(NetworkWriter writer, bool initialState)
        {
            base.OnSerialize(writer, initialState);
            if (initialState)
            {
                _serverLedger.Write(writer);
                _serverPhaseLedger.Write(writer);
                _serverWolbaekLedger.Write(writer);
            }
        }

        public override void OnDeserialize(NetworkReader reader, bool initialState)
        {
            base.OnDeserialize(reader, initialState);
            if (initialState)
            {
                _clientLedger.Read(reader);
                _clientPhaseLedger.Read(reader);
                _clientWolbaekLedger.Read(reader);
            }
        }
    }
}
