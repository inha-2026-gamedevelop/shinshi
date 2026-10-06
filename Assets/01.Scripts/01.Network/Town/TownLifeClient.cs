// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 생활 화면(UI 담당)은 이 창구의 이벤트만 구독하고 요청 메서드로 보낸다. 거리, 선점, 시간 판정은 마을 서버가
    // 서버 캐릭터 위치로 하고 행동력, 재고는 API가 확정한다. 연결되지 않았으면 거짓이나 Guid.Empty를 돌려준다
    /// <summary> 클라이언트의 마을 생활 요청 송신과 결과 수신 창구 </summary>
    public static class TownLifeClient
    {
        /****************************************
        *                Fields
        ****************************************/

        private static LifeSnapshotMessage _snapshot;
        private static bool                _hasSnapshot;

        /****************************************
        *              Properties
        ****************************************/

        public static LifeSnapshotMessage Snapshot    => _snapshot;
        public static bool                HasSnapshot => _hasSnapshot;

        public static event Action<LifeResultMessage>      ResultReceived;
        public static event Action<LifeSnapshotMessage>    SnapshotChanged;
        public static event Action<GatherNodeStateMessage> NodeChanged;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 클라이언트가 시작할 때 한 번 부른다, 이전 연결의 상태를 비운다 </summary>
        public static void RegisterHandlers()
        {
            _snapshot    = default;
            _hasSnapshot = false;
            NetworkClient.ReplaceHandler<LifeResultMessage>(OnResult);
            NetworkClient.ReplaceHandler<LifeSnapshotMessage>(OnSnapshot);
            NetworkClient.ReplaceHandler<GatherNodeStateMessage>(OnNode);
        }

        public static bool RequestSnapshot()
        {
            return Send(new LifeRequestMessage(Guid.Empty, ELifeAction.Snapshot, 0, 0, null, 0));
        }

        /// <summary> 사용 중이거나 재생 대기인 노드 상태를 받는다, 마을에 들어온 직후 한 번 부른다 </summary>
        public static bool RequestGatherNodes()
        {
            return Send(new LifeRequestMessage(Guid.Empty, ELifeAction.GatherNodes, 0, 0, null, 0));
        }

        public static bool StartGather(int zone, int node)
        {
            return Send(new LifeRequestMessage(Guid.Empty, ELifeAction.GatherStart, zone, node, null, 0));
        }

        public static bool CancelGather()
        {
            return Send(new LifeRequestMessage(Guid.Empty, ELifeAction.GatherCancel, 0, 0, null, 0));
        }

        /// <summary> 새 operationId를 만들어 보낸다. 결과를 받지 못해 다시 보낼 때는 RetryPotionCraft에 그 값을 넣는다 </summary>
        public static Guid CraftPotion(string potionDefinitionId, int count)
        {
            Guid operationId = Guid.NewGuid();
            return Send(new LifeRequestMessage(operationId, ELifeAction.PotionCraft, 0, 0, potionDefinitionId, count))
                ? operationId
                : Guid.Empty;
        }

        public static bool RetryPotionCraft(Guid operationId, string potionDefinitionId, int count)
        {
            return Send(new LifeRequestMessage(operationId, ELifeAction.PotionCraft, 0, 0, potionDefinitionId, count));
        }

        private static bool Send(LifeRequestMessage message)
        {
            if (!NetworkClient.isConnected)
            {
                return false;
            }

            NetworkClient.Send(message);
            return true;
        }

        private static void OnResult(LifeResultMessage message)
        {
            ResultReceived?.Invoke(message);
        }

        private static void OnSnapshot(LifeSnapshotMessage message)
        {
            _snapshot    = message;
            _hasSnapshot = true;
            SnapshotChanged?.Invoke(message);
        }

        private static void OnNode(GatherNodeStateMessage message)
        {
            NodeChanged?.Invoke(message);
        }
    }
}
