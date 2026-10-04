// System
using System;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Life
{
    /// <summary> 계정 하나를 가리키는 생활 요청의 공통 부분. 호출 마을 서버는 인증된 자격에서 정한다 </summary>
    public abstract class LifeAccountInput
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid TownInstanceId { get; set; }
        public Guid OperationId    { get; set; }
        public Guid AccountId      { get; set; }
        public Guid AccountLeaseId { get; set; }
    }

    /// <summary> 채집 완료 한 번 </summary>
    public sealed class LifeGatherInput : LifeAccountInput
    {
        /****************************************
        *              Properties
        ****************************************/

        public string ZoneId { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
    }

    /// <summary> 물약 제작 </summary>
    public sealed class LifePotionCraftInput : LifeAccountInput
    {
        /****************************************
        *              Properties
        ****************************************/

        public string PotionDefinitionId { get; set; } = string.Empty;
        public int    Count              { get; set; }
    }

    /// <summary> 생활 재고 조회 결과. 거절이면 Response가 null이고 ReasonCode가 있다 </summary>
    public sealed class LifeSnapshotOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public int                   ReasonCode { get; }
        public LifeSnapshotResponse? Response   { get; }
        public DateTimeOffset        ServerTime { get; }

        /****************************************
        *                Methods
        ****************************************/

        private LifeSnapshotOutcome(int reasonCode, LifeSnapshotResponse? response, DateTimeOffset serverTime)
        {
            ReasonCode = reasonCode;
            Response   = response;
            ServerTime = serverTime;
        }

        public static LifeSnapshotOutcome Found(LifeSnapshotResponse response, DateTimeOffset serverTime)
        {
            ArgumentNullException.ThrowIfNull(response);
            return new LifeSnapshotOutcome(ContractCodes.RejectReason.NONE, response, serverTime);
        }

        public static LifeSnapshotOutcome Rejected(int reasonCode, DateTimeOffset serverTime)
        {
            return new LifeSnapshotOutcome(reasonCode, null, serverTime);
        }
    }

    /// <summary> 원장 출처로 쓰는 구역, 노드 ID 형식 </summary>
    public static class LifeIds
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int MAX_ID_LENGTH = 64;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 영문, 숫자, '_', '-', '.'만 쓰는 64자 이하. ':'는 출처 구분자라 받지 않는다 </summary>
        public static bool IsValid(string? value)
        {
            if (string.IsNullOrEmpty(value) || (value.Length > MAX_ID_LENGTH))
            {
                return false;
            }

            foreach (char character in value)
            {
                bool allowed = ((character >= 'a') && (character <= 'z')) || ((character >= 'A') && (character <= 'Z'))
                    || ((character >= '0') && (character <= '9')) || (character == '_') || (character == '-')
                    || (character == '.');
                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
