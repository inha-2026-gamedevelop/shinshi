// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 하드 2페이즈 P05, 전방위 투사체 난사의 발별 경로 예고 표시다. 입력은 서버의 IBossBarrageListener 인자와 같은 값이다
    // 발 하나의 예고는 원점에서 서버가 알린 방향으로 벽 앞까지의 경로 길이만큼 뻗는 띠이고, 예정 시각부터 telegraphSeconds 동안 차오른다
    // 발사가 확정되면 예고는 짧게 번쩍이고 사라진다. 나간 투사체의 비행과 소멸은 기존 투사체 표현(OnProjectileLaunched)이 그린다
    // 방향은 발의 예고 알림에서 처음 알려지므로 예고 전에는 다음 발을 그릴 수 없다. 발사가 확정되지 않으면 스스로 쏘지 않고 예고만 지운다
    // 늦게 접속하면 Begin에 이미 발사된 발 수를 넘기고, 아직 발사되지 않은 발만 TelegraphShot으로 다시 알려야 한다. 복제 계층이 그 발들의 방향과 경로 길이와 예정 시각을 들고 있어야 한다
    /// <summary> 전방위 난사의 발별 예고, 발사, 취소를 시전 번호별로 관리한다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossBarrageView : BossHardTelegraphViewBase
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int   MAX_SHOTS        = 32;
        private const float FIRE_FLASH_SECONDS = 0.15f; // 발사가 확정된 예고가 번쩍이는 시간

        /****************************************
        *                Fields
        ****************************************/

        private enum EShotState
        {
            Idle,
            Telegraphed,
            Fired,
            Expired
        }

        private sealed class Entry
        {
            public int                  CastId;
            public Vector3              Origin;
            public double               CastStart;
            public int                  ShotCount;
            public float                TelegraphSeconds;
            public float                ShotInterval;
            public float                Width;
            public EShotState[]         States;
            public int[]                SectorIndices;
            public double[]             TelegraphAt;
            public double[]             FiredAt;
            public bool[]               Launched;
            public BossGroundShapeVFX[] Shapes;
            public int                  FiredCount;
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

        /// <summary> 시전을 시작한다. 이미 있는 시전 번호나 끝난 번호면 무시한다. firedCount는 이미 발사 처리된 발 수다 </summary>
        public bool Begin(int castInstanceId, Vector3 origin, double castStartServerTime, int shotCount, float telegraphSeconds,
            float shotIntervalSeconds, float width, int firedCount = 0)
        {
            if (IsServerBuild || IsKnown(castInstanceId))
            {
                return false;
            }

            if ((shotCount < 1) || (shotCount > MAX_SHOTS))
            {
                return false;
            }

            Entry entry = new Entry
            {
                CastId           = castInstanceId,
                Origin           = origin,
                CastStart        = castStartServerTime,
                ShotCount        = shotCount,
                TelegraphSeconds = telegraphSeconds,
                ShotInterval     = shotIntervalSeconds,
                Width            = width,
                States           = new EShotState[shotCount],
                SectorIndices    = new int[shotCount],
                TelegraphAt      = new double[shotCount],
                FiredAt          = new double[shotCount],
                Launched         = new bool[shotCount],
                Shapes           = new BossGroundShapeVFX[shotCount]
            };

            // 마지막 발의 발사 처리까지 지난 시전은 그리지 않고 늦게 온 알림이 되살리지도 못한다
            double lastFire = castStartServerTime + telegraphSeconds + ((shotCount - 1) * shotIntervalSeconds);
            if ((firedCount >= shotCount) || (ReadServerTime() >= (lastFire + CONFIRM_GRACE_SECONDS + FIRE_FLASH_SECONDS)))
            {
                _ended.Remember(castInstanceId);
                return false;
            }

            int fired = Mathf.Clamp(firedCount, 0, shotCount);
            for (int k = 0; k < fired; ++k)
            {
                entry.States[k] = EShotState.Fired;
            }

            entry.FiredCount = fired;
            _entries.Add(entry);
            Wake();
            return true;
        }

        /// <summary> 발 하나의 예고 시작. 이미 예고했거나 발사한 발이면 무시한다. 시각은 서버가 알린 예정 시각이다 </summary>
        public bool TelegraphShot(int castInstanceId, int shotIndex, int sectorIndex, Vector3 direction, float pathLength,
            double scheduledServerTime)
        {
            if ((IsServerBuild) || (!TryFind(castInstanceId, out Entry entry)))
            {
                return false;
            }

            if ((shotIndex < 0) || (shotIndex >= entry.ShotCount) || (entry.States[shotIndex] != EShotState.Idle) || (pathLength <= 0f))
            {
                return false;
            }

            BossGroundShapeVFX shape = ShapePool.Acquire();
            entry.States[shotIndex]        = EShotState.Telegraphed;
            entry.SectorIndices[shotIndex] = sectorIndex;
            entry.TelegraphAt[shotIndex]   = scheduledServerTime;
            entry.Shapes[shotIndex]        = shape;
            if (shape != null)
            {
                shape.ShowRect(entry.Origin, direction, pathLength, entry.Width);
            }

            Wake();
            return true;
        }

        /// <summary> 발 하나의 발사 처리. 발사됐으면 예고가 번쩍이고, 받아들여지지 않았으면 조용히 지운다. 이미 처리한 발이면 무시한다 </summary>
        public bool FireShot(int castInstanceId, int shotIndex, bool isLaunched, double scheduledServerTime)
        {
            if ((IsServerBuild) || (!TryFind(castInstanceId, out Entry entry)))
            {
                return false;
            }

            if ((shotIndex < 0) || (shotIndex >= entry.ShotCount) || (entry.States[shotIndex] == EShotState.Fired))
            {
                return false;
            }

            entry.States[shotIndex]  = EShotState.Fired;
            entry.Launched[shotIndex] = isLaunched;
            entry.FiredAt[shotIndex] = scheduledServerTime;
            ++entry.FiredCount;

            if ((!isLaunched) || (ReadServerTime() >= (scheduledServerTime + FIRE_FLASH_SECONDS)))
            {
                ReleaseShape(ref entry.Shapes[shotIndex]);
            }

            Wake();
            return true;
        }

        /// <summary> 중단, 아직 나가지 않은 발의 예고를 바로 지운다. 이미 나간 투사체는 기존 투사체 표현이 따로 정리한다 </summary>
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

        /// <summary> 발 하나의 상태, 시험과 진단용이다. 도형이 없으면 shape는 null이다 </summary>
        public bool TryGetShot(int castInstanceId, int shotIndex, out bool isTelegraphed, out bool isFired, out BossGroundShapeVFX shape)
        {
            if (TryFind(castInstanceId, out Entry entry) && (shotIndex >= 0) && (shotIndex < entry.ShotCount))
            {
                isTelegraphed = entry.States[shotIndex] == EShotState.Telegraphed;
                isFired       = entry.States[shotIndex] == EShotState.Fired;
                shape         = entry.Shapes[shotIndex];
                return true;
            }

            isTelegraphed = false;
            isFired       = false;
            shape         = null;
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
                TickEntry(entry, now);

                if (IsFinished(entry, now))
                {
                    Remove(entry);
                }
            }
        }

        private void TickEntry(Entry entry, double now)
        {
            for (int k = 0; k < entry.ShotCount; ++k)
            {
                if (entry.States[k] == EShotState.Telegraphed)
                {
                    double end = entry.TelegraphAt[k] + entry.TelegraphSeconds;

                    // 발사 확정이 오지 않으면 예고만 남기지 않고 지운다
                    if (now >= (end + CONFIRM_GRACE_SECONDS))
                    {
                        entry.States[k] = EShotState.Expired;
                        ReleaseShape(ref entry.Shapes[k]);
                        continue;
                    }

                    if (entry.Shapes[k] != null)
                    {
                        DrawCharging(entry.Shapes[k], Ratio(now, entry.TelegraphAt[k], end), 1f);
                    }
                }
                else if ((entry.States[k] == EShotState.Fired) && (entry.Shapes[k] != null))
                {
                    float age = Mathf.Max(0f, (float)(now - entry.FiredAt[k]));
                    if (age >= FIRE_FLASH_SECONDS)
                    {
                        ReleaseShape(ref entry.Shapes[k]);
                    }
                    else
                    {
                        DrawFlash(entry.Shapes[k], age, FIRE_FLASH_SECONDS);
                    }
                }
            }
        }

        // 모든 발이 처리됐고 남은 도형이 없으면 끝이다. 처리 알림이 끝내 오지 않아도 마지막 발의 예정 시각에서 일정 시간 뒤 끝난다
        private static bool IsFinished(Entry entry, double now)
        {
            double lastFire = entry.CastStart + entry.TelegraphSeconds + ((entry.ShotCount - 1) * entry.ShotInterval);
            if (now >= (lastFire + CONFIRM_GRACE_SECONDS + FIRE_FLASH_SECONDS))
            {
                return true;
            }

            if (entry.FiredCount < entry.ShotCount)
            {
                return false;
            }

            for (int k = 0; k < entry.ShotCount; ++k)
            {
                if (entry.Shapes[k] != null)
                {
                    return false;
                }
            }

            return true;
        }

        private void Remove(Entry entry)
        {
            ReleaseShapes(entry.Shapes);
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
