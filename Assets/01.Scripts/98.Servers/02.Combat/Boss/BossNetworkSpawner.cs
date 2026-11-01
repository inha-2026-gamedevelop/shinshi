// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.AI;

using Object = UnityEngine.Object;

namespace Shinshi.Combat
{
    /// <summary> 보스 프리팹을 서버 소유로 스폰하고 서버 몸체의 값과 공격 사건을 복제 상태에 잇는다 </summary>
    public sealed class BossNetworkSpawner : IBossObjectHost
    {
        // 던전 프로세스는 출정 하나에 보스 하나만 만들므로 풀을 두지 않는다. 스폰 메시지가 첫 값을 싣도록 상태 기록을 스폰보다 먼저 하고,
        // 사건 연결은 스폰 뒤에 한다. 스폰 전에 Rpc를 부르면 관찰자가 없어 버려지고 기록만 남는다

        /****************************************
        *                Fields
        ****************************************/

        private readonly GameObject                _prefab;
        private readonly IReadOnlyList<GameObject> _candidates; // 보스 ID로 고를 후보, 없으면 기본 프리팹만 쓴다

        /****************************************
        *                Methods
        ****************************************/

        // prefab은 맞는 프리팹이 없는 보스가 쓰는 기본 프리팹이다. candidates는 매니저의 spawnPrefabs를 그대로 넘기면 되고
        // 그 안에서 BossState.PrefabBossId가 보스 ID와 같은 프리팹을 먼저 고른다. 클라이언트도 같은 목록을 등록해 두어야 스폰을 받는다
        public BossNetworkSpawner(GameObject prefab, IReadOnlyList<GameObject> candidates = null)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            if ((!prefab.TryGetComponent(out NetworkIdentity _)) || (!prefab.TryGetComponent(out BossState _)))
            {
                throw new ArgumentException("보스 프리팹 루트에 NetworkIdentity와 BossState가 필요하다", nameof(prefab));
            }

            _prefab     = prefab;
            _candidates = candidates;
        }

        // 보스 ID를 지정하지 않은 프리팹이 기본이다. 모두 지정돼 있으면 처음 찾은 보스 프리팹을 기본으로 쓴다
        /// <summary> 매니저의 spawnPrefabs에서 기본 보스 프리팹을 찾는다, 서버 설치 때 한 번만 부른다 </summary>
        public static GameObject FindPrefab(NetworkManager manager)
        {
            if (manager == null)
            {
                return null;
            }

            GameObject       first   = null;
            List<GameObject> prefabs = manager.spawnPrefabs;
            for (int i = 0; i < prefabs.Count; ++i)
            {
                if ((prefabs[i] == null) || (!prefabs[i].TryGetComponent(out BossState state)))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(state.PrefabBossId))
                {
                    return prefabs[i];
                }

                if (first == null)
                {
                    first = prefabs[i];
                }
            }

            return first;
        }

        /// <summary> 보스 정의 ID에 맞는 프리팹을 고른다, 후보에 없으면 fallback이다 </summary>
        public static GameObject SelectPrefab(IReadOnlyList<GameObject> candidates, GameObject fallback, string bossId)
        {
            if ((candidates == null) || (string.IsNullOrEmpty(bossId)))
            {
                return fallback;
            }

            for (int i = 0; i < candidates.Count; ++i)
            {
                GameObject candidate = candidates[i];
                if ((candidate != null) && (candidate.TryGetComponent(out BossState state)) && (state.PrefabBossId == bossId)
                    && (candidate.TryGetComponent(out NetworkIdentity _)))
                {
                    return candidate;
                }
            }

            return fallback;
        }

        public bool TrySpawn(BossServer boss, out BossState state)
        {
            state = null;
            if ((boss == null) || (!NetworkServer.active))
            {
                return false;
            }

            GameObject prefab   = SelectPrefab(_candidates, _prefab, boss.BossId);
            GameObject instance = Object.Instantiate(prefab, boss.Position, Quaternion.LookRotation(boss.Forward, Vector3.up));
            if (!instance.TryGetComponent(out BossState candidate))
            {
                Object.Destroy(instance);
                return false;
            }

            Publish(boss, candidate);

            // 구조물 배치는 전투 내내 바뀌지 않아 스폰 전에 한 번만 싣는다. 첫 관찰자의 스폰 초기 상태부터 들어간다
            if (boss.TryGetChargeStructureLayout(out BossChargeStructureSnapshot layout))
            {
                candidate.SetChargeStructureLayout(in layout);
            }

            NetworkServer.Spawn(instance);
            if (candidate.netId == 0)
            {
                Object.Destroy(instance);
                Debug.LogError($"[Boss] 보스 개체 스폰에 실패했다, combatId={boss.CombatId}");
                return false;
            }

            boss.SetEventListener(candidate);
            state = candidate;
            return true;
        }

        public void Publish(BossServer boss, BossState state)
        {
            if ((boss == null) || (state == null))
            {
                return;
            }

            state.SetCombatSnapshot(boss.CombatId, boss.BossId, boss.Position, FacingRules.EncodeYaw(boss.Forward), boss.Health,
                boss.MaxHealth, boss.LifeState, boss.FlowState, boss.PhaseState);
            state.SetTimelineSnapshot(boss.AppearStartServerTime, boss.DefeatServerTime, boss.PhaseGeneration,
                boss.PhaseTransitionServerTime);
            state.SetPhase2TransitionSnapshot(boss.Phase2TransitionId, boss.Phase2TransitionStartServerTime,
                boss.Phase2TransitionEndServerTime);
            state.SetBodyPushRadius(boss.BodyPushRadius);
            state.SetArenaEdge(boss.ConfinesToArena, boss.ArenaCenter, boss.ArenaRadius);
        }

        public void Despawn(BossServer boss, BossState state)
        {
            boss?.SetEventListener(null);
            if (state == null)
            {
                return;
            }

            if ((NetworkServer.active) && (state.netId != 0))
            {
                NetworkServer.Destroy(state.gameObject);
                return;
            }

            Object.Destroy(state.gameObject);
        }
    }
}
