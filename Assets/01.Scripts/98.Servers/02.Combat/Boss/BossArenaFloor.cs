// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.AI;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;

namespace Shinshi.Combat
{
    // 서버 이동에는 중력, 높이 판정, 트리거가 없어 낙하를 물리로 감지할 수 없다. 그래서 외곽 제거 뒤의 낙하를 위치 표본으로 정한다.
    // CollectFallen이 불릴 때마다(틱마다) 참가자 위치를 한 번 보고, 외곽에서 처음 본 표본 시각이 타이머 시작이며 중앙에서 본 표본 하나가
    // 타이머를 지운다. 외곽 표본이 FALL_SECONDS 이상 이어지면 낙하 확정이다. 두 표본 사이에 중앙을 스쳐 지나간 것은 보지 못하며
    // 틱 간격만큼의 오차로 받아들인다. 회피, 기술 이동, 밀림으로 나간 것도 같은 규칙이다.
    //
    // 제거 순간 외곽에 서 있었다는 이유만으로는 내주지 않는다. 그 사람도 첫 표본부터 타이머가 돌고 그 안에 중앙으로 들어오면 산다.
    // 낙하 이력은 생명 단위다: 한 번 내준 참가자는 다시 내주지 않다가 부활하면(마지막 부활 시각이 낙하 확정 시각 이후) 이력을 지운다.
    // 쓰러진 사람과 명단에서 빠진 사람(이탈 확정)은 보지 않고 타이머도 지운다. 재접속 유예로 전장에 남은 캐릭터는 본다:
    // 연결을 끊어 추락을 피하는 길을 닫는다. 부활 직후 보호는 여기서 보지 않으므로 낙하를 막지 않는다.
    //
    // 이번 단계에는 실제 바닥 오브젝트가 없어 제거는 상태만 바꾼다. 바닥 표시와 맵의 데드존 깊이가 정해지면 FALL_SECONDS를 맞춘다
    /// <summary> 최후의 영역의 외곽 바닥 제거 상태와 서버 위치 기반 낙하 판정, 중앙 바닥의 부활 지점 선택 </summary>
    public sealed class BossArenaFloor : IWolbaekFinalFloor
    {
        /****************************************
        *              Constants
        ****************************************/

        /// <summary> 초, 외곽 표본이 이만큼 이어지면 낙하로 확정한다. 맵의 데드존 깊이가 정해지기 전의 임시값이다 </summary>
        public const float FALL_SECONDS = 0.6f;

        /// <summary> 부활 지점 후보가 전장 중심에서 벗어나는 최대 거리의 중앙 반경 대비 비율 </summary>
        public const float REVIVE_AREA_RATE = 0.5f;

        private const float REVIVE_INNER_RING_RATE = 0.25f; // 안쪽 후보 고리의 중앙 반경 대비 비율
        private const int   REVIVE_RING_POINTS     = 8;     // 고리 하나의 후보 수
        private const int   REVIVE_RING_COUNT      = 2;
        private const float FULL_TURN              = Mathf.PI * 2f;
        private const float NAV_SAMPLE_RADIUS      = 1f;    // m, 후보에서 NavMesh를 찾는 반경
        private const float REVIVE_AREA_TOLERANCE  = 0.01f; // m, 바깥 고리 후보가 부동소수 오차로 범위 밖이 되지 않게 하는 여유
        private const int   NO_SLOT                = -1;

        private const double TIME_EPSILON = 0.000001; // 초, 낙하 시간 경계의 부동소수 여유

        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 부활 지점 후보를 걸을 수 있는 지점으로 맞춘다, 맞출 수 없으면 거짓이다 </summary>
        public delegate bool PointValidator(Vector3 candidate, out Vector3 point);

        private struct Track
        {
            public int    CombatId;
            public bool   IsOutside;    // 마지막 표본이 외곽이었고 타이머가 돌고 있다
            public double OutsideSince; // 이어진 외곽 표본 중 첫 표본의 시각
            public bool   IsDelivered;  // 이 생명에서 낙하를 이미 내줬다
            public double FallTime;     // 낙하를 확정한 시각
        }

