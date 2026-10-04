// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class JoinTokenLoadoutTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string INSTANCE_ID    = "1b2c3d4e-5f60-4718-8a9b-0c1d2e3f4a5b";
        private const string CHARACTER_ID   = "2b2c3d4e-5f60-4718-8a9b-0c1d2e3f4a5b";
        private const string PARTICIPANT_ID = "3b2c3d4e-5f60-4718-8a9b-0c1d2e3f4a5b";
        private const string SORTIE_ID      = "4b2c3d4e-5f60-4718-8a9b-0c1d2e3f4a5b";
        private const string POTION_ID      = "item_potion_small";
        private const string BINDING        = "Account";
        private const long   SERVER_TIME    = 1000;
        private const int    CAPACITY       = 4;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryCreate_DungeonWithLoadout_CarriesLoadoutAndPotionsToBinding()
        {
            CharacterCombatLoadout loadout = Loadout();
            var potions = new[] { new ReservationPotion(POTION_ID, BINDING, 3) };

            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Dungeon, INSTANCE_ID, Response(loadout, potions), true,
                out JoinTokenClaims claims);
            var registry = new ServerSessionRegistry(CAPACITY);
            Assert.AreEqual(ERejectReason.None, registry.TryAdmit(in claims, out SessionBinding binding));

            Assert.AreEqual(ERejectReason.None, reason);
            Assert.AreSame(loadout, claims.CombatLoadout);
            Assert.AreSame(loadout, binding.CombatLoadout);
            Assert.AreSame(potions, binding.ReservedPotions);
        }

        [Test]
        public void TryCreate_RequiredLoadoutMissing_RejectsSessionMismatch()
        {
            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Dungeon, INSTANCE_ID, Response(null, null), true,
                out _);

            Assert.AreEqual(ERejectReason.SessionMismatch, reason);
        }

        [Test]
        public void TryCreate_OptionalLoadoutMissing_AdmitsWithoutLoadout()
        {
            ERejectReason reason = JoinTokenClaimsRules.TryCreate(EServerRole.Dungeon, INSTANCE_ID, Response(null, null), false,
                out JoinTokenClaims claims);

            Assert.AreEqual(ERejectReason.None, reason);
            Assert.IsNull(claims.CombatLoadout);
            Assert.IsNull(claims.ReservedPotions);
        }

        [Test]
        public void ExchangeResponse_PotionsJson_RoundTrips()
        {
            JoinTokenExchangeResponse response = Response(Loadout(), new[] { new ReservationPotion(POTION_ID, BINDING, 2) });

            var read = BackendJson.Deserialize<JoinTokenExchangeResponse>(BackendJson.Serialize(response));

            Assert.AreEqual(1, read.Potions.Length);
            Assert.AreEqual(POTION_ID, read.Potions[0].DefinitionId);
            Assert.AreEqual(BINDING, read.Potions[0].BindingKind);
            Assert.AreEqual(2, read.Potions[0].Quantity);
        }

        [Test]
        public void ExchangeResponse_WithoutPotionsKey_ReadsNull()
        {
            var read = BackendJson.Deserialize<JoinTokenExchangeResponse>("{\"characterId\":\"" + CHARACTER_ID + "\"}");

            Assert.IsNull(read.Potions);
            Assert.IsNull(read.CombatLoadout);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static CharacterCombatLoadout Loadout()
        {
            return new CharacterCombatLoadout((int)EPlayerClass.Warrior, new CombatEquipmentEntry[Constants.Combat.EQUIPMENT_SLOT_COUNT],
                new int[Constants.Combat.TRAINING_STAT_COUNT], new CombatTalismanEntry[0], "test");
        }

        private static JoinTokenExchangeResponse Response(CharacterCombatLoadout loadout, ReservationPotion[] potions)
        {
            return new JoinTokenExchangeResponse(CHARACTER_ID, INSTANCE_ID, (int)EServerRole.Dungeon, PARTICIPANT_ID, SORTIE_ID,
                SERVER_TIME, null, (int)EPlayerClass.Warrior, null, GameSessionCodes.Mode.NORMAL, "map", "battle", null,
                GameSessionCodes.Difficulty.NONE, loadout, null, potions);
        }
    }
}
