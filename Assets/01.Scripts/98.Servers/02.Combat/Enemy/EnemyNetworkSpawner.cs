// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Network;

using Object = UnityEngine.Object;

namespace Shinshi.Combat
{
    /// <summary> 일반 적 프리팹을 서버 소유로 스폰하고 서버 몸체의 위치를 복제 개체에 잇는다 </summary>
    public sealed class EnemyNetworkSpawner : IEnemyObjectHost
    {
        // 출정당 180마리, 동시 24마리라 서버에서 Instantiate를 반복하지 않고 NetworkServer.UnSpawn으로 내린 개체를 다시 쓴다.
        // 위치는 프리팹의 NetworkTransform이 적 복제 주기로 보낸다(ApplyEnemyReplication).
        // TODO(진욱, 기웅): 일반 적 네트워크 프리팹(PF_NetEnemy, NetworkIdentity + NetworkTransform)과 체력, 생사, 종류를 싣는 적 상태
        // 복제 컴포넌트가 아직 없다. 프리팹이 없으면 부트스트랩이 경고만 남기고 적 개체를 스폰하지 않는다

        /****************************************
        *                Fields
        ****************************************/

        // 매니저 spawnPrefabs에서 일반 적 프리팹을 찾는 이름. 적 상태 컴포넌트가 생기면 그 컴포넌트로 찾는다
        public const string PREFAB_NAME = "PF_NetEnemy";

        private readonly GameObject             _prefab;
        private readonly ShinshiNetworkManager  _manager;
        private readonly Stack<NetworkIdentity> _pool;

        /****************************************
        *              Properties
        ****************************************/

        public int PooledCount => _pool.Count;

        /****************************************
        *                Methods
        ****************************************/

        public EnemyNetworkSpawner(GameObject prefab, ShinshiNetworkManager manager, int capacity)
        {
            if ((prefab == null) || (manager == null))
            {
                throw new ArgumentNullException(nameof(prefab), "적 프리팹과 네트워크 매니저가 필요하다");
            }

            if (!prefab.TryGetComponent(out NetworkIdentity _))
            {
                throw new ArgumentException("적 프리팹 루트에 NetworkIdentity가 필요하다", nameof(prefab));
            }

            _prefab  = prefab;
            _manager = manager;
            _pool    = new Stack<NetworkIdentity>(capacity);
        }

        /// <summary> 매니저의 spawnPrefabs에서 일반 적 프리팹을 찾는다, 서버 설치 때 한 번만 부른다. 없으면 null </summary>
        public static GameObject FindPrefab(NetworkManager manager)
        {
            if (manager == null)
            {
                return null;
            }

            List<GameObject> prefabs = manager.spawnPrefabs;
            for (int i = 0; i < prefabs.Count; ++i)
            {
                if ((prefabs[i] != null) && (string.Equals(prefabs[i].name, PREFAB_NAME, StringComparison.Ordinal)))
                {
                    return prefabs[i];
                }
            }

            return null;
        }

        public bool TrySpawn(EnemyServer enemy, out NetworkIdentity instance)
        {
            instance = null;
            if ((enemy == null) || (!NetworkServer.active))
            {
                return false;
            }

            NetworkIdentity candidate = Acquire();
            if (candidate == null)
            {
                return false;
            }

            // 스폰 메시지가 첫 위치를 싣도록 기록을 스폰보다 먼저 한다
            Publish(enemy, candidate);
            candidate.gameObject.SetActive(true);
            NetworkServer.Spawn(candidate.gameObject);
            if (candidate.netId == 0)
            {
                Release(candidate);
                Debug.LogError($"[Enemy] 적 개체 스폰에 실패했다, combatId={enemy.CombatId}");
                return false;
            }

            instance = candidate;
            return true;
        }

        public void Publish(EnemyServer enemy, NetworkIdentity instance)
        {
            if ((enemy == null) || (instance == null))
            {
                return;
            }

            instance.transform.SetPositionAndRotation(enemy.Position, Quaternion.LookRotation(enemy.Forward, Vector3.up));
        }

        public void Despawn(NetworkIdentity instance)
        {
            if (instance == null)
            {
                return;
            }

            if ((NetworkServer.active) && (instance.netId != 0))
            {
                NetworkServer.UnSpawn(instance.gameObject);
            }

            Release(instance);
        }

        /// <summary> 풀에 남은 개체를 지운다, 서버를 내릴 때 부른다 </summary>
        public void DestroyPool()
        {
            while (_pool.Count > 0)
            {
                NetworkIdentity pooled = _pool.Pop();
                if (pooled != null)
                {
                    Object.Destroy(pooled.gameObject);
                }
            }
        }

        // 서버 종료가 스폰된 개체를 먼저 지웠을 수 있어 풀에서 꺼낸 개체가 파괴됐으면 버리고 다음 것을 본다
        private NetworkIdentity Acquire()
        {
            while (_pool.Count > 0)
            {
                NetworkIdentity pooled = _pool.Pop();
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
            return identity;
        }

        private void Release(NetworkIdentity instance)
        {
            if (instance == null)
            {
                return;
            }

            instance.gameObject.SetActive(false);
            _pool.Push(instance);
        }
    }
}
