// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Network
{
    // 재료 차감과 완제품 지급은 API가 한 트랜잭션으로 확정한다. 마을은 형식과 레시피 존재만 보고 중계한다.
    // operationId는 클라이언트가 만들고 응답을 잃었을 때만 같은 값으로 다시 보낸다. 참가자마다 하나만 보낸다
    // TODO(민성): 의술사 NPC 위치와 상호작용 거리가 정해지지 않아 거리 판정을 하지 않는다. NPC 배치가 정해지면 서버 위치로 막는다
    /// <summary> 물약 제작 요청을 생활 API로 중계한다, 메인 스레드에서만 부른다 </summary>
    public sealed class PotionCraftServer : IBackendResponseListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string OPERATION_ID_FORMAT = "D";

        private readonly BackendGateway                 _gateway;
        private readonly ITownAudience                  _audience;
        private readonly LifeConfigSO                   _life;
        private readonly Action<int>                    _crafted;
        private readonly Dictionary<int, Guid>          _inFlightByMember;
        private readonly Dictionary<int, PendingCraft>  _pendingByRequest;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> crafted는 제작이 확정된 참가자 번호로 불린다(생활 재고 갱신용) </summary>
        public PotionCraftServer(BackendGateway gateway, ITownAudience audience, LifeConfigSO life, Action<int> crafted)
        {
            if ((audience == null) || (life == null))
            {
                throw new ArgumentNullException(nameof(audience), "물약 제작에 필요한 참조가 없다");
            }

            _gateway          = gateway;
            _audience         = audience;
            _life             = life;
            _crafted          = crafted;
            _inFlightByMember = new Dictionary<int, Guid>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _pendingByRequest = new Dictionary<int, PendingCraft>(Constants.Session.MAX_SESSION_PARTICIPANTS);
        }

        /// <summary> 제작을 보낸다. None이면 결과가 LifeResultMessage로 따로 간다 </summary>
        public ERejectReason Request(TownMember member, in LifeRequestMessage message)
        {
            if (member == null)
            {
                return ERejectReason.InvalidTarget;
            }

            if (_gateway == null)
            {
                return ERejectReason.BackendUnavailable;
            }

            if (!member.HasBackendIdentity)
            {
                return ERejectReason.SessionMismatch;
            }

            if ((message.OperationId == Guid.Empty) || (message.Count <= 0) || (message.Count > Constants.Town.MAX_POTION_CRAFT_COUNT)
                || (!IsKnownPotion(message.PotionDefinitionId)))
            {
                return ERejectReason.InvalidTarget;
            }

            if (_inFlightByMember.TryGetValue(member.ParticipantId, out Guid inFlight))
            {
                return (inFlight == message.OperationId) ? ERejectReason.None : ERejectReason.PendingCommit;
            }

            string operationId = message.OperationId.ToString(OPERATION_ID_FORMAT);
            var body = new LifePotionCraftRequest(operationId, member.AccountId, member.AccountLeaseId, message.PotionDefinitionId,
                message.Count);
            BackendRequest request = BackendRequest.Post(LifeRoutes.POTION_CRAFTS, body, typeof(OperationResult), operationId,
                Constants.Network.API_TIMEOUT_SECONDS);
            int requestId = _gateway.Send(request, this);
            _inFlightByMember[member.ParticipantId] = message.OperationId;
            _pendingByRequest[requestId]            = new PendingCraft(member.ParticipantId, message.OperationId);
            return ERejectReason.None;
        }

        public void RemoveMember(int participantId)
        {
            _inFlightByMember.Remove(participantId);
        }

        // 게이트웨이가 메인 스레드의 Pump에서 부른다
        public void OnBackendResponse(in BackendResponse response)
        {
            if (!_pendingByRequest.TryGetValue(response.RequestId, out PendingCraft pending))
            {
                return;
            }

            _pendingByRequest.Remove(response.RequestId);
            if ((!_inFlightByMember.TryGetValue(pending.ParticipantId, out Guid inFlight)) || (inFlight != pending.OperationId))
            {
                return;
            }

            _inFlightByMember.Remove(pending.ParticipantId);
            var result = response.Payload as OperationResult;
            ERejectReason reason;
            if ((response.IsSucceeded) && (result != null) && (result.Status == ContractCodes.OperationStatus.COMMITTED))
            {
                reason = ERejectReason.None;
            }
            else if ((TownRejectRules.IsDefiniteRejection(in response))
                || ((result != null) && (result.Status == ContractCodes.OperationStatus.REJECTED)))
            {
                reason = TownRejectRules.ReasonOf(in response, result, ERejectReason.InvalidTarget);
            }
            else
            {
                // 결과를 모른다. 같은 operationId로 다시 보내면 API가 처음 결과를 돌려준다
                reason = ERejectReason.BackendUnavailable;
            }

            _audience.Send(pending.ParticipantId, new LifeResultMessage(pending.OperationId, ELifeAction.PotionCraft, reason, 0, 0));
            if (reason == ERejectReason.None)
            {
                _crafted?.Invoke(pending.ParticipantId);
            }
        }

        private bool IsKnownPotion(string definitionId)
        {
            if (string.IsNullOrEmpty(definitionId))
            {
                return false;
            }

            IReadOnlyList<LifeConfigSO.PotionDefinition> potions = _life.Potions;
            for (int i = 0; i < potions.Count; ++i)
            {
                if ((potions[i] != null) && (string.Equals(potions[i].DefinitionId, definitionId, StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }

        /****************************************
        *               Records
        ****************************************/

        private readonly struct PendingCraft
        {
            private readonly int  _participantId;
            private readonly Guid _operationId;

            public int  ParticipantId => _participantId;
            public Guid OperationId   => _operationId;

            public PendingCraft(int participantId, Guid operationId)
            {
                _participantId = participantId;
                _operationId   = operationId;
            }
        }
    }
}
