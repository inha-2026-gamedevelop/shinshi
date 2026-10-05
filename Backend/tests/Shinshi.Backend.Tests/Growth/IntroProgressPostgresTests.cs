// System
using System;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Growth;

namespace Shinshi.Backend.Tests.Growth
{
    /// <summary> 마을 서버가 중계한 신시 안내 완료 기록을 실제 PostgreSQL에서 검사한다. 멱등 actor는 계정이고 임대가 호출 마을에 있어야 한다 </summary>
    public sealed class IntroProgressPostgresTests
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 처음 기록은 진행 revision을 올리고, 같은 operationId 재전송은 다른 마을에서 와도 처음 결과다.
        /// 이미 완료한 계정의 새 기록도 확정이며 revision은 그대로다 </summary>
        [PostgresFact]
        public async Task CompleteIntro_LeasedTown_RecordsOnceAndReplaysAcrossTowns()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            PostgresAccountProgressRepository progress = Create(environment);
            GrowthPlayer player = await environment.CreatePlayerAsync();
            Guid operationId = Guid.NewGuid();

            OperationResult first = await progress.CompleteIntroAsync(environment.TownId, player.AccountId, operationId);
            OperationResult replay = await progress.CompleteIntroAsync(Guid.NewGuid(), player.AccountId, operationId);
            OperationResult again = await progress.CompleteIntroAsync(environment.TownId, player.AccountId, Guid.NewGuid());

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, first.Status);
            Assert.Equal(1L, first.CommittedRevision);
            Assert.Equal(player.AccountId.ToString("D"), first.ReceiptId);
            Assert.Equal(first.ServerTimeUnixMs, replay.ServerTimeUnixMs);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, again.Status);
            Assert.Equal(1L, again.CommittedRevision);
            Assert.True((await progress.ReadAsync(player.AccountId)).IntroCompleted);
        }

        /// <summary> 다른 마을의 호출은 SESSION_MISMATCH, 없는 계정은 INVALID_TARGET이고 아무것도 기록하지 않는다 </summary>
        [PostgresFact]
        public async Task CompleteIntro_OtherTownOrUnknownAccount_RejectsWithoutRecord()
        {
            await using GrowthTestEnvironment environment = await GrowthTestEnvironment.CreateAsync();
            PostgresAccountProgressRepository progress = Create(environment);
            GrowthPlayer player = await environment.CreatePlayerAsync();

            OperationResult otherTown = await progress.CompleteIntroAsync(Guid.NewGuid(), player.AccountId, Guid.NewGuid());
            OperationResult unknown = await progress.CompleteIntroAsync(environment.TownId, Guid.NewGuid(), Guid.NewGuid());

            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, otherTown.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.INVALID_TARGET, unknown.ReasonCode);
            Assert.False((await progress.ReadAsync(player.AccountId)).IntroCompleted);
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM account_progress;"));
        }

        private static PostgresAccountProgressRepository Create(GrowthTestEnvironment environment)
        {
            return new PostgresAccountProgressRepository(environment.DataSource, environment.Content,
                EGrowthContentProfile.Trial, environment.Executor);
        }
    }
}
