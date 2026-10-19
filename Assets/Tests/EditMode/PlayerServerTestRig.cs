// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Tests
{
    // BossPlayerDisplaceLookupTests가 EditMode에서 PlayerServer를 만드는 방식을 여러 명에 쓰도록 묶었다.
    // 복제 상태가 서버 전용 호출을 받도록 NetworkServer.active를 켜 두고 Dispose에서 되돌린다
    /// <summary> EditMode 시험용 서버 플레이어 생성기, 만든 개체와 설정을 Dispose에서 지운다 </summary>
    public sealed class PlayerServerTestRig
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int SESSION_EPOCH = 1;
        private const int BASE_ATTACK   = 2850;

        private readonly List<GameObject>       _objects = new List<GameObject>();
        private readonly PlayerConfigSO         _config;
        private readonly CharacterLevelConfigSO _levels;
        private readonly bool                   _wasServerActive;

        /****************************************
        *              Properties
        ****************************************/

        public CombatTargetRegistry Registry { get; }
        public DamageResolver       Resolver { get; }
        public List<PlayerServer>   Players  { get; } = new List<PlayerServer>();
        public List<PlayerCombatState> Combats { get; } = new List<PlayerCombatState>();
        public PlayerConfigSO       Config   => _config;

        /****************************************
        *                Methods
        ****************************************/

        public PlayerServerTestRig()
        {
            _wasServerActive = NetworkServer.active;
            typeof(NetworkServer).GetProperty("active").SetValue(null, true);

            _config  = ScriptableObject.CreateInstance<PlayerConfigSO>();
            _levels  = ScriptableObject.CreateInstance<CharacterLevelConfigSO>();
            Registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);

            var ledger = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY, Constants.Combat.HIT_LEDGER_RETENTION_SECONDS,
                Constants.Combat.LONGEST_ACTION_SECONDS);
            Resolver = new DamageResolver(Registry, ledger, _config.DefenseReductionConstant, _config.DamageReductionCap);
        }

        /// <summary> 플레이어를 하나 만들어 목록에 더한다, 전투 번호는 등록부가 발급한다 </summary>
        public PlayerServer Add(int participantId, Vector3 position, EPlayerClass playerClass = EPlayerClass.Warrior)
        {
            var owner = new GameObject($"PlayerServerTestRig{participantId}", typeof(NetworkIdentity));
            owner.transform.position = position;
            _objects.Add(owner);

            PlayerInputState  input    = owner.AddComponent<PlayerInputState>();
            PlayerCombatState combat   = owner.GetComponent<PlayerCombatState>();
            NetworkIdentity   identity = owner.GetComponent<NetworkIdentity>();
            PropertyInfo      property = typeof(NetworkBehaviour).GetProperty("netIdentity", BindingFlags.Instance | BindingFlags.Public);
            property.SetValue(input, identity);
            property.SetValue(combat, identity);

            CharacterStats stats  = CharacterStatsRules.Base(_config, _levels.ToSettings(), BASE_ATTACK);
            var            player = new PlayerServer(input, combat, playerClass, Registry.IssueCombatId(), participantId,
                SESSION_EPOCH, _config, in stats, Resolver, Registry, new CombatLineOfSight(0, 1f), 0.0);
            Players.Add(player);
            Combats.Add(combat);
            return player;
        }

        public void Dispose()
        {
            for (int i = 0; i < Players.Count; ++i)
            {
                Players[i].Shutdown();
            }

            Players.Clear();
            for (int i = 0; i < _objects.Count; ++i)
            {
                Object.DestroyImmediate(_objects[i]);
            }

            _objects.Clear();
            Object.DestroyImmediate(_config);
            Object.DestroyImmediate(_levels);
            typeof(NetworkServer).GetProperty("active").SetValue(null, _wasServerActive);
        }
    }
}
