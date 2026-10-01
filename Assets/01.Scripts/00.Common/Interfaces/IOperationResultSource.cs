// System
using System;

namespace Shinshi.Common
{
    /// <summary> 로컬 플레이어가 operationId로 보낸 요청의 결과 영수증을 UI에 알린다 </summary>
    public interface IOperationResultSource
    {
        // 상태가 바뀔 때만 알린다. 전달은 최소 한 번이라 같은 결과가 다시 와도 구현이 걸러 한 번만 부른다.
        // Committed나 Rejected를 알린 operationId는 다시 Pending으로 알리지 않는다.
        // 서버가 시작한 작업(드롭, 정산)의 영수증도 올 수 있으므로 UI는 자기가 보낸 operationId만 골라 쓴다.
        //
        // 해제 책임은 구독한 쪽에 있다. OnEnable에서 메서드 그룹으로 += 하고 OnDisable에서 -= 한다.
        // 꺼져 있는 동안 놓친 결과는 다시 켜질 때 TryGetLatest로 채운다
        event Action<OperationReceipt> ReceiptUpdated;

        // 아직 최종 결과가 없는 요청 수, 0보다 크면 저장 대기를 표시한다
        int PendingCount { get; }

        // 이번 접속에서 이 창구가 본 마지막 영수증. 모르는 ID면 false이며, 그때 성공으로 가정하지 않는다
        bool TryGetLatest(string operationId, out OperationReceipt receipt);
    }
}
