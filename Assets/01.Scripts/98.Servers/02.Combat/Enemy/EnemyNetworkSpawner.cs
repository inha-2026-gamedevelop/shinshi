// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;
using Shinshi.Network;

using Object = UnityEngine.Object;

namespace Shinshi.Combat
{
    /// <summary> 일반 적 프리팹을 서버 소유로 스폰하고 위치, 상태, 공격 예고, 출현 예고, 투사체를 복제 개체에 잇는다 </summary>
    public sealed class EnemyNetworkSpawner : IEnemyObjectHost, IEnemyPresentationHost
    {
        // 출정당 180마리, 동시 24마리라 서버에서 Instantiate를 반복하지 않고 NetworkServer.UnSpawn으로 내린 개체를 다시 쓴다.
        // 위치는 프리팹의 NetworkTransform이 적 복제 주기로 보내고(ApplyEnemyReplication), 종류와 체력과 예고는 EnemyNetState가 싣는다.
        // 출현 예고와 투사체는 PF_NetEnemyEffect 개체 하나가 한 건을 맡고 같은 방식으로 풀에 돌려 쓴다.
        // 효과 프리팹이 매니저에 없으면 효과만 꺼지고 적 개체는 그대로 스폰된다

        /****************************************
        *                Fields
        ****************************************/

        // 매니저 spawnPrefabs에서 프리팹을 찾는 이름
        public const string PREFAB_NAME        = "PF_NetEnemy";
        public const string EFFECT_PREFAB_NAME = "PF_NetEnemyEffect";

        private readonly GameObject                                 _prefab;
        private readonly GameObject                                 _effectPrefab;
        private readonly ShinshiNetworkManager                      _manager;
        private readonly Stack<NetworkIdentity>                     _pool;
        private readonly Stack<NetworkIdentity>                     _effectPool;
        private readonly Dictionary<NetworkIdentity, EnemyNetState> _states;
        private readonly Dictionary<int, EEnemyRole>                _pendingRoles;
        private readonly Dictionary<int, NetworkIdentity>           _previews;
        private readonly Dictionary<int, NetworkIdentity>           _projectiles;

        private int _nextPreviewId = 1;

        /****************************************
        *              Properties
        ****************************************/

        public int PooledCount => _pool.Count;

        public int PooledEffectCount => _effectPool.Count;

        public int ActivePreviewCount => _previews.Count;

        public int ActiveProjectileCount => _projectiles.Count;

        /// <summary> 효과 프리팹을 찾았는가, 없으면 출현 예고와 투사체가 보이지 않는다 </summary>
        public bool HasEffectPrefab => _effectPrefab != null;

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

            _prefab       = prefab;
            _manager      = manager;
            _effectPrefab = FindEffectPrefab(manager);
            _pool         = new Stack<NetworkIdentity>(capacity);
            _effectPool   = new Stack<NetworkIdentity>(capacity);
            _states       = new Dictionary<NetworkIdentity, EnemyNetState>(capacity);
            _pendingRoles = new Dictionary<int, EEnemyRole>(capacity);
            _previews     = new Dictionary<int, NetworkIdentity>(capacity);
            _projectiles  = new Dictionary<int, NetworkIdentity>(capacity);

            if (_effectPrefab == null)
            {
                Debug.LogWarning($"[Enemy] {EFFECT_PREFAB_NAME}이 spawnPrefabs에 없어 출현 예고와 투사체 표시가 꺼진다");
            }
        }

        /// <summary> 매니저의 spawnPrefabs에서 일반 적 프리팹을 찾는다, 서버 설치 때 한 번만 부른다. 없으면 null </summary>
        public static GameObject FindPrefab(NetworkManager manager)
        {
            return FindByName(manager, PREFAB_NAME);
        }

        /// <summary> 매니저의 spawnPrefabs에서 효과 프리팹을 찾는다, 없으면 null </summary>
        public static GameObject FindEffectPrefab(NetworkManager manager)
        {
            return FindByName(manager, EFFECT_PREFAB_NAME);
        }

