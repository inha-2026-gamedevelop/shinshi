// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 하드 2페이즈 P03, 잔류 부채꼴 연쇄의 표시다. 입력은 서버의 IBossChainFanListener 인자와 같은 값이다
    // 시전 시작부터 safeRevealSeconds까지는 피난 방향만 보이고, 그 뒤로 구역 예고가 폭발 간격마다 하나씩 차례로 나타난다. 구역 k는 시전 시작 + 첫 폭발 + k x 간격에 폭발한다
    // 구역 0은 피난 방향의 오른쪽 끝(위에서 볼 때 시계 방향)에 붙어 시계 방향으로 이어진다. 폭발한 구역은 위험 종료가 알려질 때까지 모두 같이 남는다
    // 폭발은 서버가 알린 구역만 그린다. 확정이 없으면 스스로 폭발하지 않고, 위험 종료 알림이 끝내 오지 않아도 예정 시각에서 일정 시간 뒤 지운다
    // 늦게 접속하면 Begin에 폭발한 구역 수와 위험 종료 여부를 넘긴다. 이 값은 복제 계층이 알림으로 세어 들고 있어야 한다
    /// <summary> 잔류 부채꼴 연쇄의 피난 방향, 구역 예고, 폭발, 잔류 위험 종료, 취소를 시전 번호별로 관리한다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossChainFanView : BossHardTelegraphViewBase
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int   MAX_ZONES = 16;
        private const float FULL_TURN = 360f;
        private const float HALF      = 0.5f;

        /****************************************
        *                Fields
        ****************************************/

        private enum EZoneState
        {
            Pending,
            Blasted,
            Expired
        }

        private sealed class Entry
        {
            public int                  CastId;
            public Vector3              Origin;
            public Vector3              SafeDirection;
            public float                Range;
            public int                  ZoneCount;
            public float                ZoneArc;
            public double               CastStart;
            public float                SafeReveal;
            public float                FirstBlast;
            public float                BlastInterval;
            public float                Linger;
            public EZoneState[]         States;
            public double[]             BlastedAt;
            public BossGroundShapeVFX[] Zones;
            public BossGroundShapeVFX   Safe;
            public int                  BlastedCount;
            public bool                 EndSeen;
            public double               EndSeenAt;
        }

        private readonly List<Entry>        _entries = new List<Entry>();
        private readonly BossEndedIdHistory _ended   = new BossEndedIdHistory(ENDED_HISTORY);

        /****************************************
        *              Properties
        ****************************************/

        public int Count => _entries.Count;

        protected override bool HasEntries => _entries.Count > 0;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 시전을 시작한다. 이미 있는 시전 번호나 끝난 번호면 무시한다. blastedCount는 이미 폭발한 구역 수이고 dangerEnded가 참이면 표시할 것이 없다 </summary>
        public bool Begin(int castInstanceId, Vector3 origin, Vector3 safeDirection, float range, int zoneCount, float zoneArc,
            double castStartServerTime, float safeRevealSeconds, float firstBlastSeconds, float blastIntervalSeconds,
            float lingerSeconds, int blastedCount = 0, bool dangerEnded = false)
        {
            if (IsServerBuild || IsKnown(castInstanceId))
            {
                return false;
            }

            if ((zoneCount < 1) || (zoneCount > MAX_ZONES) || (zoneArc <= 0f) || (range <= 0f))
            {
                return false;
            }

            safeDirection.y = 0f;
            Entry entry = new Entry
            {
                CastId        = castInstanceId,
                Origin        = origin,
                SafeDirection = (safeDirection.sqrMagnitude > (MIN_SPAN * MIN_SPAN)) ? safeDirection.normalized : Vector3.forward,
                Range         = range,
                ZoneCount     = zoneCount,
                ZoneArc       = zoneArc,
                CastStart     = castStartServerTime,
                SafeReveal    = safeRevealSeconds,
                FirstBlast    = firstBlastSeconds,
                BlastInterval = blastIntervalSeconds,
                Linger        = lingerSeconds,
                States        = new EZoneState[zoneCount],
                BlastedAt     = new double[zoneCount],
                Zones         = new BossGroundShapeVFX[zoneCount]
            };

            if (dangerEnded || (ReadServerTime() >= (GetDangerEndTime(entry) + CONFIRM_GRACE_SECONDS + FADE_SECONDS)))
            {
                _ended.Remember(castInstanceId);
                return false;
            }

            entry.BlastedCount = Mathf.Clamp(blastedCount, 0, zoneCount);
            for (int k = 0; k < entry.BlastedCount; ++k)
            {
                entry.States[k]    = EZoneState.Blasted;
                entry.BlastedAt[k] = GetBlastTime(entry, k);
            }

            _entries.Add(entry);
            Wake();
            return true;
        }

        /// <summary> 구역 하나의 폭발 확정. 이미 폭발한 구역이면 무시한다. 확정 없이 지워진 구역도 폭발이 확정되면 다시 그린다 </summary>
        public bool Blast(int castInstanceId, int zoneIndex, double scheduledServerTime)
        {
            if ((IsServerBuild) || (!TryFind(castInstanceId, out Entry entry)))
            {
                return false;
            }

            if ((zoneIndex < 0) || (zoneIndex >= entry.ZoneCount) || (entry.States[zoneIndex] == EZoneState.Blasted))
            {
                return false;
            }

            entry.States[zoneIndex]    = EZoneState.Blasted;
            entry.BlastedAt[zoneIndex] = scheduledServerTime;
            ++entry.BlastedCount;
            Wake();
            return true;
        }

        /// <summary> 잔류 위험 종료, 남은 구역이 옅어지며 사라진다 </summary>
        public bool EndDanger(int castInstanceId, double scheduledServerTime)
        {
            if ((IsServerBuild) || (!TryFind(castInstanceId, out Entry entry)) || entry.EndSeen)
            {
                return false;
            }

            entry.EndSeen   = true;
            entry.EndSeenAt = scheduledServerTime;
            Wake();
            return true;
        }

        /// <summary> 중단, 표시는 바로 지운다. 모르는 시전이면 나중에 늦게 오는 시작이 되살아나지 않게 기억해 둔다 </summary>
        public bool Cancel(int castInstanceId)
        {
            if (IsServerBuild)
            {
                return false;
            }

            if (!TryFind(castInstanceId, out Entry entry))
            {
                _ended.Remember(castInstanceId);
                return false;
            }

            Remove(entry);
            return true;
        }

        /// <summary> 진행 중인 시전의 상태, 시험과 진단용이다 </summary>
        public bool TryGetState(int castInstanceId, out int blastedCount, out bool dangerEnded, out BossGroundShapeVFX safe)
        {
            if (TryFind(castInstanceId, out Entry entry))
            {
                blastedCount = entry.BlastedCount;
                dangerEnded  = entry.EndSeen;
                safe         = entry.Safe;
                return true;
            }

            blastedCount = 0;
            dangerEnded  = false;
            safe         = null;
            return false;
        }

        /// <summary> 구역 하나의 상태, 시험과 진단용이다. 도형이 없으면 shape는 null이다 </summary>
        public bool TryGetZone(int castInstanceId, int zoneIndex, out bool isBlasted, out BossGroundShapeVFX shape)
        {
            if (TryFind(castInstanceId, out Entry entry) && (zoneIndex >= 0) && (zoneIndex < entry.ZoneCount))
            {
                isBlasted = entry.States[zoneIndex] == EZoneState.Blasted;
                shape     = entry.Zones[zoneIndex];
                return true;
            }

            isBlasted = false;
            shape     = null;
            return false;
        }

        public override void ClearAll()
        {
            for (int i = _entries.Count - 1; i >= 0; --i)
            {
                Remove(_entries[i]);
            }
        }

        public override void ResetNetworkSession()
        {
            ClearAll();
            _ended.Clear();
        }

        protected override void Tick(double now)
        {
            for (int i = _entries.Count - 1; i >= 0; --i)
            {
                Entry entry = _entries[i];
                if (TickEntry(entry, now))
                {
                    Remove(entry);
                }
            }
        }

        // 한 시전을 그린다, 끝났으면 참이다
        private bool TickEntry(Entry entry, double now)
        {
            if ((!entry.EndSeen) && (now >= (GetDangerEndTime(entry) + CONFIRM_GRACE_SECONDS)))
            {
                entry.EndSeen   = true;
                entry.EndSeenAt = now;
            }

            float fadeAge = entry.EndSeen ? Mathf.Max(0f, (float)(now - entry.EndSeenAt)) : 0f;
            float fade    = entry.EndSeen ? (1f - Mathf.Clamp01(fadeAge / FADE_SECONDS)) : 1f;
            if (entry.EndSeen && (fadeAge >= FADE_SECONDS))
            {
                return true;
            }

            double revealEnd = entry.CastStart + entry.SafeReveal;

            if (entry.Safe == null)
            {
                entry.Safe = ShapePool.Acquire();
                if (entry.Safe != null)
                {
                    entry.Safe.ShowSector(entry.Origin, entry.SafeDirection, entry.Range, GetSafeArc(entry));
                }
            }

            if (entry.Safe != null)
            {
                // 피난 방향만 보이는 동안은 또렷하게, 구역이 예고된 뒤에는 옅게 남긴다
                if (now < revealEnd)
                {
                    DrawCharging(entry.Safe, SafeColor, 1f, 1f);
                }
                else
                {
                    DrawSafe(entry.Safe, fade);
                }
            }

            for (int k = 0; k < entry.ZoneCount; ++k)
            {
                TickZone(entry, k, now, revealEnd, fade);
            }

            return false;
        }

        private void TickZone(Entry entry, int zone, double now, double revealEnd, float fade)
        {
            double blastTime = GetBlastTime(entry, zone);

            // 구역 예고는 폭발 시각에서 같은 예고 시간만큼 앞서 차례로 나타난다
            double appearTime = blastTime - (entry.FirstBlast - entry.SafeReveal);

            if (entry.States[zone] == EZoneState.Pending)
            {
                if (now < Math.Max(revealEnd, appearTime))
                {
                    return;
                }

                if (now >= (blastTime + CONFIRM_GRACE_SECONDS))
                {
                    entry.States[zone] = EZoneState.Expired;
                    ReleaseShape(ref entry.Zones[zone]);
                    return;
                }
            }

            if (entry.States[zone] == EZoneState.Expired)
            {
                return;
            }

            if (entry.Zones[zone] == null)
            {
                BossGroundShapeVFX shape = ShapePool.Acquire();
                if (shape == null)
                {
                    return;
                }

                shape.ShowSector(entry.Origin, GetZoneDirection(entry, zone), entry.Range, entry.ZoneArc);
                entry.Zones[zone] = shape;
            }

            if (entry.States[zone] == EZoneState.Pending)
            {
                DrawCharging(entry.Zones[zone], Ratio(now, Math.Max(revealEnd, appearTime), blastTime), 1f);
                return;
            }

            float age = Mathf.Max(0f, (float)(now - entry.BlastedAt[zone]));
            if (age < FLASH_SECONDS)
            {
                DrawFlash(entry.Zones[zone], age, FLASH_SECONDS);
            }
            else
            {
                DrawResidual(entry.Zones[zone], fade);
            }
        }

        private static float GetSafeArc(Entry entry)
        {
            return FULL_TURN - (entry.ZoneCount * entry.ZoneArc);
        }

        // 구역 k의 가운데는 피난 방향에서 오른쪽으로 피난 각도의 절반 + 구역 각도 x (k + 0.5)도다
        private static Vector3 GetZoneDirection(Entry entry, int zone)
        {
            float offset = (GetSafeArc(entry) * HALF) + (entry.ZoneArc * (zone + HALF));
            return (Quaternion.AngleAxis(offset, Vector3.up) * entry.SafeDirection).normalized;
        }

        private static double GetBlastTime(Entry entry, int zone)
        {
            return entry.CastStart + entry.FirstBlast + (zone * entry.BlastInterval);
        }

        // 마지막 폭발 뒤 잔류 시간이 지나는 예정 시각이다
        private static double GetDangerEndTime(Entry entry)
        {
            return GetBlastTime(entry, entry.ZoneCount - 1) + entry.Linger;
        }

        private void Remove(Entry entry)
        {
            ReleaseShapes(entry.Zones);
            ReleaseShape(ref entry.Safe);
            _entries.Remove(entry);
            _ended.Remember(entry.CastId);
        }

        private bool TryFind(int castId, out Entry entry)
        {
            for (int i = 0; i < _entries.Count; ++i)
            {
                if (_entries[i].CastId == castId)
                {
                    entry = _entries[i];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        private bool IsKnown(int castId)
        {
            return _ended.Contains(castId) || TryFind(castId, out Entry _);
        }
    }
}
