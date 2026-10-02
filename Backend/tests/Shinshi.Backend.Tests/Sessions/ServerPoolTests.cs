// System
using System;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Sessions
{
    /// <summary> 서버 풀 heartbeat 만료, 재등록, 드레인이 유휴 던전 배정에 반영되는지 검사한다 </summary>
    public sealed class ServerPoolTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long TICKETS          = 2;
        private const long POTIONS          = 3;
        private const long POTIONS_RESERVED = 1;
        private const int  TEST_PORT        = 7780;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 15초를 넘긴 heartbeat 공백 동안은 배정에서 빠지지만, 같은 bootId의 늦은 heartbeat나 등록 재전송은 되살린다.
        /// 다른 bootId heartbeat는 거절하고, 새 bootId 등록은 이전 프로세스가 살아 있으면 일시 거절, 끊긴 뒤에는 대체한다 </summary>
        [PostgresFact]
        public async Task Heartbeat_AfterTimeout_SameBootRevivesAndNewBootReplaces()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(0);
            Guid dungeonId = Guid.NewGuid();
            Guid firstBoot = Guid.NewGuid();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            Assert.Equal(EServerPoolStatus.Accepted, (await RegisterAsync(environment, dungeonId, firstBoot,
                ContractCodes.ServerRole.DUNGEON)).Status);

            ServerPoolOutcome beat = await environment.Pool.HeartbeatAsync(dungeonId, firstBoot);
            ServerPoolOutcome wrongBoot = await environment.Pool.HeartbeatAsync(dungeonId, Guid.NewGuid());
            await environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '16 seconds' WHERE instance_id = @Id;",
                new { Id = dungeonId });
            ReservationOutcome noCapacity = await environment.Reservations.ReserveAsync(environment.CreateReservation(
                Guid.NewGuid(), GameSessionCodes.Mode.BOSS, new[] { player }, POTIONS_RESERVED));
            ServerPoolOutcome lateRegister = await environment.Pool.RegisterAsync(new ServerRegistration(dungeonId,
                firstBoot, ContractCodes.ServerRole.DUNGEON, "127.0.0.1", TEST_PORT, "test-build",
                SessionTestEnvironment.CONTENT_VERSION));
            Guid secondBoot = Guid.NewGuid();
            ServerPoolOutcome whileAlive = await RegisterAsync(environment, dungeonId, secondBoot,
                ContractCodes.ServerRole.DUNGEON);
            await environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '16 seconds' WHERE instance_id = @Id;",
                new { Id = dungeonId });
            ServerPoolOutcome rebooted = await RegisterAsync(environment, dungeonId, secondBoot,
                ContractCodes.ServerRole.DUNGEON);
            ServerPoolOutcome oldBoot = await environment.Pool.HeartbeatAsync(dungeonId, firstBoot);
            ReservationOutcome reserved = await environment.Reservations.ReserveAsync(environment.CreateReservation(
                Guid.NewGuid(), GameSessionCodes.Mode.BOSS, new[] { player }, POTIONS_RESERVED));

            Assert.Equal(EServerPoolStatus.Accepted, beat.Status);
            Assert.Equal(EServerPoolStatus.BootMismatch, wrongBoot.Status);
            Assert.Equal(ContractCodes.RejectReason.NO_CAPACITY, noCapacity.Result.ReasonCode);
            Assert.Equal(EServerPoolStatus.Accepted, lateRegister.Status);
            Assert.Equal(EServerPoolStatus.OtherBootAlive, whileAlive.Status);
            Assert.Equal(EServerPoolStatus.Accepted, rebooted.Status);
            Assert.Equal(GameSessionStates.INSTANCE_IDLE, rebooted.State);
            Assert.Equal(EServerPoolStatus.BootMismatch, oldBoot.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, reserved.Result.Status);
        }

        /// <summary> 드레인한 던전은 새 출정에 배정하지 않는다 </summary>
        [PostgresFact]
        public async Task Drain_IdleDungeon_StopsAllocation()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(0);
            Guid dungeonId = Guid.NewGuid();
            Guid boot = Guid.NewGuid();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            await RegisterAsync(environment, dungeonId, boot, ContractCodes.ServerRole.DUNGEON);

            ServerPoolOutcome drained = await environment.Pool.DrainAsync(dungeonId, boot);
            ReservationOutcome outcome = await environment.Reservations.ReserveAsync(environment.CreateReservation(
                Guid.NewGuid(), GameSessionCodes.Mode.BOSS, new[] { player }, POTIONS_RESERVED));

            Assert.Equal(GameSessionStates.INSTANCE_DRAINING, drained.State);
            Assert.Equal(ContractCodes.RejectReason.NO_CAPACITY, outcome.Result.ReasonCode);
            Assert.Equal(0L, (await environment.ReadWalletAsync(player.AccountId)).TicketsReserved);
        }

        /// <summary> 살아 있는 서버 임대가 없는 던전은 접속 토큰을 검증할 수 없으므로 배정하지 않는다 </summary>
        [PostgresFact]
        public async Task Allocate_DungeonWithoutServerLease_IsSkipped()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(0);
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            await environment.Pool.RegisterAsync(new ServerRegistration(Guid.NewGuid(), Guid.NewGuid(),
                ContractCodes.ServerRole.DUNGEON, "127.0.0.1", TEST_PORT, "test-build",
                SessionTestEnvironment.CONTENT_VERSION));

            ReservationOutcome outcome = await environment.Reservations.ReserveAsync(environment.CreateReservation(
                Guid.NewGuid(), GameSessionCodes.Mode.BOSS, new[] { player }, POTIONS_RESERVED));

            Assert.Equal(ContractCodes.RejectReason.NO_CAPACITY, outcome.Result.ReasonCode);
        }

        /// <summary> 등록된 인스턴스는 역할을 바꿔 재등록할 수 없다 </summary>
        [PostgresFact]
        public async Task Register_RoleChange_IsRejected()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(0);
            Guid instanceId = Guid.NewGuid();
            await RegisterAsync(environment, instanceId, Guid.NewGuid(), ContractCodes.ServerRole.DUNGEON);

            ServerPoolOutcome changed = await RegisterAsync(environment, instanceId, Guid.NewGuid(),
                ContractCodes.ServerRole.TOWN);

            Assert.Equal(EServerPoolStatus.RoleMismatch, changed.Status);
        }

        private static async Task<ServerPoolOutcome> RegisterAsync(SessionTestEnvironment environment, Guid instanceId,
            Guid bootId, int role)
        {
            ServerPoolOutcome outcome = await environment.Pool.RegisterAsync(new ServerRegistration(instanceId, bootId,
                role, "127.0.0.1", TEST_PORT, "test-build", SessionTestEnvironment.CONTENT_VERSION));
            if (outcome.Status == EServerPoolStatus.Accepted)
            {
                await environment.GrantServerLeaseAsync(instanceId, role);
            }

            return outcome;
        }
    }
}
