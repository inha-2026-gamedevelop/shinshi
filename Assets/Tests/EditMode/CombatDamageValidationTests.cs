// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public sealed class CombatDamageValidationTests
    {
        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryResolveStats_ContentVersionMismatch_DoesNotKeepTalismanRegistration()
        {
            bool serverActive = NetworkServer.active;
            var created = new List<Object>();
            try
            {
                typeof(NetworkServer).GetProperty("active").SetValue(null, true);
                GameDB.ResetForTests();
                GameDB.Initialize(GameDBTestFixtures.CreateValidRoot(created));
                var runtimeRoot = new GameObject("DamageContentValidationTest");
                created.Add(runtimeRoot);
                CombatServerRuntime runtime = runtimeRoot.AddComponent<CombatServerRuntime>();
                runtime.Initialize(GameDB.Player, 30, 1, new OpenRoster());
                var talismans = new TalismanModifierResolver(GameDB.Talismans, GameDB.Skills, 1);
                typeof(CombatServerRuntime).GetField("_talismans", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(runtime, talismans);
                var entries = new CombatTalismanEntry[0];
                Assert.IsTrue(talismans.TryRegister(1, EPlayerClass.Warrior, entries, out string registerError), registerError);
                var loadout = new CharacterCombatLoadout((int)EPlayerClass.Warrior,
                    new CombatEquipmentEntry[Constants.Combat.EQUIPMENT_SLOT_COUNT],
                    new int[Constants.Combat.TRAINING_STAT_COUNT], entries, "mismatched-content");
                var binding = new SessionBinding("participant", 1, null, EPlayerClass.Warrior);
                typeof(SessionBinding).GetProperty("CombatLoadout").SetValue(binding, loadout);
                LogAssert.Expect(LogType.Error, "[Combat] 전투 준비의 콘텐츠 버전이 서버와 다르다, participantId=1, " +
                    "loadout=mismatched-content, server=" + GameDB.ContentVersion);
                MethodInfo resolve = typeof(CombatServerRuntime).GetMethod("TryResolveStats", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsFalse((bool)resolve.Invoke(runtime, new object[] { binding, EPlayerClass.Warrior, null }));
                Assert.IsTrue(talismans.TryRegister(2, EPlayerClass.Warrior, entries, out registerError),
                    "a rejected participant must release the only talisman slot: " + registerError);
            }
            finally
            {
                for (int i = created.Count - 1; i >= 0; --i)
                {
                    Object.DestroyImmediate(created[i]);
                }
                GameDB.ResetForTests();
                typeof(NetworkServer).GetProperty("active").SetValue(null, serverActive);
            }
        }

        /****************************************
        *                Helpers
        ****************************************/

        private sealed class OpenRoster : IParticipantRoster
        {
            public bool IsClosed(int participantId) => false;
            public void Close(int participantId) { }
        }
    }
}
