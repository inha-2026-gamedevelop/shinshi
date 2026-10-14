// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// Third Party
using Mirror;
using NUnit.Framework;

// Project
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Tests
{
    public sealed class BossPlayerDisplaceLookupTests
    {
        private const BindingFlags PRIVATE   = BindingFlags.Instance | BindingFlags.NonPublic;
        private const int          COMBAT_ID = 7;

        private GameObject _object;
        private PlayerConfigSO _config;
        private CharacterLevelConfigSO _levels;
        private PlayerServer _player;
        private PlayerInputState _input;
        private List<PlayerServer> _players;
        private BossPlayerDisplaceLookup _lookup;
        private bool _serverActive;

        [SetUp]
        public void SetUp()
        {
            _serverActive = NetworkServer.active;
            typeof(NetworkServer).GetProperty("active").SetValue(null, true);
            _config = ScriptableObject.CreateInstance<PlayerConfigSO>();
            _levels = ScriptableObject.CreateInstance<CharacterLevelConfigSO>();
            _object = new GameObject("BossDisplaceTest", typeof(NetworkIdentity));
            _input = _object.AddComponent<PlayerInputState>();
            PlayerCombatState combat = _object.GetComponent<PlayerCombatState>();
            NetworkIdentity identity = _object.GetComponent<NetworkIdentity>();
            PropertyInfo property = typeof(NetworkBehaviour).GetProperty("netIdentity", BindingFlags.Instance | BindingFlags.Public);
            property.SetValue(_input, identity);
            property.SetValue(combat, identity);
            CharacterStats stats = CharacterStatsRules.Base(_config, _levels.ToSettings(), 2850);
            var registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY,
                Constants.Combat.HIT_LEDGER_RETENTION_SECONDS, Constants.Combat.LONGEST_ACTION_SECONDS);
            var resolver = new DamageResolver(registry, ledger, _config.DefenseReductionConstant, _config.DamageReductionCap);
            _player = new PlayerServer(_input, combat, EPlayerClass.Warrior, COMBAT_ID, 1, 1, _config, in stats,
                resolver, registry, new CombatLineOfSight(0, 1f), 0);
            _players = new List<PlayerServer> { _player };
            _lookup = new BossPlayerDisplaceLookup(_players);
        }

        [TearDown]
        public void TearDown()
        {
            _player?.Shutdown();
            Object.DestroyImmediate(_object);
            Object.DestroyImmediate(_config);
            Object.DestroyImmediate(_levels);
            typeof(NetworkServer).GetProperty("active").SetValue(null, _serverActive);
        }

        [Test]
        public void TryGetDisplaceTarget_KnownCombatId_ReturnsThatPlayer()
        {
            Assert.IsTrue(_lookup.TryGetDisplaceTarget(COMBAT_ID, out IBossDisplaceTarget target));
            Assert.AreSame(_player, target);
            Assert.AreEqual(COMBAT_ID, target.CombatId);
            Assert.AreEqual(Constants.Combat.PLAYER_BODY_RADIUS, target.BodyRadius);
        }

        [Test]
        public void TryGetDisplaceTarget_UnknownOrNoneCombatId_ReturnsFalse()
        {
            Assert.IsFalse(_lookup.TryGetDisplaceTarget(COMBAT_ID + 1, out IBossDisplaceTarget unknown));
            Assert.IsNull(unknown);
            Assert.IsFalse(_lookup.TryGetDisplaceTarget(CombatIds.NONE, out IBossDisplaceTarget none));
            Assert.IsNull(none);
        }

        [Test]
        public void TryGetDisplaceTarget_PlayerLeftList_ReturnsFalse()
        {
            _players.Clear();

            Assert.IsFalse(_lookup.TryGetDisplaceTarget(COMBAT_ID, out _), "목록을 복사하지 않아 퇴장이 바로 반영된다");
        }

        [Test]
        public void Displace_Standing_MovesAndRepublishesAuthority()
        {
            _player.PublishMovement(1.0);
            var pushed = new Vector3(0.6f, 0f, -0.8f);
            Assert.IsTrue(_player.CanBeDisplaced);

            _player.Displace(pushed, 1.05);
            _player.PublishMovement(1.05);

            Assert.AreEqual(pushed, _player.Position);
            Assert.AreEqual(pushed, _input.MoveAuthority.Position, "서 있던 대상도 소유 예측이 맞출 권위 위치가 다시 나간다");
            Assert.IsFalse(_input.MoveAuthority.IsMoving);
        }

        [Test]
        public void Displace_FollowingPath_StopsPath()
        {
            PathFollower path = BeginStraightPath(new Vector3(5f, 0f, 0f));
            _player.PublishMovement(1.0);
            Assert.IsTrue(_input.MoveAuthority.IsMoving);
            var pushed = new Vector3(0f, 0f, 1f);

            _player.Displace(pushed, 1.05);
            _player.PublishMovement(1.05);

            Assert.IsFalse(path.IsMoving);
            Assert.AreEqual(pushed, _player.Position);
            Assert.IsFalse(_input.MoveAuthority.IsMoving);
            Assert.AreEqual(pushed, _input.MoveAuthority.Position);
        }

        [Test]
        public void Displace_Dead_DoesNotMove()
        {
            Vector3 before = _player.Position;
            _player.ApplyDamage(_player.MaxHealth, true, 1.0);

            Assert.IsFalse(_player.CanBeDisplaced);
            _player.Displace(before + Vector3.right, 1.05);

            Assert.AreEqual(before, _player.Position);
        }

        [Test]
        public void Displace_Dodging_DoesNotMove()
        {
            Vector3 before = _player.Position;
            typeof(PlayerServer).GetField("_isDodging", PRIVATE).SetValue(_player, true);

            Assert.IsFalse(_player.CanBeDisplaced);
            _player.Displace(before + Vector3.right, 1.05);

            Assert.AreEqual(before, _player.Position);
        }

        [Test]
        public void Displace_NonFinitePosition_KeepsPositionAndPath()
        {
            PathFollower path = BeginStraightPath(new Vector3(5f, 0f, 0f));
            Vector3 before = _player.Position;

            _player.Displace(new Vector3(float.NaN, 0f, 0f), 1.05);
            _player.Displace(new Vector3(0f, float.PositiveInfinity, 0f), 1.05);

            Assert.AreEqual(before, _player.Position);
            Assert.IsTrue(path.IsMoving);
        }

        // EditMode에는 내비 메시가 없어 경로 계산을 거치지 않고 서버 경로 버퍼에 직선 한 구간을 직접 채운다
        private PathFollower BeginStraightPath(Vector3 destination)
        {
            var path = (PathFollower)typeof(PlayerServer).GetField("_path", PRIVATE).GetValue(_player);
            path.CornerBuffer[0] = _player.Position;
            path.CornerBuffer[1] = destination;
            path.Begin(2);
            return path;
        }
    }
}
