// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Network;

using Object = UnityEngine.Object;

namespace Shinshi.Combat
{
    /// <summary> 동료 프리팹을 서버 소유로 스폰하고 내린 개체를 풀에 모아 다시 쓴다 </summary>
    public sealed class CompanionNetworkSpawner : ICompanionObjectHost
    {
        // 내릴 때 NetworkServer.UnSpawn을 쓴다. 서버 개체는 netId를 비우고 꺼진 채 남아 다음 소환에 다시 스폰되고,
        // 클라이언트는 받은 개체를 지운다. 동료는 20초마다 생기고 사라지므로 서버에서 Instantiate를 반복하지 않는다.
        // 클라이언트 쪽 생성은 출정당 최대 8체, 20초 주기라 풀을 두지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private readonly GameObject                  _prefab;
        private readonly ShinshiNetworkManager       _manager;
        private readonly Stack<CompanionCombatState> _pool;

        /****************************************
        *              Properties
        ****************************************/

        public int PooledCount => _pool.Count;

        /****************************************
        *                Methods
        ****************************************/

        public CompanionNetworkSpawner(GameObject prefab, ShinshiNetworkManager manager, int capacity)
        {
            if ((prefab == null) || (manager == null))
            {
                throw new ArgumentNullException(nameof(prefab), "동료 프리팹과 네트워크 매니저가 필요하다");
            }

            if ((!prefab.TryGetComponent(out NetworkIdentity _)) || (!prefab.TryGetComponent(out CompanionCombatState _)))
            {
                throw new ArgumentException("동료 프리팹 루트에 NetworkIdentity와 CompanionCombatState가 필요하다", nameof(prefab));
            }

            _prefab  = prefab;
            _manager = manager;
            _pool    = new Stack<CompanionCombatState>(capacity);
        }

        /// <summary> 매니저의 spawnPrefabs에서 CompanionCombatState가 붙은 프리팹을 찾는다, 서버 설치 때 한 번만 부른다 </summary>
        public static GameObject FindPrefab(NetworkManager manager)
        {
            if (manager == null)
            {
                return null;
            }

            List<GameObject> prefabs = manager.spawnPrefabs;
            for (int i = 0; i < prefabs.Count; ++i)
            {
                if ((prefabs[i] != null) && (prefabs[i].TryGetComponent(out CompanionCombatState _)))
                {
                    return prefabs[i];
                }
            }

            return null;
        }

        public bool TrySpawn(ICompanionSlotSource source, int slot, out CompanionCombatState state)
        {
            state = null;
            if ((source == null) || (!NetworkServer.active))
            {
                return false;
            }

            CompanionCombatState candidate = Acquire();
            if (candidate == null)
            {
                return false;
            }

            // 스폰 메시지가 첫 값을 싣도록 상태 연결(현재 판정 값 기록)을 스폰보다 먼저 한다
            if (!source.BindCompanionState(slot, candidate))
            {
                Release(candidate);
                Debug.LogError($"[Combat] 동료 상태를 연결하지 못해 스폰하지 않는다, participantId={source.ParticipantId}, slot={slot}");
                return false;
            }

            NetworkServer.Spawn(candidate.gameObject);
            if (candidate.netId == 0)
            {
                source.UnbindCompanionState(slot);
                Release(candidate);
                Debug.LogError($"[Combat] 동료 개체 스폰에 실패했다, participantId={source.ParticipantId}, slot={slot}");
                return false;
            }

            state = candidate;
            return true;
        }

        public void Despawn(ICompanionSlotSource source, int slot, CompanionCombatState state)
        {
            source?.UnbindCompanionState(slot);
            if (state == null)
            {
                return;
            }

            if ((NetworkServer.active) && (state.netId != 0))
            {
                NetworkServer.UnSpawn(state.gameObject);
            }

            Release(state);
        }

        public void DestroyPool()
        {
            while (_pool.Count > 0)
            {
                CompanionCombatState state = _pool.Pop();
                if (state != null)
                {
                    Object.Destroy(state.gameObject);
                }
            }
        }

        // 서버 종료가 스폰된 개체를 먼저 지웠을 수 있어 풀에서 꺼낸 개체가 파괴됐으면 버리고 다음 것을 본다
        private CompanionCombatState Acquire()
        {
            while (_pool.Count > 0)
            {
                CompanionCombatState pooled = _pool.Pop();
                if (pooled != null)
                {
                    return pooled;
                }
            }

            GameObject instance = Object.Instantiate(_prefab);
            if (!instance.TryGetComponent(out NetworkIdentity identity))
            {
                Object.Destroy(instance);
                return null;
            }

            // 클라이언트의 생성 처리기와 같은 간격을 써야 원격 보간 버퍼가 맞는다. 풀에서 다시 써도 설정은 유지된다
            _manager.ApplyEnemyReplication(identity);
            instance.name = _prefab.name;
            return instance.GetComponent<CompanionCombatState>();
        }

        private void Release(CompanionCombatState state)
        {
            if (state == null)
            {
                return;
            }

            if (state.gameObject.activeSelf)
            {
                state.gameObject.SetActive(false);
            }

            _pool.Push(state);
        }
    }
}
