// System
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Data.Characters
{
    /// <summary> 캐릭터 생성과 이름 조회. 이름은 CharacterNameRules로 정규화한 값과 비교 키만 받는다 </summary>
    public interface ICharacterRepository
    {
        // 계정 actor의 멱등 작업이다. 같은 operationId와 본문은 처음 결과, 다른 본문은 IDEMPOTENCY_CONFLICT다.
        // 이름 중복은 비교 키 유일 제약으로 확정하며 CHARACTER_NAME_TAKEN으로 거절한다
        Task<CharacterCreateOutcome> CreateAsync(Guid accountId, Guid operationId, string name, string nameKey,
            int classId, string appearanceJson, string requestJson, CancellationToken cancellationToken = default);

        Task<bool> IsNameTakenAsync(string nameKey, CancellationToken cancellationToken = default);
    }
}