        private readonly IBossParticipantRoster _roster;
        private readonly IBossReviveSource      _revive;
        private readonly Vector3                _center;
        private readonly float                  _centralRadius;
        private readonly PointValidator         _validator;
        private readonly IBossStandQuery        _stand;
        private readonly List<int>              _idBuffer;
        private readonly Track[]                _tracks;

        private int  _trackCount = 0;
        private bool _isRemoved  = false;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 외곽 바닥 제거를 받아들였다 </summary>
        public bool IsOuterFloorRemoved => _isRemoved;

        public float CentralRadius => _centralRadius;

        /****************************************
        *                Methods
        ****************************************/

        // center는 전장 중심이고 centralRadius는 제거 뒤에도 남는 중앙 바닥의 반경이다(WolbaekFinalAreaSpec.CentralRadius).
        // validator를 비우면 NavMesh에서 가까운 지점을 찾는다. NavMesh가 없는 시험은 자기 검증을 넘긴다.
        // stand는 지점이 정적 지형과 겹치는지 보는 검사이고 비우면 보지 않는다
        public BossArenaFloor(IBossParticipantRoster roster, IBossReviveSource revive, Vector3 center, float centralRadius,
            PointValidator validator = null, IBossStandQuery stand = null)
        {
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
            _revive = revive ?? throw new ArgumentNullException(nameof(revive));

            if ((float.IsNaN(centralRadius)) || (float.IsInfinity(centralRadius)) || (centralRadius <= 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(centralRadius), "중앙 반경은 0보다 큰 유한값이다");
            }

            float sum = center.x + center.y + center.z;
            if ((float.IsNaN(sum)) || (float.IsInfinity(sum)))
            {
                throw new ArgumentOutOfRangeException(nameof(center), "전장 중심은 유한값이다");
            }

            _center        = center;
            _centralRadius = centralRadius;
            _validator     = validator ?? SampleNavMesh;
            _stand         = stand;
            _idBuffer      = new List<int>(Constants.Combat.MAX_COMBAT_TARGETS);
            _tracks        = new Track[Constants.Combat.MAX_COMBAT_TARGETS];
        }

        // 실제 바닥 오브젝트가 없어 거절할 조건이 없다. 다시 불려도 이미 쌓인 타이머와 이력은 그대로 둔다
        public bool TryRemoveOuterFloor(int castInstanceId, double serverTime)
        {
            _isRemoved = true;
            return true;
        }

        public int CollectFallen(double serverTime, List<int> results)
        {
            results.Clear();
            if (!_isRemoved)
            {
                return 0;
            }

            _idBuffer.Clear();
            _roster.CollectParticipants(_idBuffer);
            DropDeparted();

            for (int i = 0; i < _idBuffer.Count; ++i)
            {
                int combatId = _idBuffer[i];
                if (!_roster.TryGetParticipant(combatId, out BossParticipantInfo info))
                {
                    continue;
                }

                int slot = FindOrAddTrack(combatId);
                if (slot == NO_SLOT)
                {
                    continue;
                }

                if (Sample(ref _tracks[slot], in info, serverTime))
                {
                    results.Add(combatId);
                }
            }

            return results.Count;
        }

        // 후보는 전장 중심, 안쪽 고리, 바깥 고리(중앙 반경의 절반) 순서다. 맞춘 지점이 후보 범위를 벗어나면 버린다:
        // NavMesh 표본이 후보에서 밀려나 외곽으로 나가면 부활하자마자 낙하 타이머가 돈다
        public bool TryGetSafeRevivePoint(int combatId, out Vector3 point)
        {
            if (TryValidate(_center, out point))
            {
                return true;
            }

            for (int ring = 0; ring < REVIVE_RING_COUNT; ++ring)
            {
                float radius = _centralRadius * ((ring == 0) ? REVIVE_INNER_RING_RATE : REVIVE_AREA_RATE);
                for (int i = 0; i < REVIVE_RING_POINTS; ++i)
                {
                    float   angle     = (i / (float)REVIVE_RING_POINTS) * FULL_TURN;
                    Vector3 candidate = _center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    if (TryValidate(candidate, out point))
                    {
                        return true;
                    }
                }
            }

            point = default;
            return false;
        }

