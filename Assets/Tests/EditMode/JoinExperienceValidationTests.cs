// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public sealed class JoinExperienceValidationTests
    {
        private const string INSTANCE_ID = "1b2c3d4e-5f60-4718-8a9b-0c1d2e3f4a5b";
        private const string CHARACTER_ID = "2b2c3d4e-5f60-4718-8a9b-0c1d2e3f4a5b";
        private const string PARTICIPANT_ID = "3b2c3d4e-5f60-4718-8a9b-0c1d2e3f4a5b";
        private const string SORTIE_ID = "4b2c3d4e-5f60-4718-8a9b-0c1d2e3f4a5b";
        private readonly List<Object> _created = new List<Object>();
        private string _hash;

        [SetUp]
        public void SetUp()
        {
            GameDB.ResetForTests();
            GameDB.Initialize(GameDBTestFixtures.CreateValidRoot(_created));
            _hash = CharacterLevelRules.SettingsHash(GameDB.CharacterLevel.ToSettings());
        }

        [TearDown]
        public void TearDown()
        {
            GameDB.ResetForTests();
            for (int i = 0; i < _created.Count; ++i)
            {
                Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
        }

        [TestCase(EServerRole.Dungeon)]
        [TestCase(EServerRole.Town)]
        public void TryCreate_InvalidExperience_RejectsBeforeAdmission(EServerRole role)
        {
            string session = role == EServerRole.Dungeon ? SORTIE_ID : null;
            var valid = new CharacterExperienceSnapshot(2850, session, 0, _hash);
            Assert.That(Validate(role, valid), Is.EqualTo(ERejectReason.None));
            Assert.That(Validate(role, null), Is.EqualTo(ERejectReason.SessionMismatch));
            Assert.That(Validate(role, new CharacterExperienceSnapshot(2850, session, 0, "wrong")),
                Is.EqualTo(ERejectReason.SessionMismatch));
            Assert.That(Validate(role, new CharacterExperienceSnapshot(-1, session, 0, _hash)),
                Is.EqualTo(ERejectReason.SessionMismatch));
            Assert.That(Validate(role, new CharacterExperienceSnapshot(2850, session, -1, _hash)),
                Is.EqualTo(ERejectReason.SessionMismatch));
            string wrongSession = role == EServerRole.Dungeon ? INSTANCE_ID : SORTIE_ID;
            Assert.That(Validate(role, new CharacterExperienceSnapshot(2850, wrongSession, 0, _hash)),
                Is.EqualTo(ERejectReason.SessionMismatch));
        }

        private static ERejectReason Validate(EServerRole role, CharacterExperienceSnapshot experience)
        {
            bool dungeon = role == EServerRole.Dungeon;
            var loadout = new CharacterCombatLoadout((int)EPlayerClass.Warrior,
                new CombatEquipmentEntry[Constants.Combat.EQUIPMENT_SLOT_COUNT],
                new int[Constants.Combat.TRAINING_STAT_COUNT], new CombatTalismanEntry[0], "test");
            var response = new JoinTokenExchangeResponse(CHARACTER_ID, INSTANCE_ID, (int)role,
                dungeon ? PARTICIPANT_ID : null, dungeon ? SORTIE_ID : null, 1000,
                dungeon ? null : PARTICIPANT_ID, (int)EPlayerClass.Warrior, null,
                dungeon ? GameSessionCodes.Mode.NORMAL : JoinTokenExchangeResponse.NO_MODE,
                dungeon ? "region_forest" : null, dungeon ? "battle" : null, null,
                GameSessionCodes.Difficulty.NONE, dungeon ? loadout : null, null, null, null, experience);
            return JoinTokenClaimsRules.TryCreate(role, INSTANCE_ID, response, true, out _);
        }
    }
}
