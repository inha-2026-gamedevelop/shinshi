// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.AI
{
    // 시험 구동기 전용 임시 판정기다. 현재 개발 씬의 바닥은 한 덩어리라 실제로 사라지는 바닥이 없고, 여기서는 중앙에서의 거리만으로
    // "사라진 바닥 위"를 흉내 낸다. 이 거리 판정은 운영의 바닥 구조와 서버 추락 판정이 아니다. 사망 확정, 승리 보호, 부활 시각, 노란 여우불
    // 등록도 구동기 안의 대역이며 시간표와 우선순위를 눈으로 시험하려는 용도다. 운영 서버는 이 클래스를 쓰지 않는다
    /// <summary> 최후의 영역의 바닥, 추락 사망, 승리 보호, 부활 시각, 노란 여우불 등록을 흉내 내는 시험 대역 </summary>
    public sealed class WolbaekFinalDevJudge : IWolbaekFinalFloor, IWolbaekFinalKiller, IWolbaekFinalGuard, IBossReviveSource,
        IWolbaekYellowRegistry
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float REVIVE_OFFSET_FACTOR = 0.25f;

        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 참가자의 지금 위치를 읽는다, 쓰러졌거나 없으면 거짓이다 </summary>
        public delegate bool PositionReader(int combatId, out Vector3 position);

        private readonly Vector3                      _center;
        private readonly float                        _centralRadius;
        private readonly PositionReader               _positions;
        private readonly Func<int, bool>              _kill;
        private readonly CombatTargetRegistry         _registry;
        private readonly System.Collections.Generic.Dictionary<int, double> _stamps =
            new System.Collections.Generic.Dictionary<int, double>();

        private bool _removed;
        private bool _victoryProtected;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 외곽 바닥 제거를 받아들였다 </summary>
        public bool IsRemoved => _removed;

        /// <summary> 승리 보호 호출을 받았다, 구동기의 플레이어 대역은 실제로 보호받지 않는다 </summary>
        public bool VictoryProtected => _victoryProtected;

        /****************************************
        *                Methods
        ****************************************/

        // kill은 참가자 대역을 사망 처리하고 살아 있었으면 참을 돌려준다
        public WolbaekFinalDevJudge(Vector3 center, float centralRadius, PositionReader positions, Func<int, bool> kill,
            CombatTargetRegistry registry)
        {
            _center        = center;
            _centralRadius = centralRadius;
            _positions     = positions ?? throw new ArgumentNullException(nameof(positions));
            _kill          = kill ?? throw new ArgumentNullException(nameof(kill));
            _registry      = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary> 구동기의 부활 처리가 부활 시각을 기록한다 </summary>
        public void NoteRevive(int combatId, double serverTime)
        {
            _stamps[combatId] = serverTime;
        }

        public bool TryRemoveOuterFloor(int castInstanceId, double serverTime)
        {
            _removed = true;
            return true;
        }

        public bool IsOnRemovedFloor(int combatId)
        {
            if (!_removed)
            {
                return false;
            }

            if (!_positions(combatId, out Vector3 position))
            {
                return false;
            }

            float dx = position.x - _center.x;
            float dz = position.z - _center.z;
            return Mathf.Sqrt((dx * dx) + (dz * dz)) > _centralRadius;
        }

        // 중앙에서 조금 비켜난 임시 지점이다, 운영의 검증된 안전 지점이 아니다
        public bool TryGetSafeRevivePoint(int combatId, out Vector3 point)
        {
            point = _center + (Vector3.right * (_centralRadius * REVIVE_OFFSET_FACTOR));
            return true;
        }

        public bool TryKill(int combatId, int castInstanceId, EWolbaekFinalDeath cause)
        {
            return _kill(combatId);
        }

        public void GrantVictoryProtection(int castInstanceId, double serverTime)
        {
            _victoryProtected = true;
        }

        public bool TryGetLastReviveServerTime(int combatId, out double serverTime)
        {
            return _stamps.TryGetValue(combatId, out serverTime);
        }

        public int IssueCombatId()
        {
            return _registry.IssueCombatId();
        }

        public bool Register(IDamageable target)
        {
            return _registry.Register(target);
        }

        public bool Unregister(int combatId)
        {
            return _registry.Unregister(combatId);
        }
    }
}