        /****************************************
        *                 Fall
        ****************************************/

        // 표본 하나를 반영하고 이 표본으로 낙하가 확정됐으면 참이다
        private bool Sample(ref Track track, in BossParticipantInfo info, double serverTime)
        {
            if (track.IsDelivered)
            {
                // 같은 생명에서는 다시 내주지 않는다. 낙하 확정 뒤에 부활했을 때만 새 생명으로 본다
                if ((!_revive.TryGetLastReviveServerTime(track.CombatId, out double revivedAt)) || (revivedAt < track.FallTime))
                {
                    return false;
                }

                track.IsDelivered = false;
                track.IsOutside   = false;
            }

            if ((info.LifeState != EParticipantLifeState.Alive) || (!IsOutside(info.Position)))
            {
                track.IsOutside = false;
                return false;
            }

            // 두 표본 사이에 죽었다가 부활해 다시 외곽에 선 사람은 이어진 외곽이 아니다
            bool isContinued = (track.IsOutside)
                && ((!_revive.TryGetLastReviveServerTime(track.CombatId, out double lastRevive)) || (lastRevive <= track.OutsideSince));
            if (!isContinued)
            {
                track.IsOutside    = true;
                track.OutsideSince = serverTime;
                return false;
            }

            if ((serverTime - track.OutsideSince) < (FALL_SECONDS - TIME_EPSILON))
            {
                return false;
            }

            track.IsOutside   = false;
            track.IsDelivered = true;
            track.FallTime    = serverTime;
            return true;
        }

        // 수평 거리만 본다. 비유한 좌표는 비교가 거짓이 되어 외곽으로 본다
        private bool IsOutside(Vector3 position)
        {
            float dx = position.x - _center.x;
            float dz = position.z - _center.z;
            return !(((dx * dx) + (dz * dz)) <= (_centralRadius * _centralRadius));
        }

        // 이번 명단에 없는 번호의 기록을 지운다. 이탈이 확정됐거나 재접속으로 캐릭터가 새 번호를 받은 경우다
        private void DropDeparted()
        {
            for (int i = _trackCount - 1; i >= 0; --i)
            {
                if (IsListed(_tracks[i].CombatId))
                {
                    continue;
                }

                --_trackCount;
                _tracks[i] = _tracks[_trackCount];
            }
        }

        private bool IsListed(int combatId)
        {
            for (int i = 0; i < _idBuffer.Count; ++i)
            {
                if (_idBuffer[i] == combatId)
                {
                    return true;
                }
            }

            return false;
        }

        private int FindOrAddTrack(int combatId)
        {
            for (int i = 0; i < _trackCount; ++i)
            {
                if (_tracks[i].CombatId == combatId)
                {
                    return i;
                }
            }

            if (_trackCount >= _tracks.Length)
            {
                return NO_SLOT;
            }

            _tracks[_trackCount] = new Track { CombatId = combatId };
            ++_trackCount;
            return _trackCount - 1;
        }

        /****************************************
        *                Revive
        ****************************************/

        private bool TryValidate(Vector3 candidate, out Vector3 point)
        {
            if (!_validator(candidate, out point))
            {
                return false;
            }

            float dx    = point.x - _center.x;
            float dz    = point.z - _center.z;
            float limit = (_centralRadius * REVIVE_AREA_RATE) + REVIVE_AREA_TOLERANCE;
            if (!(((dx * dx) + (dz * dz)) <= (limit * limit)))
            {
                return false;
            }

            return (_stand == null) || (_stand.CanStand(point, Constants.Combat.PLAYER_BODY_RADIUS));
        }

        private static bool SampleNavMesh(Vector3 candidate, out Vector3 point)
        {
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, NAV_SAMPLE_RADIUS, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }

            point = candidate;
            return false;
        }
    }
}
