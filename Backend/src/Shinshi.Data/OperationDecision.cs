// System
using System;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data
{
    /// <summary> 효과 처리의 확정 결과. Pending을 최종 결과로 저장할 수 없다 </summary>
    public sealed class OperationDecision
    {
        /****************************************
        *              Properties
        ****************************************/

        public int Status { get; }
        public int ReasonCode { get; }
        public long CommittedRevision { get; }
        public string ReceiptId { get; }

        /****************************************
        *                Methods
        ****************************************/

        private OperationDecision(int status, int reasonCode, long committedRevision, string receiptId)
        {
            if (committedRevision < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(committedRevision));
            }

            Status            = status;
            ReasonCode        = reasonCode;
            CommittedRevision = committedRevision;
            ReceiptId         = receiptId;
        }

        /// <summary> 커밋할 효과와 같은 트랜잭션에 저장할 영수증 ID를 지정한다 </summary>
        public static OperationDecision Committed(long revision, string receiptId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(receiptId);
            return new OperationDecision(ContractCodes.OperationStatus.COMMITTED,
                ContractCodes.RejectReason.NONE, revision, receiptId);
        }

        /// <summary> 거절된 효과는 롤백하고 이 결과만 저장한다. 거절 사유는 필수다 </summary>
        public static OperationDecision Rejected(int reasonCode, long revision = 0)
        {
            if (reasonCode <= ContractCodes.RejectReason.NONE)
            {
                throw new ArgumentOutOfRangeException(nameof(reasonCode));
            }

            return new OperationDecision(ContractCodes.OperationStatus.REJECTED, reasonCode, revision, string.Empty);
        }
    }
}

