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

        private readonly GameObject _prefab;

        /****************************************
        *                Methods
        ****************************************/

        public BossNetworkSpawner(GameObject prefab)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            if ((!prefab.TryGetComponent(out NetworkIdentity _)) || (!prefab.TryGetComponent(out BossState _)))
            {
                throw new ArgumentException("보스 프리팹 루트에 NetworkIdentity와 BossState가 필요하다", nameof(prefab));
            }

            _prefab = prefab;
        }

        /// <summary> 매니저의 spawnPrefabs에서 BossState가 붙은 프리팹을 찾는다, 서버 설치 때 한 번만 부른다 </summary>
        public static GameObject FindPrefab(NetworkManager manager)
        {
            if (manager == null)
            {
                return null;
            }

            List<GameObject> prefabs = manager.spawnPrefabs;
            for (int i = 0; i < prefabs.Count; ++i)
            {
                if ((prefabs[i] != null) && (prefabs[i].TryGetComponent(out BossState _)))
                {
                    return prefabs[i];
                }
            }

            return null;
        }

        public bool TrySpawn(BossServer boss, out BossState state)
        {
            state = null;
            if ((boss == null) || (!NetworkServer.active))
            {
                return false;
            }

            GameObject instance = Object.Instantiate(_prefab, boss.Position, Quaternion.LookRotation(boss.Forward, Vector3.up));
            if (!instance.TryGetComponent(out BossState candidate))
            {
                Object.Destroy(instance);
                return false;
            }

            Publish(boss, candidate);
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
