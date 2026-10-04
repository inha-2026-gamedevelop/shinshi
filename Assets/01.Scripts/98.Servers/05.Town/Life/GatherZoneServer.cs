// System
using System;
using System.Collections.Generic;
using System.Globalization;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 마을 약초 노드의 선점, 채집, 재생을 판정하고 완료를 생활 API로 저장한다, 서버 메인 스레드에서만 부른다 </summary>
    public sealed class GatherZoneServer : IBackendResponseListener
    {
        // 노드 상태는 이 프로세스 메모리에만 있다. 선점은 먼저 받은 요청 하나만 이긴다(메인 스레드라 같은 틱 안에서도 순서가 있다).
        // 계정당 선점은 하나다. 시작 때 서버 캐릭터 위치로 거리를 보고, 선점 중에 움직이면 비용 없이 취소한다.
        // 채집 시간이 지나면 Completing으로 바꾸고 operationId를 하나 정해 저장을 보낸다. Completing은 API의 확정 결과로만 끝난다:
        // 확정이면 Depleted(재생 대기), 확정 거절이면 비용 없이 Available. 결과를 모르면 같은 operationId로 다시 보내므로
        // 약초는 두 번 지급되지 않는다. 연결이 끊긴 선점은 완료하지 않고 해제 시간을 기다린다. Completing은 끊겨도 그대로 둔다

        /****************************************
        *                Fields
        ****************************************/

        private const string ZONE_ID_PREFIX      = "herb_zone_";
        private const string NODE_ID_PREFIX      = "herb_node_";
        private const string OPERATION_ID_FORMAT = "D";

        private readonly BackendGateway       _gateway;
        private readonly ITownAudience        _audience;
        private readonly IGatherNodeLayout    _layout;
        private readonly GatherSettings       _settings;
        private readonly Func<double>         _clock;
        private readonly Action<int>          _gathered;
        private readonly NodeSlot[]           _nodes;
        private readonly Dictionary<long, int>   _indexByKey;
        private readonly Dictionary<string, int> _claimByAccount;
        private readonly Dictionary<int, int>    _nodeByRequest;

        private int _count = 0;

        /****************************************
        *              Properties
        ****************************************/

        // 상태를 들고 있는 노드 수, 한 번 쓰인 노드는 프로세스 수명 동안 남는다
        public int NodeCount => _count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> gathered는 채집이 확정된 참가자 번호로 불린다(생활 재고 갱신용). gateway가 null이면 채집 시작을 거절한다 </summary>
        public GatherZoneServer(BackendGateway gateway, ITownAudience audience, IGatherNodeLayout layout, in GatherSettings settings,
            Func<double> clock, Action<int> gathered)
        {
            if ((audience == null) || (layout == null))
            {
                throw new ArgumentNullException(nameof(audience), "채집 판정에 필요한 참조가 없다");
            }

            _gateway        = gateway;
            _audience       = audience;
            _layout         = layout;
            _settings       = settings;
            _clock          = clock ?? BackendGateway.MonotonicSeconds;
            _gathered       = gathered;
            _nodes          = new NodeSlot[Constants.Town.MAX_GATHER_NODES];
            _indexByKey     = new Dictionary<long, int>(Constants.Town.MAX_GATHER_NODES);
            _claimByAccount = new Dictionary<string, int>(Constants.Town.MAX_GATHER_NODES, StringComparer.OrdinalIgnoreCase);
            _nodeByRequest  = new Dictionary<int, int>(Constants.Town.MAX_GATHER_NODES);
        }

        public EGatherNodeState StateOf(int zone, int node)
        {
            return (_indexByKey.TryGetValue(TownGatherNodeRegistry.ToKey(zone, node), out int index))
                ? _nodes[index].State
                : EGatherNodeState.Available;
        }

        /// <summary> 노드를 선점한다, 성공하면 None. 계정당 하나, 먼저 온 요청 하나만 이긴다 </summary>
        public ERejectReason Start(TownMember member, int zone, int node)
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

            if (!_layout.TryGetNode(zone, node, out Vector3 nodePosition))
            {
                return ERejectReason.InvalidTarget;
            }

            if (_claimByAccount.ContainsKey(member.AccountId))
            {
                return ERejectReason.WrongPhase;
            }

            int index = GetOrCreate(zone, node, nodePosition);
            if (index < 0)
            {
                return ERejectReason.NoCapacity;
            }

            ref NodeSlot target = ref _nodes[index];
            if (target.State != EGatherNodeState.Available)
            {
                return ERejectReason.Occupied;
            }

            if (!_audience.TryGetPosition(member.ParticipantId, out Vector3 playerPosition))
            {
                return ERejectReason.OutOfRange;
            }

            float range = _settings.InteractRange;
            if ((playerPosition - target.Position).sqrMagnitude > (range * range))
            {
                return ERejectReason.OutOfRange;
            }

            target.State          = EGatherNodeState.Claimed;
            target.AccountId      = member.AccountId;
            target.AccountLeaseId = member.AccountLeaseId;
            target.ParticipantId  = member.ParticipantId;
            target.ClaimedAt      = _clock();
            target.ClaimPosition  = playerPosition;
            target.Disconnected   = false;
            _claimByAccount[member.AccountId] = index;
            Broadcast(index);
            return ERejectReason.None;
        }

        /// <summary> 내 선점을 비용 없이 푼다. 저장을 보낸 뒤(Completing)에는 풀 수 없다 </summary>
        public ERejectReason Cancel(TownMember member)
        {
            if ((member == null) || (string.IsNullOrEmpty(member.AccountId))
                || (!_claimByAccount.TryGetValue(member.AccountId, out int index)))
            {
                return ERejectReason.InvalidTarget;
            }

            if (_nodes[index].State != EGatherNodeState.Claimed)
            {
                return ERejectReason.WrongPhase;
            }

            Release(index);
            return ERejectReason.None;
        }

        /// <summary> 연결이 끊겼다. 선점이면 해제 시간을 재기 시작한다. 저장 중이면 결과를 그대로 기다린다 </summary>
        public void OnMemberDisconnected(TownMember member)
        {
            if ((member == null) || (string.IsNullOrEmpty(member.AccountId))
                || (!_claimByAccount.TryGetValue(member.AccountId, out int index)))
            {
                return;
            }

            ref NodeSlot target = ref _nodes[index];
            if ((target.State != EGatherNodeState.Claimed) || (target.ParticipantId != member.ParticipantId))
            {
                return;
            }

            target.Disconnected   = true;
            target.DisconnectedAt = _clock();
        }

        /// <summary> 같은 계정이 다시 들어왔다. 남은 선점이나 저장 결과를 새 연결에 잇는다 </summary>
        public void OnMemberConnected(TownMember member)
        {
            if ((member == null) || (string.IsNullOrEmpty(member.AccountId))
                || (!_claimByAccount.TryGetValue(member.AccountId, out int index)))
            {
                return;
            }

            ref NodeSlot target = ref _nodes[index];
            target.ParticipantId  = member.ParticipantId;
            target.AccountLeaseId = (target.State == EGatherNodeState.Claimed) ? member.AccountLeaseId : target.AccountLeaseId;
            target.Disconnected   = false;
        }

        /// <summary> 사용 중이거나 재생 대기인 노드 상태를 한 참가자에게 보낸다, 마을에 들어온 직후 요청으로 부른다 </summary>
        public void SendActiveNodes(int participantId)
        {
            double now = _clock();
            for (int i = 0; i < _count; ++i)
            {
                if (_nodes[i].State != EGatherNodeState.Available)
                {
                    _audience.Send(participantId, ToMessage(i, now));
                }
            }
        }

        /// <summary> 매 프레임 부른다. 선점 이동 취소, 시간 초과, 끊김 해제, 완료 저장, 재전송, 재생을 처리한다 </summary>
        public void Tick()
        {
            double now = _clock();
            for (int i = 0; i < _count; ++i)
            {
                switch (_nodes[i].State)
                {
                    case EGatherNodeState.Claimed:
                        TickClaimed(i, now);
                        break;
                    case EGatherNodeState.Completing:
                        if ((_nodes[i].RequestId == 0) && (now >= _nodes[i].NextAttemptAt))
                        {
                            SendGather(i);
                        }
                        break;
                    case EGatherNodeState.Depleted:
                        if (now >= _nodes[i].RegrowAt)
                        {
                            _nodes[i].State = EGatherNodeState.Available;
                            Broadcast(i);
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        // 게이트웨이가 메인 스레드의 Pump에서 부른다
        public void OnBackendResponse(in BackendResponse response)
        {
            if (!_nodeByRequest.TryGetValue(response.RequestId, out int index))
            {
                return;
            }

            _nodeByRequest.Remove(response.RequestId);
            ref NodeSlot target = ref _nodes[index];
            if ((target.State != EGatherNodeState.Completing) || (target.RequestId != response.RequestId))
            {
                return;
            }

            target.RequestId = 0;
            var  result    = response.Payload as OperationResult;
            int  owner     = target.ParticipantId;
            if ((response.IsSucceeded) && (result != null) && (result.Status == ContractCodes.OperationStatus.COMMITTED))
            {
                ClearClaim(ref target);
                target.State    = EGatherNodeState.Depleted;
                target.RegrowAt = _clock() + _settings.RegrowSeconds;
                Broadcast(index);
                _audience.Send(owner, new LifeResultMessage(Guid.Empty, ELifeAction.GatherCompleted, ERejectReason.None, target.Zone,
                    target.NodeIndex));
                _gathered?.Invoke(owner);
                return;
            }

            bool definite = (TownRejectRules.IsDefiniteRejection(in response))
                || ((result != null) && (result.Status == ContractCodes.OperationStatus.REJECTED));
            if (definite)
            {
                // 행동력 부족, 보관 상한 같은 거절은 아무것도 바꾸지 않았다. 비용 없이 노드를 돌려준다
                ERejectReason reason = TownRejectRules.ReasonOf(in response, result, ERejectReason.InvalidTarget);
                int zone = target.Zone;
                int node = target.NodeIndex;
                Release(index);
                _audience.Send(owner, new LifeResultMessage(Guid.Empty, ELifeAction.GatherCompleted, reason, zone, node));
                return;
            }

            target.NextAttemptAt = _clock() + Math.Max(Constants.Town.GATHER_RETRY_SECONDS, response.RetryAfterSeconds);
        }

        private void TickClaimed(int index, double now)
        {
            ref NodeSlot target = ref _nodes[index];
            if (target.Disconnected)
            {
                if (now >= (target.DisconnectedAt + _settings.DisconnectReleaseSeconds))
                {
                    Release(index);
                }

                return;
            }

            if (now >= (target.ClaimedAt + _settings.ClaimTimeoutSeconds))
            {
                Release(index);
                return;
            }

            // 위치를 모르거나 선점 위치에서 움직였으면 취소한다
            float tolerance = Constants.Town.GATHER_MOVE_CANCEL_DISTANCE;
            if ((!_audience.TryGetPosition(target.ParticipantId, out Vector3 position))
                || ((position - target.ClaimPosition).sqrMagnitude > (tolerance * tolerance)))
            {
                int owner = target.ParticipantId;
                int zone  = target.Zone;
                int node  = target.NodeIndex;
                Release(index);
                _audience.Send(owner, new LifeResultMessage(Guid.Empty, ELifeAction.GatherCancel, ERejectReason.OutOfRange, zone, node));
                return;
            }

            if (now < (target.ClaimedAt + _settings.GatherSeconds))
            {
                return;
            }

            target.State         = EGatherNodeState.Completing;
            target.OperationId   = Guid.NewGuid().ToString(OPERATION_ID_FORMAT);
            target.NextAttemptAt = 0.0;
            Broadcast(index);
            SendGather(index);
        }

        private void SendGather(int index)
        {
            ref NodeSlot target = ref _nodes[index];
            var body = new LifeGatherRequest(target.OperationId, target.AccountId, target.AccountLeaseId, target.ZoneId, target.NodeId);
            BackendRequest request = BackendRequest.Post(LifeRoutes.GATHERS, body, typeof(OperationResult), target.OperationId,
                Constants.Network.API_TIMEOUT_SECONDS);
            target.RequestId = _gateway.Send(request, this);
            _nodeByRequest[target.RequestId] = index;
        }

        private void Release(int index)
        {
            ref NodeSlot target = ref _nodes[index];
            ClearClaim(ref target);
            target.State = EGatherNodeState.Available;
            Broadcast(index);
        }

        private void ClearClaim(ref NodeSlot target)
        {
            if (target.AccountId != null)
            {
                _claimByAccount.Remove(target.AccountId);
            }

            target.AccountId      = null;
            target.AccountLeaseId = null;
            target.ParticipantId  = 0;
            target.OperationId    = null;
            target.RequestId      = 0;
            target.Disconnected   = false;
        }

        private void Broadcast(int index)
        {
            _audience.SendToAll(ToMessage(index, _clock()));
        }

        private GatherNodeStateMessage ToMessage(int index, double now)
        {
            ref NodeSlot target = ref _nodes[index];
            float regrow = (target.State == EGatherNodeState.Depleted) ? (float)Math.Max(0.0, target.RegrowAt - now) : 0f;
            return new GatherNodeStateMessage(target.Zone, target.NodeIndex, target.State, target.ParticipantId, regrow);
        }

        // 처음 쓰는 노드면 칸을 만든다. API 출처 문자열도 이때 한 번만 만든다
        private int GetOrCreate(int zone, int node, Vector3 position)
        {
            long key = TownGatherNodeRegistry.ToKey(zone, node);
            if (_indexByKey.TryGetValue(key, out int index))
            {
                return index;
            }

            if (_count == _nodes.Length)
            {
                return -1;
            }

            index = _count;
            _nodes[index] = new NodeSlot(zone, node, position,
                ZONE_ID_PREFIX + zone.ToString(CultureInfo.InvariantCulture),
                NODE_ID_PREFIX + node.ToString(CultureInfo.InvariantCulture));
            _indexByKey.Add(key, index);
            ++_count;
            return index;
        }

        /****************************************
        *               Records
        ****************************************/

        // 배열 안에서 ref로 제자리 수정하는 값 묶음이라 클래스 밖으로 내보내지 않는다
        private struct NodeSlot
        {
            public int              Zone;
            public int              NodeIndex;
            public Vector3          Position;
            public string           ZoneId;
            public string           NodeId;
            public EGatherNodeState State;
            public string           AccountId;
            public string           AccountLeaseId;
            public int              ParticipantId;
            public double           ClaimedAt;
            public Vector3          ClaimPosition;
            public bool             Disconnected;
            public double           DisconnectedAt;
            public string           OperationId;
            public int              RequestId;
            public double           NextAttemptAt;
            public double           RegrowAt;

            public NodeSlot(int zone, int node, Vector3 position, string zoneId, string nodeId)
            {
                Zone           = zone;
                NodeIndex      = node;
                Position       = position;
                ZoneId         = zoneId;
                NodeId         = nodeId;
                State          = EGatherNodeState.Available;
                AccountId      = null;
                AccountLeaseId = null;
                ParticipantId  = 0;
                ClaimedAt      = 0.0;
                ClaimPosition  = default;
                Disconnected   = false;
                DisconnectedAt = 0.0;
                OperationId    = null;
                RequestId      = 0;
                NextAttemptAt  = 0.0;
                RegrowAt       = 0.0;
            }
        }
    }
}
