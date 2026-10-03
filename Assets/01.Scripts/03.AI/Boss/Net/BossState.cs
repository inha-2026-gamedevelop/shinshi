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
    public class BossState : NetworkBehaviour, IBossEventListener
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

        /****************************************
        *                Fields
        ****************************************/

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

        private IBossStateListener _listener;
        private bool               _clientStarted = false;

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
        }

        public override void OnStopClient()
        {
            _clientStarted = false;
            _clientLedger.Clear();
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
            _listener = listener;
            if (_clientStarted)
            {
                _clientLedger.Replay(_listener);
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
        }

        /// <summary> 다음 스폰에 다시 쓸 때 이전 전투의 기록을 지운다 </summary>
        [Server]
        public void ResetServerRecord()
        {
            _serverLedger.Clear();
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
            RpcFinished(castId);
        }

        [Server]
        public void OnCancelled(int castId)
        {
            _serverLedger.OnCancelled(castId);
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
            _listener?.OnFinished(castId);
        }

        [ClientRpc]
        private void RpcCancelled(int castId)
        {
            _clientLedger.OnCancelled(castId);
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

        /****************************************
        *              SyncVar Hooks
        ****************************************/

        private void OnLifeStateSynced(EParticipantLifeState previous, EParticipantLifeState next)
        {
            LifeStateChanged?.Invoke(next);
        }

        private void OnFlowStateSynced(EBossFlowState previous, EBossFlowState next)
        {
            FlowStateChanged?.Invoke(next);
        }

        private void OnPhaseStateSynced(EBossPhaseState previous, EBossPhaseState next)
        {
            PhaseStateChanged?.Invoke(next);
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
            }
        }

        public override void OnDeserialize(NetworkReader reader, bool initialState)
        {
            base.OnDeserialize(reader, initialState);
            if (initialState)
            {
                _clientLedger.Read(reader);
            }
        }
    }
}
