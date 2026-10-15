// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 시험 구동기 전용 임시 판정기다. 현재 개발 씬의 바닥은 한 덩어리라 실제로 사라지는 바닥이 없고, 여기서는 중앙에서의 거리만으로
    // "사라진 바닥 위"를 흉내 낸다. 낙하는 운영 창구와 같은 표본 규칙을 따른다: CollectFallen이 불릴 때마다 살아 있는 참가자의 위치를
    // 한 번 보고, 외곽에서 처음 본 시각부터 FALL_SECONDS 이상 외곽 표본이 이어지면 낙하 확정이다. 중앙에서 본 표본 하나가 타이머를
    // 지우며 두 표본 사이에 중앙을 스쳐 지나간 것은 보지 못한다. 사망과 부활도 타이머를 지우고, 한 생명에 한 번만 내주며 부활하면
    // 다시 떨어질 수 있다. 접속이 끊긴 참가자도 명단에 남아 있으면 본다. 구동기에는 이탈이 없어 이탈 정리는 하지 않는다.
    // 사망 확정, 승리 보호, 부활 시각, 노란 여우불 등록도 구동기 안의 대역이며 시간표와 우선순위를 눈으로 시험하려는 용도다.
    // 운영 서버는 이 클래스를 쓰지 않는다
    /// <summary> 최후의 영역의 바닥, 낙하 판정, 추락 사망, 승리 보호, 부활 시각, 노란 여우불 등록을 흉내 내는 시험 대역 </summary>
    public sealed class WolbaekFinalDevJudge : IWolbaekFinalFloor, IWolbaekFinalKiller, IWolbaekFinalGuard, IBossReviveSource,
        IWolbaekYellowRegistry
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  REVIVE_OFFSET_FACTOR = 0.25f;
        private const double FALL_SECONDS         = 0.6; // 초, 외곽 표본이 이만큼 이어지면 낙하 확정이다. 운영 창구의 임시값과 같게 둔다

        /****************************************
        *                Fields
        ****************************************/

        private readonly Vector3                 _center;
        private readonly float                   _centralRadius;
        private readonly IBossParticipantRoster  _roster;
        private readonly Func<int, bool>         _kill;
        private readonly CombatTargetRegistry    _registry;
        private readonly Dictionary<int, double> _stamps     = new Dictionary<int, double>();
        private readonly Dictionary<int, double> _outerSince = new Dictionary<int, double>();
        private readonly HashSet<int>            _yielded    = new HashSet<int>();
        private readonly List<int>               _idBuffer   = new List<int>();

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

        // roster는 낙하 표본의 대상과 위치를 읽는 명단이고, kill은 참가자 대역을 사망 처리하고 살아 있었으면 참을 돌려준다
        public WolbaekFinalDevJudge(Vector3 center, float centralRadius, IBossParticipantRoster roster, Func<int, bool> kill,
            CombatTargetRegistry registry)
        {
            _center        = center;
            _centralRadius = centralRadius;
            _roster        = roster ?? throw new ArgumentNullException(nameof(roster));
            _kill          = kill ?? throw new ArgumentNullException(nameof(kill));
            _registry      = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary> 구동기의 부활 처리가 부활 시각을 기록한다, 새 생명이므로 낙하 타이머와 내준 이력을 지운다 </summary>
        public void NoteRevive(int combatId, double serverTime)
        {
            _stamps[combatId] = serverTime;
            _outerSince.Remove(combatId);
            _yielded.Remove(combatId);
        }

        public bool TryRemoveOuterFloor(int castInstanceId, double serverTime)
        {
            _removed = true;
            return true;
        }

        public int CollectFallen(double serverTime, List<int> results)
        {
            results.Clear();
            if (!_removed)
            {
                return 0;
            }

            _idBuffer.Clear();
            _roster.CollectParticipants(_idBuffer);

            for (int i = 0; i < _idBuffer.Count; ++i)
            {
                int id = _idBuffer[i];
                if ((!_roster.TryGetParticipant(id, out BossParticipantInfo info)) || (info.LifeState != EParticipantLifeState.Alive))
                {
                    _outerSince.Remove(id);
                    continue;
                }

                if (_yielded.Contains(id))
                {
                    continue;
                }

                if (!IsOuter(info.Position))
                {
                    _outerSince.Remove(id);
                    continue;
                }

                if (!_outerSince.TryGetValue(id, out double since))
                {
                    _outerSince.Add(id, serverTime);
                    continue;
                }

                if ((serverTime - since) >= FALL_SECONDS)
                {
                    _outerSince.Remove(id);
                    _yielded.Add(id);
                    results.Add(id);
                }
            }

            _idBuffer.Clear();
            return results.Count;
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

        // 중앙 반경을 넘어선 자리가 사라진 바닥이다, 경계 위는 중앙으로 본다
        private bool IsOuter(Vector3 position)
        {
            float dx = position.x - _center.x;
            float dz = position.z - _center.z;
            return Mathf.Sqrt((dx * dx) + (dz * dz)) > _centralRadius;
        }
    }
}
