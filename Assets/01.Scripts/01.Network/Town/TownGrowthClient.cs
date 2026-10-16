// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 성장 화면(UI 담당)은 이 창구의 이벤트만 구독하고 요청 메서드로 보낸다. 판정, 확률, 비용 차감은 마을 서버와 API가 한다.
    // 요청 메서드는 새 operationId를 만들어 보내고 돌려준다. 결과를 받지 못하면 RetryLast로 같은 요청을 다시 보낸다.
    // 연결되지 않았거나 결과가 정해지지 않은 다른 쓰기가 남아 있으면 Guid.Empty를 돌려주고 아무것도 보내지 않는다.
    // 남은 쓰기는 HasUnresolvedWrite와 UnresolvedAction으로 보이고, 그 요청을 보낸 창만 RetryLast나 ReleaseWrite를 부른다
    /// <summary> 클라이언트의 마을 성장 요청 송신과 결과 수신 창구 </summary>
    public static class TownGrowthClient
    {
        /****************************************
        *                Fields
        ****************************************/

        private static GrowthSnapshotMessage _snapshot;
        private static bool                  _hasSnapshot;
        private static GrowthRequestMessage  _lastRequest;
        private static bool                  _hasLastRequest;

        private static readonly GrowthWriteLock _writeLock = new GrowthWriteLock();

        /****************************************
        *              Properties
        ****************************************/

        public static GrowthSnapshotMessage Snapshot    => _snapshot;
        public static bool                  HasSnapshot => _hasSnapshot;

        // 모든 성장창이 이 값을 보고 쓰기 버튼을 잠근다
        public static bool          HasUnresolvedWrite         => _writeLock.IsHeld;
        public static EGrowthAction UnresolvedAction           => _writeLock.Action;
        public static Guid          UnresolvedOperationId      => _writeLock.OperationId;
        public static bool          IsUnresolvedOutcomeUnknown => _writeLock.IsOutcomeUnknown;

        public static event Action<GrowthResultMessage>   ResultReceived;
        public static event Action<GrowthSnapshotMessage> SnapshotChanged;

        // 공용 쓰기 잠금이 걸리거나 풀리거나 결과 불명이 됐을 때
        public static event Action                        WriteLockChanged;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 클라이언트가 시작할 때 한 번 부른다, 이전 연결의 상태를 비운다 </summary>
        public static void RegisterHandlers()
        {
            _snapshot       = default;
            _hasSnapshot    = false;
            _lastRequest    = default;
            _hasLastRequest = false;
            _writeLock.Clear();
            NetworkClient.ReplaceHandler<GrowthResultMessage>(OnResult);
            NetworkClient.ReplaceHandler<GrowthSnapshotMessage>(OnSnapshot);
        }

        public static Guid RequestSnapshot()
        {
            return Send(EGrowthAction.Snapshot, null, null, 0, 0, false, null, null);
        }

        public static Guid RegisterTalisman(string effectId)
        {
            return Send(EGrowthAction.TalismanRegister, effectId, null, 0, 0, false, null, null);
        }

        public static Guid UpgradeTalisman(string effectId)
        {
            return Send(EGrowthAction.TalismanUpgrade, effectId, null, 0, 0, false, null, null);
        }

        public static Guid ReplaceTalismanLoadout(TownLoadoutEntry[] entries)
        {
            return Send(EGrowthAction.TalismanLoadout, null, null, 0, 0, false, null, entries);
        }

        public static Guid DisassembleTalisman(string effectId, int quantity)
        {
            return Send(EGrowthAction.TalismanDisassemble, effectId, null, quantity, 0, false, null, null);
        }

        /// <summary> stat은 ETrainingStat 값 </summary>
        public static Guid Train(int stat, int steps)
        {
            return Send(EGrowthAction.Training, null, null, stat, steps, false, null, null);
        }

        /// <summary> allocation은 ETrainingStat 순서의 새 투자 단계 </summary>
        public static Guid RespecTraining(int[] allocation)
        {
            return Send(EGrowthAction.TrainingRespec, null, null, 0, 0, false, allocation, null);
        }

        public static Guid CraftEquipment(string definitionId)
        {
            return Send(EGrowthAction.EquipmentCraft, definitionId, null, 0, 0, false, null, null);
        }

        public static Guid EnhanceEquipment(string itemId)
        {
            return Send(EGrowthAction.EquipmentEnhance, itemId, null, 0, 0, false, null, null);
        }

        /// <summary> 장비 옵션 두 줄을 새로 뽑아 바꾼다, 난수는 서버가 넣는다. 결과를 모르면 RetryLast로 같은 요청을 확인한다 </summary>
        public static Guid RerollEquipmentOptions(string itemId)
        {
            return Send(EGrowthAction.EquipmentRerollOptions, itemId, null, 0, 0, false, null, null);
        }

        public static Guid InheritEquipment(string sourceItemId, string targetItemId)
        {
            return Send(EGrowthAction.EquipmentInherit, sourceItemId, targetItemId, 0, 0, false, null, null);
        }

        public static Guid EquipEquipment(string itemId)
        {
            return Send(EGrowthAction.EquipmentEquip, itemId, null, 0, 0, false, null, null);
        }

        public static Guid UnequipEquipment(string itemId)
        {
            return Send(EGrowthAction.EquipmentUnequip, itemId, null, 0, 0, false, null, null);
        }

        public static Guid LockEquipment(string itemId, bool locked)
        {
            return Send(EGrowthAction.EquipmentLock, itemId, null, 0, 0, locked, null, null);
        }

        public static Guid DismantleEquipment(string itemId)
        {
            return Send(EGrowthAction.EquipmentDismantle, itemId, null, 0, 0, false, null, null);
        }

        /// <summary> 결과가 정해지지 않은 쓰기 요청을 같은 operationId로 다시 보낸다, 서버와 API가 처음 결과를 돌려준다 </summary>
        public static bool RetryLast()
        {
            if ((!NetworkClient.isConnected) || (!_hasLastRequest) || (!_writeLock.IsHeld))
            {
                return false;
            }

            if (_writeLock.MarkRetrying(_lastRequest.OperationId))
            {
                WriteLockChanged?.Invoke();
            }

            NetworkClient.Send(_lastRequest);
            return true;
        }

        // 시간 초과 뒤 서버 상태를 다시 읽고 다음 시도를 새 요청으로 보내기로 한 창이 자기 요청에만 부른다
        /// <summary> 자기 쓰기 요청의 잠금을 푼다, 다른 요청이면 아무것도 하지 않고 거짓 </summary>
        public static bool ReleaseWrite(Guid operationId)
        {
            if (!_writeLock.Release(operationId))
            {
                return false;
            }

            _hasLastRequest = false;
            WriteLockChanged?.Invoke();
            return true;
        }

        private static Guid Send(EGrowthAction action, string targetId, string secondaryId, int intArg, int intArg2, bool flag,
            int[] allocation, TownLoadoutEntry[] loadout)
        {
            if (!NetworkClient.isConnected)
            {
                return Guid.Empty;
            }

            bool isWrite = GrowthWriteLock.IsWrite(action);
            if ((isWrite) && (_writeLock.IsHeld))
            {
                return Guid.Empty;
            }

            Guid operationId = Guid.NewGuid();
            var  request     = new GrowthRequestMessage(operationId, action, targetId, secondaryId, intArg, intArg2, flag,
                allocation, loadout);

            // 조회는 멱등 확인이 필요 없다. 기억하면 다른 창의 조회가 응답 불명인 쓰기 요청의 RetryLast를 가로챈다
            if (isWrite)
            {
                _lastRequest    = request;
                _hasLastRequest = true;
                _writeLock.TryBegin(operationId, action);
            }

            NetworkClient.Send(request);
            if (isWrite)
            {
                WriteLockChanged?.Invoke();
            }

            return operationId;
        }

        // 잠금을 먼저 풀어야 결과를 받은 창이 같은 처리 안에서 다음 쓰기(수련 묶음의 다음 항목)를 보낼 수 있다
        private static void OnResult(GrowthResultMessage message)
        {
            bool changed = _writeLock.Resolve(message.OperationId, message.Reason);
            if ((changed) && (!_writeLock.IsHeld))
            {
                _hasLastRequest = false;
            }

            if (changed)
            {
                WriteLockChanged?.Invoke();
            }

            ResultReceived?.Invoke(message);
        }

        private static void OnSnapshot(GrowthSnapshotMessage message)
        {
            _snapshot    = message;
            _hasSnapshot = true;
            SnapshotChanged?.Invoke(message);
        }
    }
}
