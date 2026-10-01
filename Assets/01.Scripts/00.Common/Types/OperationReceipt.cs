// System
using System;

namespace Shinshi.Common
{
    /// <summary> operationId로 보낸 요청의 서버 결과 영수증, Pending은 수락만 했을 뿐 성공이 아니다 </summary>
    public readonly struct OperationReceipt
    {
        /****************************************
        *              Constants
        ****************************************/

        // 확정되지 않았거나 거절되어 확정 revision이 없을 때의 값
        public const long NO_REVISION = -1;

        /****************************************
        *                Fields
        ****************************************/

        private readonly string           _operationId;
        private readonly EOperationStatus _status;
        private readonly ERejectReason    _rejectReason;
        private readonly long             _committedRevision;
        private readonly long             _serverTimeUnixMs;
        private readonly string           _receiptId;

        /****************************************
        *              Properties
        ****************************************/

        public string           OperationId       => _operationId;
        public EOperationStatus Status            => _status;
        public ERejectReason    RejectReason      => _rejectReason;
        public long             CommittedRevision => _committedRevision;
        public string           ReceiptId         => _receiptId;

        // 서버가 결과를 정한 UTC 시각(Unix 밀리초). 표시와 정렬에만 쓰고 클라이언트 시각과 비교해 만료를 판정하지 않는다
        public long ServerTimeUnixMs => _serverTimeUnixMs;

        // default 값은 Status가 Pending과 같으므로 ID 유무로 빈 영수증을 먼저 거른다
        public bool IsEmpty     => string.IsNullOrEmpty(_operationId);
        public bool IsPending   => (!IsEmpty) && (_status == EOperationStatus.Pending);
        public bool IsCommitted => (!IsEmpty) && (_status == EOperationStatus.Committed);
        public bool IsRejected  => (!IsEmpty) && (_status == EOperationStatus.Rejected);

        /// <summary> Committed 또는 Rejected, 이 상태가 되기 전에는 같은 operationId로만 재시도한다 </summary>
        public bool IsFinal => (IsCommitted) || (IsRejected);

        /****************************************
        *                Methods
        ****************************************/

        private OperationReceipt(string operationId, EOperationStatus status, ERejectReason rejectReason,
            long committedRevision, long serverTimeUnixMs, string receiptId)
        {
            _operationId       = operationId;
            _status            = status;
            _rejectReason      = rejectReason;
            _committedRevision = committedRevision;
            _serverTimeUnixMs  = serverTimeUnixMs;
            _receiptId         = receiptId;
        }

        /// <summary> 서버가 받았지만 아직 영속 확정 전인 결과, 화면은 저장 대기로 표시한다 </summary>
        public static OperationReceipt Pending(string operationId, long serverTimeUnixMs)
        {
            ThrowIfMissingOperationId(operationId);

            return new OperationReceipt(operationId, EOperationStatus.Pending, ERejectReason.None,
                NO_REVISION, serverTimeUnixMs, string.Empty);
        }

        /// <summary> 영속 확정된 결과, 새 revision과 영수증 ID를 함께 받는다 </summary>
        public static OperationReceipt Committed(string operationId, long committedRevision, long serverTimeUnixMs,
            string receiptId)
        {
            ThrowIfMissingOperationId(operationId);

            if (committedRevision <= NO_REVISION)
            {
                throw new ArgumentOutOfRangeException(nameof(committedRevision), "확정 결과에는 revision이 있어야 한다");
            }

            if (string.IsNullOrWhiteSpace(receiptId))
            {
                throw new ArgumentException("확정 결과에는 영수증 ID가 있어야 한다", nameof(receiptId));
            }

            return new OperationReceipt(operationId, EOperationStatus.Committed, ERejectReason.None,
                committedRevision, serverTimeUnixMs, receiptId);
        }

        /// <summary> 효과 없이 거절된 결과, 사유 코드가 반드시 있다 </summary>
        public static OperationReceipt Rejected(string operationId, ERejectReason reason, long serverTimeUnixMs)
        {
            ThrowIfMissingOperationId(operationId);

            if (reason == ERejectReason.None)
            {
                throw new ArgumentException("거절 결과에는 사유 코드가 있어야 한다", nameof(reason));
            }

            return new OperationReceipt(operationId, EOperationStatus.Rejected, reason,
                NO_REVISION, serverTimeUnixMs, string.Empty);
        }

        public bool Matches(string operationId)
        {
            return (!IsEmpty) && (string.Equals(_operationId, operationId, StringComparison.Ordinal));
        }

        private static void ThrowIfMissingOperationId(string operationId)
        {
            if (string.IsNullOrWhiteSpace(operationId))
            {
                throw new ArgumentException("operationId가 비어 있다", nameof(operationId));
            }
        }
    }
}