        /// <summary> 적 프리팹이 스포너가 쓰는 구성을 갖췄는지 본다, 빌더와 테스트가 쓴다 </summary>
        public static bool ValidatePrefab(GameObject prefab, out string error)
        {
            if (prefab == null)
            {
                error = "프리팹이 없다";
                return false;
            }

            if (!prefab.TryGetComponent(out NetworkIdentity _))
            {
                error = "루트에 NetworkIdentity가 없다";
                return false;
            }

            if (!prefab.TryGetComponent(out EnemyNetState _))
            {
                error = "루트에 EnemyNetState가 없다";
                return false;
            }

            if (!ShinshiNetworkManager.HasRootTransformReplication(prefab))
            {
                error = "루트에 NetworkTransform이 없다";
                return false;
            }

            if (!prefab.TryGetComponent(out EnemyNetworkView _))
            {
                error = "루트에 EnemyNetworkView가 없다";
                return false;
            }

            if (prefab.GetComponentInChildren<Collider>(true) == null)
            {
                error = "충돌체가 없다";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary> 효과 프리팹이 쓰는 구성을 갖췄는지 본다, 빌더와 테스트가 쓴다 </summary>
        public static bool ValidateEffectPrefab(GameObject prefab, out string error)
        {
            if (prefab == null)
            {
                error = "프리팹이 없다";
                return false;
            }

            if (!prefab.TryGetComponent(out NetworkIdentity _))
            {
                error = "루트에 NetworkIdentity가 없다";
                return false;
            }

            if (!prefab.TryGetComponent(out EnemyEffectState _))
            {
                error = "루트에 EnemyEffectState가 없다";
                return false;
            }

            if (!prefab.TryGetComponent(out EnemyEffectView _))
            {
                error = "루트에 EnemyEffectView가 없다";
                return false;
            }

            error = null;
            return true;
        }

        /****************************************
        *            IEnemyObjectHost
        ****************************************/

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

            EEnemyRole role = EEnemyRole.Melee;
            if (_pendingRoles.TryGetValue(enemy.CombatId, out EEnemyRole pending))
            {
                role = pending;
                _pendingRoles.Remove(enemy.CombatId);
            }

            // 스폰 메시지가 첫 위치와 첫 상태를 싣도록 기록을 스폰보다 먼저 한다
            if (_states.TryGetValue(candidate, out EnemyNetState state))
            {
                state.Bind(enemy.CombatId, role, enemy.Health, enemy.MaxHealth);
            }

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

            if (_states.TryGetValue(instance, out EnemyNetState state))
            {
                state.SetSnapshot(enemy.Health, enemy.MaxHealth, enemy.LifeState);
            }
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
            DestroyStack(_pool);
            DestroyStack(_effectPool);
            _states.Clear();
            _pendingRoles.Clear();
            _previews.Clear();
            _projectiles.Clear();
        }

        /****************************************
        *         IEnemyPresentationHost
        ****************************************/

        public void DescribeEnemy(int combatId, EEnemyRole role)
        {
            _pendingRoles[combatId] = role;
        }

        public void SetBehavior(NetworkIdentity instance, EEnemyState behavior)
        {
            if ((instance != null) && (_states.TryGetValue(instance, out EnemyNetState state)))
            {
                state.SetBehavior(behavior);
            }
        }

        public void BeginTelegraph(NetworkIdentity instance, EEnemyAttackShape shape, Vector3 origin, Vector3 direction, float range,
            float width, double startServerTime, double endServerTime)
        {
            if ((instance != null) && (_states.TryGetValue(instance, out EnemyNetState state)))
            {
                state.BeginTelegraph(shape, origin, direction, range, width, startServerTime, endServerTime);
            }
        }

        public void EndTelegraph(NetworkIdentity instance)
        {
            if ((instance != null) && (_states.TryGetValue(instance, out EnemyNetState state)))
            {
                state.EndTelegraph();
            }
        }

        public int ReserveSpawnPreview(Vector3 position, EEnemyRole role, float radius, double startServerTime, double endServerTime)
        {
            if ((!NetworkServer.active) || (_effectPrefab == null))
            {
                return 0;
            }

            NetworkIdentity effect = AcquireEffect();
            if ((effect == null) || (!effect.TryGetComponent(out EnemyEffectState state)))
            {
                ReleaseEffect(effect);
                return 0;
            }

            int previewId = _nextPreviewId++;
            state.SetupSpawnPreview(previewId, role, position, radius, startServerTime, endServerTime);
            if (!SpawnEffect(effect))
            {
                return 0;
            }

            _previews[previewId] = effect;
            return previewId;
        }

        public void MaterializeSpawnPreview(int previewId)
        {
            RemovePreview(previewId);
        }

        public void CancelSpawnPreview(int previewId)
        {
            RemovePreview(previewId);
        }

        public void CancelAllSpawnPreviews()
        {
            if (_previews.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<int, NetworkIdentity> pair in _previews)
            {
                DespawnEffect(pair.Value);
            }

            _previews.Clear();
        }

        public IBossProjectileListener CreateProjectileListener()
        {
            return new ProjectileAdapter(this);
        }

        /****************************************
        *               Internal
        ****************************************/

        private static GameObject FindByName(NetworkManager manager, string prefabName)
        {
            if (manager == null)
            {
                return null;
            }

            List<GameObject> prefabs = manager.spawnPrefabs;
            for (int i = 0; i < prefabs.Count; ++i)
            {
                if ((prefabs[i] != null) && (string.Equals(prefabs[i].name, prefabName, StringComparison.Ordinal)))
                {
                    return prefabs[i];
                }
            }

            return null;
        }

        private static void DestroyStack(Stack<NetworkIdentity> stack)
        {
            while (stack.Count > 0)
            {
                NetworkIdentity pooled = stack.Pop();
                if (pooled != null)
                {
                    Object.Destroy(pooled.gameObject);
                }
            }
        }

        private void RemovePreview(int previewId)
        {
            if (_previews.TryGetValue(previewId, out NetworkIdentity effect))
            {
                _previews.Remove(previewId);
                DespawnEffect(effect);
            }
        }

        private void LaunchProjectile(in BossProjectileStart start)
        {
            if ((!NetworkServer.active) || (_effectPrefab == null))
            {
                return;
            }

            EndProjectile(start.ProjectileId);

            NetworkIdentity effect = AcquireEffect();
            if ((effect == null) || (!effect.TryGetComponent(out EnemyEffectState state)))
            {
                ReleaseEffect(effect);
                return;
            }

            state.SetupProjectile(start);
            if (SpawnEffect(effect))
            {
                _projectiles[start.ProjectileId] = effect;
            }
        }

        private void EndProjectile(int projectileId)
        {
            if (_projectiles.TryGetValue(projectileId, out NetworkIdentity effect))
            {
                _projectiles.Remove(projectileId);
                DespawnEffect(effect);
            }
        }

        private bool SpawnEffect(NetworkIdentity effect)
        {
            effect.gameObject.SetActive(true);
            NetworkServer.Spawn(effect.gameObject);
            if (effect.netId == 0)
            {
                ReleaseEffect(effect);
                Debug.LogError("[Enemy] 효과 개체 스폰에 실패했다");
                return false;
            }

            return true;
        }

        private void DespawnEffect(NetworkIdentity effect)
        {
            if (effect == null)
            {
                return;
            }

            if (effect.TryGetComponent(out EnemyEffectState state))
            {
                state.Clear();
            }

            if ((NetworkServer.active) && (effect.netId != 0))
            {
                NetworkServer.UnSpawn(effect.gameObject);
            }

            ReleaseEffect(effect);
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

            if (instance.TryGetComponent(out EnemyNetState state))
            {
                _states[identity] = state;
            }

            return identity;
        }

        private NetworkIdentity AcquireEffect()
        {
            while (_effectPool.Count > 0)
            {
                NetworkIdentity pooled = _effectPool.Pop();
                if (pooled != null)
                {
                    return pooled;
                }
            }

            GameObject instance = Object.Instantiate(_effectPrefab);
            if (!instance.TryGetComponent(out NetworkIdentity identity))
            {
                Object.Destroy(instance);
                return null;
            }

            instance.name = _effectPrefab.name;
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

        private void ReleaseEffect(NetworkIdentity effect)
        {
            if (effect == null)
            {
                return;
            }

            effect.gameObject.SetActive(false);
            _effectPool.Push(effect);
        }

        /****************************************
        *             Nested Types
        ****************************************/

        // 보스 투사체 관리자가 쓰는 알림 계약을 효과 개체 스폰, 해제로 옮긴다. 개체에 붙지 않는 서버 객체다
        private sealed class ProjectileAdapter : IBossProjectileListener
        {
            private readonly EnemyNetworkSpawner _owner;

            public ProjectileAdapter(EnemyNetworkSpawner owner)
            {
                _owner = owner;
            }

            public void OnProjectileLaunched(int castInstanceId, int projectileId, Vector3 origin, Vector3 direction, float speed, float width,
                float pathLength, double launchServerTime)
            {
                BossProjectileStart start = new BossProjectileStart(CombatIds.NONE, projectileId, castInstanceId, origin, direction, speed,
                    width, pathLength, launchServerTime);
                _owner.LaunchProjectile(start);
            }

            public void OnProjectileEnded(int castInstanceId, int projectileId, EBossProjectileEnd end, Vector3 position, int hitTargetId)
            {
                _owner.EndProjectile(projectileId);
            }
        }
    }
}
