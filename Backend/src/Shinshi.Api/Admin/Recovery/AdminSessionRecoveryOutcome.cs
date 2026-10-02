// System
using System;

// Project
using Shinshi.Contracts;

namespace Shinshi.Backend.Admin
{
    /// <summary> 수동 복구 결과. Result는 어드민 작업 결과이고 확정이면 ReceiptId가 실제 정산/취소 작업의 operationId다.
    /// RecoveryActorId는 그 작업의 actor(정산 재시도, 장애 취소는 세션 배정 서버, 강제 종료는 운영자 자신)이며
    /// 세션이 없으면 null이다 </summary>
    public sealed class AdminSessionRecoveryOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public OperationResult Result          { get; }
        public Guid?           RecoveryActorId { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminSessionRecoveryOutcome(OperationResult result, Guid? recoveryActorId)
        {
            ArgumentNullException.ThrowIfNull(result);
            Result          = result;
            RecoveryActorId = recoveryActorId;
        }
    }
}
