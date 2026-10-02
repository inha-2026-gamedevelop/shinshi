// System
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Data.Ledger
{
    /// <summary> 호출자의 동일 operation 트랜잭션에 검증된 원장 한 행을 추가한다 </summary>
    public interface IEconomyLedgerEntryWriter
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 새 행이면 true, 같은 원장 ID와 내용의 재전달이면 false, 충돌이면 예외로 알린다 </summary>
        Task<bool> AppendAsync(OperationExecutionContext context, EconomyLedgerEntry entry,
            CancellationToken cancellationToken = default);
    }
}
