// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 한 전장의 전투 대상 목록과 번호 발급, 서버 판정의 대상 조회 창구 </summary>
    public sealed class CombatTargetRegistry : ICombatTargetQuery
    {
        // 대상 수가 수십이라 공간 분할 없이 선형으로 훑는다. 활성 적 상한이 크게 늘면 격자 분할을 검토한다

        /****************************************
        *                Fields
        ****************************************/

        private readonly List<IDamageable>            _targets;
        private readonly Dictionary<int, IDamageable> _byId;
        private readonly int                          _capacity;

        private int _lastCombatId = CombatIds.NONE;

        /****************************************
        *              Properties
        ****************************************/

        public int Count => _targets.Count;

        /****************************************
        *                Methods
        ****************************************/

        public CombatTargetRegistry(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "전투 대상 상한은 1 이상이다");
            }

            _capacity = capacity;
            _targets  = new List<IDamageable>(capacity);
            _byId     = new Dictionary<int, IDamageable>(capacity);
        }

        /// <summary> 새 전투 번호, 전장 수명 동안 다시 쓰지 않는다 </summary>
        public int IssueCombatId()
        {
            ++_lastCombatId;
            return _lastCombatId;
        }

        /// <summary> 상한을 넘거나 번호가 겹치면 거절한다 </summary>
        public bool Register(IDamageable target)
        {
            if (target == null)
            {
                return false;
            }

            if (target.CombatId == CombatIds.NONE)
            {
                Debug.LogError("[Combat] 전투 번호가 없는 대상은 등록하지 않는다");
                return false;
            }

            if (_targets.Count >= _capacity)
            {
                Debug.LogError($"[Combat] 전투 대상 상한 {_capacity}을 넘어 등록을 거절한다, combatId={target.CombatId}");
                return false;
            }

            if (_byId.ContainsKey(target.CombatId))
            {
                Debug.LogError($"[Combat] 이미 등록된 전투 번호다, combatId={target.CombatId}");
                return false;
            }

            _targets.Add(target);
            _byId.Add(target.CombatId, target);
            return true;
        }

        public bool Unregister(int combatId)
        {
            if (!_byId.Remove(combatId))
            {
                return false;
            }

            // 순서는 판정에 쓰지 않으므로 마지막 원소로 메워 지운다
            int last = _targets.Count - 1;
            for (int i = 0; i <= last; ++i)
            {
                if (_targets[i].CombatId != combatId)
                {
                    continue;
                }

                _targets[i] = _targets[last];
                _targets.RemoveAt(last);
                break;
            }

            return true;
        }

        public bool TryGetDamageable(int combatId, out IDamageable target)
        {
            return _byId.TryGetValue(combatId, out target);
        }

        public bool TryGet(int combatId, out ICombatTarget target)
        {
            if (_byId.TryGetValue(combatId, out IDamageable damageable))
            {
                target = damageable;
                return true;
            }

            target = null;
            return false;
        }

        public int GetLivingPlayers(List<ICombatTarget> results)
        {
            results.Clear();
            for (int i = 0; i < _targets.Count; ++i)
            {
                IDamageable target = _targets[i];
                if ((target.Faction != ECombatFaction.Player) || (target.IsSummon) || (target.LifeState != EParticipantLifeState.Alive))
                {
                    continue;
                }

                results.Add(target);
            }

            return results.Count;
        }

        public int GetLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons,
            List<ICombatTarget> results)
        {
            results.Clear();
            for (int i = 0; i < _targets.Count; ++i)
            {
                IDamageable target = _targets[i];
                if (IsLivingInRadius(target, center, radius, faction, includeSummons))
                {
                    results.Add(target);
                }
            }

            return results.Count;
        }

        public int CountLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons)
        {
            int count = 0;
            for (int i = 0; i < _targets.Count; ++i)
            {
                if (IsLivingInRadius(_targets[i], center, radius, faction, includeSummons))
                {
                    ++count;
                }
            }

            return count;
        }

        private static bool IsLivingInRadius(IDamageable target, Vector3 center, float radius, ECombatFaction faction,
            bool includeSummons)
        {
            if ((target.Faction != faction) || (target.LifeState != EParticipantLifeState.Alive))
            {
                return false;
            }

            if ((target.IsSummon) && (!includeSummons))
            {
                return false;
            }

            Vector3 position = target.Position;
            float   deltaX   = position.x - center.x;
            float   deltaZ   = position.z - center.z;
            return ((deltaX * deltaX) + (deltaZ * deltaZ)) <= (radius * radius);
        }
    }
}
