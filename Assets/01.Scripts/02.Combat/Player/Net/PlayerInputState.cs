// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Combat
{
    /// <summary> 소유 플레이어의 입력 창구와 소유자 전용 상태, 판정은 서버 처리기가 한다 </summary>
    [RequireComponent(typeof(PlayerCombatState))]
    public class PlayerInputState : NetworkBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        // 서버 입력 검증과 같은 주기로 묶어 보낸다. 더 자주 부르면 마지막 의도만 남기고 다음 칸에 보낸다
        private const double SEND_INTERVAL_SECONDS = 1.0 / Constants.Network.INPUT_SEND_RATE_MAX;

        // 아래 세 값은 소유자만 받는다(OnValidate에서 syncMode를 Owner로 고정)

        // 서버가 마지막으로 헤더를 승인한 순번. 소유 예측이 들어오면 이 순번 뒤의 입력만 다시 적용한다
        [SyncVar]
        private uint _lastProcessedSequence = 0;

        [SyncVar]
        private int _sessionEpoch = 0;

        [SyncVar]
        private int _participantId = 0;

        private uint              _nextSequence       = 0;
        private double            _nextSendTime       = 0.0;
        private bool              _hasPendingMove     = false;
        private Vector3           _pendingDestination = Vector3.zero;
        private bool              _hasPendingAttack   = false;
        private ushort            _pendingAimYaw      = 0;
        private bool              _hasPendingSkill    = false;
        private bool              _hasPendingRevive   = false;
        private ESkillRequestKind _pendingSkillKind;
        private byte              _pendingSkillSlot;
        private ushort            _pendingSkillAim;
        private Vector3           _pendingSkillPoint;

        [SyncVar] private double _skillQReadyAt;
        [SyncVar] private double _skillWReadyAt;
        [SyncVar] private double _skillEReadyAt;
        [SyncVar] private double _skillRReadyAt;
        [SyncVar] private double _ascensionReadyAt;

        // 소유자 이동 예측의 기준. 원격 관찰자는 NetworkTransform으로 위치를 받으므로 보내지 않는다
        [SyncVar] private PlayerMoveAuthority _moveAuthority;
        [SyncVar] private float               _moveSpeed;

        // 회피 쿨다운이 끝나는 서버 시각. HUD와 입력의 미리 거르기가 읽는다
        [SyncVar] private double _dodgeReadyAt;

        // 부활 요청을 받았지만 안전 복귀 지점이 모두 위험해 재평가를 기다린다. 복귀 버튼의 대기 표시만 읽는다.
        // 다른 참가자에게 보일 이유가 없어 관찰자 전체에 보내는 PlayerCombatState가 아니라 소유자 전용인 여기에 둔다
        [SyncVar] private bool _isWaitingSafePoint;

        private double _nextMoveSendTime = 0.0;
        private bool   _hasPendingDodge  = false;
        private ushort _pendingDodgeYaw  = 0;

        private bool        _hasPendingPotion  = false;
        private EPotionSize _pendingPotionSize = EPotionSize.Small;

        /****************************************
        *              Properties
        ****************************************/

        public uint LastProcessedSequence => _lastProcessedSequence;
        public int  SessionEpoch          => _sessionEpoch;
        public int  ParticipantId         => _participantId;

        // 서버 부트스트랩이 OnStartServer 알림을 받고 넣는다. 클라 빌드에는 구현 타입이 없다
        public IPlayerInputServerHandler ServerHandler      { private get; set; } = null;
        public IPlayerSkillServerHandler SkillServerHandler { private get; set; } = null;

        // 서버가 거절한 입력의 순번과 사유. 소유 클라이언트에서만 온다
        public event Action<uint, ERejectReason> InputRejected;
        public event Action<uint, int, ERejectReason> SkillResult;
        public event Action<DamageFeedback> DamageFeedbackReceived;

        public PlayerMoveAuthority MoveAuthority => _moveAuthority;
        public float               MoveSpeed    => _moveSpeed;

        // 소유 클라이언트가 입력을 실제로 보낸 순간에 알린다. 예측은 이때 시작해야 서버와 같은 순번을 쓴다
        public event Action<uint, Vector3> MoveSent;

        // 공격, 기술처럼 서버가 처리하는 틱에 이동을 멈추는 입력
        public event Action<uint> ActionSent;

        // 기술 요청을 실제로 보낸 순번과 종류, 슬롯. SkillResult의 순번을 어떤 기술의 결과인지로 바꿀 때 쓴다
        public event Action<uint, ESkillRequestKind, byte> SkillSent;

        // 회피를 보낸 순번과 수평 방향. 예측은 회피를 이동 순번 하나로 보고 서버 확정 상태와 짝짓는다
        public event Action<uint, Vector3> DodgeSent;

        public double DodgeReadyAt => _dodgeReadyAt;

        public bool IsWaitingSafePoint => _isWaitingSafePoint;

        public double SkillQReadyAt    => _skillQReadyAt;
        public double SkillWReadyAt    => _skillWReadyAt;
        public double SkillEReadyAt    => _skillEReadyAt;
        public double SkillRReadyAt    => _skillRReadyAt;
        public double AscensionReadyAt => _ascensionReadyAt;

        /****************************************
        *              Unity Event
        ****************************************/

        // 순번, 세션 번호는 다른 관찰자에게 보낼 이유가 없고 순번은 입력마다 바뀌어 대역폭만 쓴다
        protected override void OnValidate()
        {
            base.OnValidate();
            syncMode = SyncMode.Owner;
        }

        private void Update()
        {
            if (!isOwned)
            {
                return;
            }

            double now = Time.unscaledTimeAsDouble;

            // 서버는 참가자별 경로 재계산 간격 안에 온 이동을 미뤄 처리한다. 같은 간격으로 보내야 예측이 경로를 시작하는
            // 시점과 서버가 시작하는 시점이 지연만큼만 어긋나고, 미뤄진 몫만큼 매번 보정되지 않는다
            bool canSendMove = (_hasPendingMove) && (now >= _nextMoveSendTime);
            if ((!canSendMove) && (!_hasPendingAttack) && (!_hasPendingSkill) && (!_hasPendingRevive) && (!_hasPendingDodge)
                && (!_hasPendingPotion))
            {
                return;
            }

            if (now < _nextSendTime)
            {
                return;
            }

            _nextSendTime = now + SEND_INTERVAL_SECONDS;

            // 상충 입력은 회피, 물약, 기술, 기본 공격 순서다
            if (_hasPendingDodge)
            {
                _hasPendingDodge = false;
                PlayerInputHeader dodgeHeader = NextHeader();
                CmdDodge(new PlayerDodgeInput(dodgeHeader, _pendingDodgeYaw));
                DodgeSent?.Invoke(dodgeHeader.Sequence, FacingRules.DecodeYaw(_pendingDodgeYaw));
                return;
            }

            // 물약은 Command가 아니라 메시지로 보낸다. 같은 연결의 신뢰 채널이라 순번 순서가 Command와 섞이지 않는다
            if (_hasPendingPotion)
            {
                _hasPendingPotion = false;
                NetworkClient.Send(new PotionUseRequestMessage(NextHeader(), _pendingPotionSize));
                return;
            }

            if (_hasPendingRevive)
            {
                _hasPendingRevive = false;
                CmdRevive(NextHeader());
                return;
            }

            if (_hasPendingSkill)
            {
                _hasPendingSkill = false;
                PlayerInputHeader skillHeader = NextHeader();
                CmdSkill(new PlayerSkillInput(skillHeader, _pendingSkillKind, _pendingSkillSlot, _pendingSkillAim, _pendingSkillPoint));
                SkillSent?.Invoke(skillHeader.Sequence, _pendingSkillKind, _pendingSkillSlot);
                ActionSent?.Invoke(skillHeader.Sequence);
                return;
            }

            // 공격을 먼저 보낸다. 이동은 다음 칸에 나가고, 그 사이 공격 준비를 취소하지 않는다
            if (_hasPendingAttack)
            {
                _hasPendingAttack = false;
                PlayerInputHeader attackHeader = NextHeader();
                CmdAttack(new PlayerAttackInput(attackHeader, _pendingAimYaw));
                ActionSent?.Invoke(attackHeader.Sequence);
                return;
            }

            if (!canSendMove)
            {
                return;
            }

            _hasPendingMove   = false;
            _nextMoveSendTime = now + Constants.Player.MOVE_REPATH_MIN_INTERVAL_SECONDS;
            PlayerInputHeader moveHeader = NextHeader();
            CmdMove(new PlayerMoveInput(moveHeader, _pendingDestination));
            MoveSent?.Invoke(moveHeader.Sequence, _pendingDestination);
        }

        /****************************************
        *            Server Callbacks
        ****************************************/

        public override void OnStartServer()
        {
            ServerLinks.NotifyStarted(this);
        }

        public override void OnStopServer()
        {
            ServerLinks.NotifyStopped(this);
        }

        /****************************************
        *            Client Callbacks
        ****************************************/

        // 재접속으로 새 개체를 받아도 서버가 승인한 순번 뒤에서 잇는다
        public override void OnStartAuthority()
        {
            _nextSequence     = _lastProcessedSequence;
            _nextMoveSendTime = 0.0;
            _hasPendingMove   = false;
            _hasPendingAttack = false;
            _hasPendingSkill  = false;
            _hasPendingRevive = false;
            _hasPendingDodge  = false;
            _hasPendingPotion = false;
#if !UNITY_SERVER
            if (isClient)
            {
                if (!TryGetComponent(out DamageNumberBinder binder))
                {
                    binder = gameObject.AddComponent<DamageNumberBinder>();
                }
                binder.Bind(this);
            }
#endif
        }

        public override void OnStopAuthority()
        {
#if !UNITY_SERVER
            if (TryGetComponent(out DamageNumberBinder binder))
            {
                binder.Unbind();
            }
#endif
        }

        public override void OnStopClient()
        {
#if !UNITY_SERVER
            if (TryGetComponent(out DamageNumberBinder binder))
            {
                binder.Unbind();
            }
#endif
        }

        /****************************************
        *            Client Requests
        ****************************************/

        /// <summary> 기술이 다시 쓸 수 있게 되는 서버 시각, 강림은 세 칸이 공용 값 하나를 쓴다 </summary>
        public double GetSkillReadyAt(ESkillRequestKind kind, byte slot)
        {
            if (kind == ESkillRequestKind.Ascension)
            {
                return _ascensionReadyAt;
            }

            switch (slot)
            {
                case Constants.Combat.SKILL_SLOT_Q:
                    return _skillQReadyAt;
                case Constants.Combat.SKILL_SLOT_W:
                    return _skillWReadyAt;
                case Constants.Combat.SKILL_SLOT_E:
                    return _skillEReadyAt;
                case Constants.Combat.SKILL_SLOT_R:
                    return _skillRReadyAt;
                default:
                    return 0.0;
            }
        }

        /// <summary> 클릭한 지면 좌표로 이동을 요청한다, 소유 클라이언트에서만 동작한다 </summary>
        public void RequestMove(Vector3 destination)
        {
            if (!isOwned)
            {
                return;
            }

            _pendingDestination = destination;
            _hasPendingMove     = true;
        }

        /// <summary> 조준 방향으로 기본 공격을 요청한다, 수평 성분이 없으면 무시한다 </summary>
        public void RequestAttack(Vector3 aimDirection)
        {
            if (!isOwned)
            {
                return;
            }

            if (!FacingRules.TryGetHorizontalDirection(aimDirection, out Vector3 direction))
            {
                return;
            }

            _pendingAimYaw    = FacingRules.EncodeYaw(direction);
            _hasPendingAttack = true;
        }

        /// <summary> 방향으로 회피를 요청한다, 수평 성분이 없으면 무시한다. 보관한 기본 공격은 버린다 </summary>
        public void RequestDodge(Vector3 direction)
        {
            if (!isOwned)
            {
                return;
            }

            if (!FacingRules.TryGetHorizontalDirection(direction, out Vector3 horizontal))
            {
                return;
            }

            _pendingDodgeYaw  = FacingRules.EncodeYaw(horizontal);
            _hasPendingDodge  = true;
            _hasPendingAttack = false;
        }

        /// <summary> 물약 사용을 요청한다, 잔량과 쿨다운과 회복은 서버가 판정하고 결과는 PotionClient로 온다 </summary>
        public void RequestPotion(EPotionSize size)
        {
            if (!isOwned)
            {
                return;
            }

            _pendingPotionSize = size;
            _hasPendingPotion  = true;
        }

        /// <summary> 사망 뒤 부활을 요청한다, 대기 시간과 횟수는 서버가 판정한다 </summary>
        public void RequestRevive()
        {
            if (!isOwned)
            {
                return;
            }

            _hasPendingRevive = true;
        }

        private PlayerInputHeader NextHeader()
        {
            ++_nextSequence;

            // predictedTime은 이 입력이 서버에 도착할 무렵의 서버 시각 추정이다
            return new PlayerInputHeader(Constants.Network.PROTOCOL_VERSION, _sessionEpoch, _participantId,
                _nextSequence, NetworkTime.predictedTime);
        }

        /// <summary> 방향 기술은 키 입력 때, 지점 기술은 지정을 확정한 뒤 호출한다, 취소된 지정은 서버로 보내지 않는다 </summary>
        public void RequestSkill(ESkillRequestKind kind, byte slot, Vector3 point, Vector3 aimDirection)
        {
            if (!isOwned)
            {
                return;
            }

            ushort yaw = FacingRules.EncodeYaw(Vector3.forward);
            if (FacingRules.TryGetHorizontalDirection(aimDirection, out Vector3 direction))
            {
                yaw = FacingRules.EncodeYaw(direction);
            }

            _pendingSkillKind  = kind;
            _pendingSkillSlot  = slot;
            _pendingSkillAim   = yaw;
            _pendingSkillPoint = point;
            _hasPendingSkill   = true;
            _hasPendingAttack  = false;
            _hasPendingMove    = false;
        }

        /****************************************
        *               Commands
        ****************************************/

        [Command]
        private void CmdMove(PlayerMoveInput input)
        {
            ServerHandler?.HandleMove(in input);
        }

        [Command]
        private void CmdAttack(PlayerAttackInput input)
        {
            ServerHandler?.HandleAttack(in input);
        }

        [Command]
        private void CmdDodge(PlayerDodgeInput input)
        {
            ServerHandler?.HandleDodge(in input);
        }

        // Mirror의 기본 requiresAuthority로 소유 연결만 이 Command를 호출할 수 있다
        [Command]
        private void CmdSkill(PlayerSkillInput input)
        {
            SkillServerHandler?.HandleSkill(in input);
        }

        [Command]
        private void CmdRevive(PlayerInputHeader header)
        {
            ServerHandler?.HandleRevive(in header);
        }

        /****************************************
        *                Server
        ****************************************/

        [Server]
        public void SetIdentity(int sessionEpoch, int participantId)
        {
            _sessionEpoch  = sessionEpoch;
            _participantId = participantId;
        }

        [Server]
        public void SetLastProcessedSequence(uint sequence)
        {
            _lastProcessedSequence = sequence;
        }

        [Server]
        public void SetSkillCooldowns(double q, double w, double e, double r, double ascension)
        {
            _skillQReadyAt = q;
            _skillWReadyAt = w;
            _skillEReadyAt = e;
            _skillRReadyAt = r;
            _ascensionReadyAt = ascension;
        }

        // SyncVar는 같은 값이면 더럽히지 않으므로 멈춰 있는 동안에는 전송되지 않는다
        [Server]
        public void SetMoveAuthority(in PlayerMoveAuthority authority)
        {
            _moveAuthority = authority;
        }

        [Server]
        public void SetMoveSpeed(float moveSpeed)
        {
            _moveSpeed = moveSpeed;
        }

        [Server]
        public void SetWaitingSafePoint(bool waiting)
        {
            _isWaitingSafePoint = waiting;
        }

        [Server]
        public void SetDodgeReadyAt(double readyAt)
        {
            _dodgeReadyAt = readyAt;
        }

        [Server]
        public void SendSkillResult(uint sequence, int castInstanceId, ERejectReason reason)
        {
            if (connectionToClient == null)
            {
                return;
            }

            TargetSkillResult(sequence, castInstanceId, reason);
        }

        [Server]
        public void SendInputRejected(uint sequence, ERejectReason reason)
        {
            if (connectionToClient == null)
            {
                return;
            }

            TargetInputRejected(sequence, reason);
        }

        // 입력 위반이 임계값을 넘은 연결을 끊는다
        [Server]
        public void DisconnectOwner()
        {
            connectionToClient?.Disconnect();
        }

        [Server]
        public void SendDamageFeedback(in DamageFeedback feedback)
        {
            if ((connectionToClient == null) || (!DamageFeedbackRules.IsValid(in feedback)))
            {
                return;
            }
            TargetDamageFeedback(feedback.SessionEpoch, feedback.Sequence, feedback.TargetCombatId,
                feedback.WorldPosition, feedback.Amount, feedback.IsCritical, feedback.ServerTime, feedback.IsDamageOverTime);
        }

        /****************************************
        *               TargetRpc
        ****************************************/

        [TargetRpc]
        private void TargetInputRejected(uint sequence, ERejectReason reason)
        {
            InputRejected?.Invoke(sequence, reason);
        }

        [TargetRpc]
        private void TargetSkillResult(uint sequence, int castInstanceId, ERejectReason reason)
        {
            SkillResult?.Invoke(sequence, castInstanceId, reason);
        }

        [TargetRpc]
        private void TargetDamageFeedback(int sessionEpoch, uint sequence, int targetCombatId, Vector3 worldPosition,
            float amount, bool isCritical, double serverTime, bool isDamageOverTime)
        {
            if (!isOwned)
            {
                return;
            }
            var feedback = new DamageFeedback(sessionEpoch, sequence, targetCombatId, worldPosition,
                amount, isCritical, serverTime, isDamageOverTime);
            DamageFeedbackReceived?.Invoke(feedback);
        }
    }
}
