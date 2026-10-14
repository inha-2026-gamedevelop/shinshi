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
    public class BossState : NetworkBehaviour, IBossEventListener, IBossMucheolEventListener, IBossHardPhaseEventListener
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

        /****************************************
        *                Fields
        ****************************************/

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

        // 서버 기록과 클라이언트 기록을 나눠 호스트에서 같은 사건이 두 번 쌓이지 않게 한다
        private readonly BossEffectLedger _serverLedger = new BossEffectLedger();
        private readonly BossEffectLedger _clientLedger = new BossEffectLedger();

        private readonly BossPhaseEffectLedger _serverPhaseLedger = new BossPhaseEffectLedger();
        private readonly BossPhaseEffectLedger _clientPhaseLedger = new BossPhaseEffectLedger();

        private IBossStateListener          _listener;
        private IBossMucheolStateListener   _mucheolListener; // 수신부가 구현했을 때만 있다
        private IBossHardPhaseStateListener _hardListener;    // 수신부가 구현했을 때만 있다
        private bool                        _clientStarted = false;

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

        /// <summary> 이 프리팹이 표현하는 보스 정의 ID, 비어 있으면 기본 프리팹이다 </summary>
        public string PrefabBossId => _prefabBossId;

        public event Action<EParticipantLifeState> LifeStateChanged;
        public event Action<EBossFlowState>        FlowStateChanged;
        public event Action<EBossPhaseState>       PhaseStateChanged;

        /****************************************
        *            Client Callbacks
        ****************************************/

        public override void OnStartClient()
        {
            _clientStarted = true;
            _clientLedger.Replay(_listener);
            _clientPhaseLedger.Replay(_mucheolListener, _hardListener);
        }

        public override void OnStopClient()
        {
            _clientStarted = false;
            _clientLedger.Clear();
            _clientPhaseLedger.Clear();
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
            if (_clientStarted)
            {
                _clientLedger.Replay(_listener);
                _clientPhaseLedger.Replay(_mucheolListener, _hardListener);
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
            }
        }

        /// <summary> 다음 스폰에 다시 쓸 때 이전 전투의 기록을 지운다 </summary>
        [Server]
        public void ResetServerRecord()
        {
            _serverLedger.Clear();
            _serverPhaseLedger.Clear();
        }

        [Server]
        public void OnTelegraphStarted(in BossTelegraphStart start)
        {
            _serverLedger.OnTelegraphStarted(in start);
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
        public void OnDashStopped(int castId, Vector3 position, bool stoppedByWall)
        {
            _serverLedger.OnDashStopped(castId, position, stoppedByWall);
            RpcDashStopped(castId, position, stoppedByWall);
        }

        [Server]
        public void OnFinished(int castId)
        {
            _serverLedger.OnFinished(castId);
            _serverPhaseLedger.OnCastEnded(castId);
            RpcFinished(castId);
        }

        [Server]
        public void OnCancelled(int castId)
        {
            _serverLedger.OnCancelled(castId);
            _serverPhaseLedger.OnCastEnded(castId);
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
        public void OnBeamCancelled(int beamId)
        {
            _serverLedger.OnBeamCancelled(beamId);
            RpcBeamCancelled(beamId);
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
        public void OnStarCancelled(int starId)
        {
            _serverLedger.OnStarCancelled(starId);
            RpcStarCancelled(starId);
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
        public void OnMultiDashDashStopped(int castId, int dashIndex, Vector3 position, bool stoppedByWall)
        {
            _serverPhaseLedger.OnMultiDashDashStopped(castId, dashIndex, position, stoppedByWall);
            RpcMultiDashDashStopped(castId, dashIndex, position, stoppedByWall);
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
        public void OnTrackingAreaCancelled(int areaId)
        {
            _serverPhaseLedger.OnTrackingAreaCancelled(areaId);
            RpcTrackingAreaCancelled(areaId);
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
        *               ClientRpc
        ****************************************/

        [ClientRpc]
        private void RpcTelegraphStarted(BossTelegraphStart start)
        {
            _clientLedger.OnTelegraphStarted(in start);
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
        private void RpcDashStopped(int castId, Vector3 position, bool stoppedByWall)
        {
            _clientLedger.OnDashStopped(castId, position, stoppedByWall);
            _listener?.OnDashStopped(castId, position, stoppedByWall);
        }

        [ClientRpc]
        private void RpcFinished(int castId)
        {
            _clientLedger.OnFinished(castId);
            _clientPhaseLedger.OnCastEnded(castId);
            _listener?.OnFinished(castId);
        }

        [ClientRpc]
        private void RpcCancelled(int castId)
        {
            _clientLedger.OnCancelled(castId);
            _clientPhaseLedger.OnCastEnded(castId);
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
        private void RpcBeamCancelled(int beamId)
        {
            _clientLedger.OnBeamCancelled(beamId);
            _listener?.OnBeamCancelled(beamId);
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
        private void RpcStarCancelled(int starId)
        {
            _clientLedger.OnStarCancelled(starId);
            _listener?.OnStarCancelled(starId);
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
        private void RpcMultiDashDashStopped(int castId, int dashIndex, Vector3 position, bool stoppedByWall)
        {
            _clientPhaseLedger.OnMultiDashDashStopped(castId, dashIndex, position, stoppedByWall);
            _mucheolListener?.OnMultiDashDashStopped(castId, dashIndex, position, stoppedByWall);
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
        private void RpcTrackingAreaCancelled(int areaId)
        {
            _clientPhaseLedger.OnTrackingAreaCancelled(areaId);
            _hardListener?.OnTrackingAreaCancelled(areaId);
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
        *              SyncVar Hooks
        ****************************************/

        private void OnLifeStateSynced(EParticipantLifeState previous, EParticipantLifeState next)
        {
            if (!IsFighting(next, _flowState))
            {
                _clientPhaseLedger.Clear();
            }

            LifeStateChanged?.Invoke(next);
        }

        private void OnFlowStateSynced(EBossFlowState previous, EBossFlowState next)
        {
            if (!IsFighting(_lifeState, next))
            {
                _clientPhaseLedger.Clear();
            }

            FlowStateChanged?.Invoke(next);
        }

        private void OnPhaseStateSynced(EBossPhaseState previous, EBossPhaseState next)
        {
            PhaseStateChanged?.Invoke(next);
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
            }
        }

        public override void OnDeserialize(NetworkReader reader, bool initialState)
        {
            base.OnDeserialize(reader, initialState);
            if (initialState)
            {
                _clientLedger.Read(reader);
                _clientPhaseLedger.Read(reader);
            }
        }
    }
}
