// System
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class PlayerClassBindingTests
    {
        // 인증기 -> 결합 -> 전투 런타임의 직업 흐름을 시험한다. Mirror 서버를 켜지 않으므로 [Server] 메서드(복제 상태 쓰기)는
        // 경고만 내고 건너뛴다. 그래서 복제 상태 대신 서버 판정이 쓰는 PlayerServer와 SkillServer의 직업을 확인한다

        /****************************************
        *                Fields
        ****************************************/

        private const int    CONNECTION_ID_BASE    = 100;
        private const float  RECONNECT_GRACE       = 30f;
        private const string KEY_A                 = "player-a";
        private const string KEY_B                 = "player-b";
        private const int    CAPACITY              = 4;
        private const float  RETAINED_HEALTH       = 12f;
        private const double RETENTION_EXPIRES_AT  = 1000000.0;

        private readonly List<GameObject> _objects = new List<GameObject>();

        private PlayerConfigSO        _player;
        private SkillCatalogSO        _catalog;
        private CombatServerRuntime   _runtime;
        private ServerSessionRegistry _sessions;
        private int                   _nextConnection;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _player         = ScriptableObject.CreateInstance<PlayerConfigSO>();
            _catalog        = ScriptableObject.CreateInstance<SkillCatalogSO>();
            _sessions       = new ServerSessionRegistry(CAPACITY);
            _nextConnection = CONNECTION_ID_BASE;

            var host = new GameObject("TestCombatRuntime");
            _objects.Add(host);
            _runtime = host.AddComponent<CombatServerRuntime>();
            _runtime.Initialize(_player, RECONNECT_GRACE, 1, new OpenRoster(), _catalog);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _objects.Count; ++i)
            {
                Object.DestroyImmediate(_objects[i]);
            }

            _objects.Clear();
            Object.DestroyImmediate(_player);
            Object.DestroyImmediate(_catalog);
        }

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(EPlayerClass.Warrior)]
        [TestCase(EPlayerClass.Sorcerer)]
        [TestCase(EPlayerClass.Summoner)]
        [TestCase(EPlayerClass.Swordsman)]
        [TestCase(EPlayerClass.Mageknight)]
        [TestCase(EPlayerClass.Archer)]
        public void AddPlayer_BoundClass_CreatesPlayerAndSkillServerWithThatClass(EPlayerClass boundClass)
        {
            PlayerServer player = AddBoundPlayer(KEY_A, boundClass, out _);

            Assert.AreEqual(boundClass, player.PlayerClass);
            Assert.IsNotNull(player.Skills);
            Assert.AreEqual(boundClass, player.Skills.PlayerClass);
        }

        [TestCase(EPlayerClass.Warrior)]
        [TestCase(EPlayerClass.Sorcerer)]
        [TestCase(EPlayerClass.Summoner)]
        [TestCase(EPlayerClass.Swordsman)]
        [TestCase(EPlayerClass.Mageknight)]
        [TestCase(EPlayerClass.Archer)]
        public void AddPlayer_BoundClass_UsesThatClassBasicAttack(EPlayerClass boundClass)
        {
            PlayerServer player = AddBoundPlayer(KEY_A, boundClass, out _);

            FieldInfo attack = typeof(PlayerServer).GetField("_attack", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.AreSame(_player.GetBasicAttack(boundClass), attack.GetValue(player));
        }

        [Test]
        public void AddPlayer_ReconnectWithSameClass_ReusesRetainedSkills()
        {
            PlayerServer first        = AddBoundPlayer(KEY_A, EPlayerClass.Sorcerer, out PlayerInputState firstInput);
            SkillServer  retained     = first.Skills;
            RetainSkillsAfterRemove(firstInput, retained);

            PlayerServer second = AddBoundPlayer(KEY_A, EPlayerClass.Sorcerer, out _);

            Assert.AreSame(retained, second.Skills);
            Assert.AreEqual(EPlayerClass.Sorcerer, second.PlayerClass);
            Assert.AreEqual(RETAINED_HEALTH, second.Health);
            Assert.AreEqual(0, RetainedSkills().Count);
        }

        [Test]
        public void AddPlayer_ReconnectWithDifferentClass_DiscardsRetainedSkillsAndUsesApiClass()
        {
            PlayerServer first    = AddBoundPlayer(KEY_A, EPlayerClass.Warrior, out PlayerInputState firstInput);
            SkillServer  retained = first.Skills;
            RetainSkillsAfterRemove(firstInput, retained);

            LogAssert.Expect(LogType.Error, new Regex("보존된 기술 상태의 직업이 API 직업과 달라 버린다"));
            PlayerServer second = AddBoundPlayer(KEY_A, EPlayerClass.Summoner, out _);

            // API가 준 직업이 정본이고, 다른 직업의 기술 상태는 이어 쓰지 않는다
            Assert.AreEqual(EPlayerClass.Summoner, second.PlayerClass);
            Assert.AreEqual(EPlayerClass.Summoner, second.Skills.PlayerClass);
            Assert.AreNotSame(retained, second.Skills);
            Assert.AreEqual(0, RetainedSkills().Count);

            // 체력 같은 직업과 무관한 보존 기록은 그대로 이어진다
            Assert.AreEqual(RETAINED_HEALTH, second.Health);
        }

        [Test]
        public void TryAdmit_Reconnect_TakesClassFromNewClaimsNotFromPreviousBinding()
        {
            _sessions.TryAdmit(new JoinTokenClaims(KEY_A, null, null, null, null, EPlayerClass.Sorcerer), out SessionBinding first);
            _sessions.Release(first);

            ERejectReason reason = _sessions.TryAdmit(new JoinTokenClaims(KEY_A, null, null, null, null, EPlayerClass.Summoner),
                out SessionBinding second);

            Assert.AreEqual(ERejectReason.None, reason);
            Assert.AreEqual(first.ParticipantId, second.ParticipantId);
            Assert.AreEqual(EPlayerClass.Sorcerer, first.PlayerClass);
            Assert.AreEqual(EPlayerClass.Summoner, second.PlayerClass);
        }

        [TestCase(EPlayerClass.Warrior)]
        [TestCase(EPlayerClass.Sorcerer)]
        [TestCase(EPlayerClass.Summoner)]
        [TestCase(EPlayerClass.Swordsman)]
        [TestCase(EPlayerClass.Mageknight)]
        [TestCase(EPlayerClass.Archer)]
        public void TryAdmit_ClaimsClass_IsCarriedToBinding(EPlayerClass playerClass)
        {
            ERejectReason reason = _sessions.TryAdmit(new JoinTokenClaims(KEY_B, null, null, null, null, playerClass),
                out SessionBinding binding);

            Assert.AreEqual(ERejectReason.None, reason);
            Assert.AreEqual(playerClass, binding.PlayerClass);
        }

        [TestCase(EPlayerClass.Warrior, EPlayerClass.Warrior, true)]
        [TestCase(EPlayerClass.Sorcerer, EPlayerClass.Sorcerer, true)]
        [TestCase(EPlayerClass.Sorcerer, EPlayerClass.Warrior, false)]
        [TestCase(EPlayerClass.Summoner, EPlayerClass.Sorcerer, false)]
        [TestCase(EPlayerClass.Archer, EPlayerClass.Archer, true)]
        [TestCase(EPlayerClass.Archer, EPlayerClass.Swordsman, false)]
        [TestCase(EPlayerClass.Mageknight, EPlayerClass.Sorcerer, false)]
        public void MatchesBoundClass_OnlySameClass_AllowsReuse(EPlayerClass bound, EPlayerClass retained, bool expected)
        {
            Assert.AreEqual(expected, RetentionRules.MatchesBoundClass(bound, retained));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private PlayerServer AddBoundPlayer(string key, EPlayerClass boundClass, out PlayerInputState input)
        {
            ERejectReason reason = _sessions.TryAdmit(new JoinTokenClaims(key, null, null, null, null, boundClass),
                out SessionBinding binding);
            Assert.AreEqual(ERejectReason.None, reason);

            // 서버를 켜지 않으므로 Mirror가 연결해 주지 않는 개체 식별자와 소유 연결을 직접 이어 준다
            var owner = new GameObject("TestPlayer_" + key, typeof(NetworkIdentity));
            _objects.Add(owner);
            input = owner.AddComponent<PlayerInputState>();
            NetworkIdentity identity = owner.GetComponent<NetworkIdentity>();
            PropertyInfo    netIdentity = typeof(NetworkBehaviour).GetProperty("netIdentity", BindingFlags.Instance | BindingFlags.Public);
            netIdentity.SetValue(input, identity);
            netIdentity.SetValue(owner.GetComponent<PlayerCombatState>(), identity);

            var connection = new NetworkConnectionToClient(_nextConnection++) { authenticationData = binding };
            typeof(NetworkIdentity).GetProperty("connectionToClient", BindingFlags.Instance | BindingFlags.Public)
                .SetValue(identity, connection);

            _runtime.AddPlayer(input);

            Assert.IsTrue(TryFindPlayer(binding.ParticipantId, out PlayerServer player), "전투 런타임이 플레이어를 만들지 못했다");
            return player;
        }

        private bool TryFindPlayer(int participantId, out PlayerServer player)
        {
            FieldInfo players = typeof(CombatServerRuntime).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic);
            var       list    = (List<PlayerServer>)players.GetValue(_runtime);
            for (int i = 0; i < list.Count; ++i)
            {
                if (list[i].ParticipantId == participantId)
                {
                    player = list[i];
                    return true;
                }
            }

            player = null;
            return false;
        }

        // 던전의 연결 끊김 보존 경로를 흉내 낸다. 개체가 사라진 뒤 체력 기록(보존소)과 기술 서버만 남는 상태다
        private void RetainSkillsAfterRemove(PlayerInputState input, SkillServer skills)
        {
            _runtime.RemovePlayer(input);
            int participantId = ((SessionBinding)input.connectionToClient.authenticationData).ParticipantId;
            var snapshot      = new ParticipantCombatSnapshot(RETAINED_HEALTH, EParticipantLifeState.Alive, 0.0, 0.0, 0u, 0, 0.0);
            _runtime.ReconnectVault.Store(participantId, in snapshot, RETENTION_EXPIRES_AT, out _);
            RetainedSkills()[participantId] = skills;
            var ticks = (List<SkillServer>)typeof(CombatServerRuntime)
                .GetField("_retainedSkillTicks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_runtime);
            ticks.Add(skills);

            // 끊긴 연결의 결합은 풀어야 같은 참가자가 다시 들어온다
            _sessions.Release((SessionBinding)input.connectionToClient.authenticationData);
        }

        private Dictionary<int, SkillServer> RetainedSkills()
        {
            return (Dictionary<int, SkillServer>)typeof(CombatServerRuntime)
                .GetField("_retainedSkills", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_runtime);
        }

        private sealed class OpenRoster : IParticipantRoster
        {
            public bool IsClosed(int participantId) { return false; }
            public void Close(int participantId) { }
        }
    }
}
