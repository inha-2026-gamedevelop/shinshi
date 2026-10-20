// System
using System;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Characters;

namespace Shinshi.Backend.Tests.Accounts
{
    /// <summary> 삭제 예약 유예(10분)를 시계 대기 없이 지나게 하는 테스트 도우미 </summary>
    internal static class CharacterDeleteTestSupport
    {
        /****************************************
        *                Fields
        ****************************************/

        // 유예보다 길게 되돌린다
        private const int BACKDATE_MINUTES = PostgresCharacterRepository.DELETE_GRACE_MINUTES + 1;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 삭제 예약 시각을 유예보다 과거로 당긴다 </summary>
        public static async Task BackdateAsync(NpgsqlDataSource dataSource, Guid characterId)
        {
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                UPDATE characters SET delete_requested_at = delete_requested_at - make_interval(mins => @Minutes)
                WHERE character_id = @CharacterId AND delete_requested_at IS NOT NULL;
                """, new { CharacterId = characterId, Minutes = BACKDATE_MINUTES });
        }

        /// <summary> 예약하고 유예를 지나게 한 뒤 최종 삭제한다. 예약이 거절되면 그 결과를 돌려준다 </summary>
        public static async Task<OperationResult> DeleteAfterGraceAsync(NpgsqlDataSource dataSource,
            ICharacterRepository repository, Guid accountId, Guid characterId, long expectedRevision)
        {
            OperationResult requested = await repository.RequestDeleteAsync(accountId, Guid.NewGuid(), characterId,
                expectedRevision);
            if (requested.Status != ContractCodes.OperationStatus.COMMITTED)
            {
                return requested;
            }

            await BackdateAsync(dataSource, characterId);
            return await repository.DeleteAsync(accountId, Guid.NewGuid(), characterId, requested.CommittedRevision);
        }
    }
}
