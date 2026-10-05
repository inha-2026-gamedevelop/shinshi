// System
using System;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Newtonsoft.Json;
using Xunit;

// Project
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Data.Reservations;

namespace Shinshi.Backend.Tests.Growth
{
    /// <summary> 출정 예약이 loadout_revision을 대조하고 전투 준비를 참가자 행에 고정하는지 검사한다 </summary>
    public sealed class ReservationLoadoutPostgresTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Reserve_FreezesLoadoutAndIgnoresRewardOnlyRevision()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(tickets: 0, potions: 0);
            await environment.ExecuteAsync(
                """
                INSERT INTO equipment_instances (item_id, account_id, character_id, definition_id, slot, grade, is_training,
                    origin, enhancement, equipped, bound)
                VALUES (gen_random_uuid(), @AccountId, @CharacterId, 'equip_weapon_rare', 0, 1, false, 'TrialGrant', 17, true, true),
                    (gen_random_uuid(), @AccountId, @CharacterId, 'equip_head_common', 1, 0, false, 'Craft', 3, false, false);
                INSERT INTO character_training (character_id, attack, energy) VALUES (@CharacterId, 4, 2);
                INSERT INTO talisman_progress (character_id, effect_id, level) VALUES (@CharacterId, 'T01-1A', 3);
                INSERT INTO talisman_loadout (character_id, skill_id, tier, effect_id)
                VALUES (@CharacterId, 'skill_warrior_wide_slash', 0, 'T01-1A');
                -- 정산 지급처럼 잔액과 revision만 바뀐 상태. 준비 확인 값(loadout_revision 0)은 그대로다
                UPDATE characters SET essence = essence + 100, revision = revision + 3 WHERE character_id = @CharacterId;
                """, new { player.AccountId, player.CharacterId });

            ReservedSessionView session = await environment.ReserveAsync(GameSessionCodes.Mode.NORMAL, new[] { player }, 0);
            string json = await environment.TextAsync(
                "SELECT loadout_snapshot::text FROM session_participants WHERE session_id = @SessionId;",
                new { session.SessionId });
            CharacterCombatLoadout loadout = JsonConvert.DeserializeObject<CharacterCombatLoadout>(json,
                ContractJson.CreateSettings())!;

            Assert.Equal(ContractCodes.PlayerClass.WARRIOR, loadout.ClassId);
            Assert.Equal(SessionTestEnvironment.CONTENT_VERSION, loadout.ContentVersion);
            Assert.Equal(6, loadout.Equipped.Length);
            Assert.Equal(("equip_weapon_rare", 1, 17), (loadout.Equipped[0].DefinitionId, loadout.Equipped[0].Grade,
                loadout.Equipped[0].Enhancement));
            Assert.All(loadout.Equipped.Skip(1), entry => Assert.Null(entry));
            Assert.Equal(new[] { 4, 0, 0, 0, 2 }, loadout.Training);
            CombatTalismanEntry talisman = Assert.Single(loadout.Talismans);
            Assert.Equal(("skill_warrior_wide_slash", 0, "T01-1A", 3), (talisman.SkillId, talisman.Tier, talisman.EffectId,
                talisman.Level));

            // 출정을 닫고 전투 준비가 바뀌면 예전 revision의 예약은 거절된다
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, (await environment.Reservations.CancelAsync(
                environment.TownId, session.SessionId, Guid.NewGuid())).Status);
            await environment.ExecuteAsync(
                "UPDATE characters SET loadout_revision = loadout_revision + 1 WHERE character_id = @CharacterId;",
                new { player.CharacterId });
            ReservationOutcome stale = await environment.Reservations.ReserveAsync(
                environment.CreateReservation(Guid.NewGuid(), GameSessionCodes.Mode.NORMAL, new[] { player }, 0));
            Assert.Equal(ContractCodes.RejectReason.STALE_REVISION, stale.Result.ReasonCode);
        }
    }
}
