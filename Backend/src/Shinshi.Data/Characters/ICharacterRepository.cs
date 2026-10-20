// System
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts;

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

        /// <summary> 인증된 계정이 소유한 살아 있는 캐릭터만 ID 순서로 반환한다 </summary>
        Task<IReadOnlyList<CharacterRecord>> ListAsync(Guid accountId, CancellationToken cancellationToken = default);

        // 계정 actor의 멱등 작업이다. 없거나 남의, 이미 삭제한 캐릭터면 CHARACTER_NOT_FOUND, 접속 중이면 ALREADY_CONNECTED,
        // 열린 출정 참가자면 RESERVATION_CONFLICT, revision이 다르면 STALE_REVISION이다.
        // 삭제 예약이 없으면 CHARACTER_DELETE_NOT_REQUESTED, 예약 뒤 유예 시간 전이면 CHARACTER_DELETE_PENDING이다
        Task<OperationResult> DeleteAsync(Guid accountId, Guid operationId, Guid characterId, long expectedRevision,
            CancellationToken cancellationToken = default);

        /// <summary> 삭제를 예약한다. 없거나 남의 캐릭터면 CHARACTER_NOT_FOUND, revision이 다르면 STALE_REVISION이다.
        /// 이미 예약했으면 시각을 바꾸지 않고 확정으로 돌려준다 </summary>
        Task<OperationResult> RequestDeleteAsync(Guid accountId, Guid operationId, Guid characterId, long expectedRevision,
            CancellationToken cancellationToken = default);

        /// <summary> 삭제 예약을 취소한다. 예약이 없으면 CHARACTER_DELETE_NOT_REQUESTED다 </summary>
        Task<OperationResult> CancelDeleteAsync(Guid accountId, Guid operationId, Guid characterId, long expectedRevision,
            CancellationToken cancellationToken = default);
    }
}
