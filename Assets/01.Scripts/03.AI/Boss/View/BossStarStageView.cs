// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 하드 2페이즈 P01, 단계형 별 모양 빔의 표시다. 입력은 서버의 IBossStarStageListener 인자와 같은 값이다
    // 단계 k의 별은 기준 방향에서 오른쪽으로 stepDegrees x k 돈 모습이고, 그 단계의 위치 예고는 이전 단계의 타격 시각(첫 단계는 시전 시작)부터 자기 타격 시각까지다
    // 타격은 서버가 알린 단계만 번쩍이고, 확정이 없으면 클라이언트가 스스로 타격하지 않는다. 다음 단계의 예고는 이전 단계의 타격 확정과 함께 시작한다
    // 늦게 접속하면 Begin에 이미 타격된 단계 수를 넘긴다. 이 수는 복제 계층이 타격 알림으로 세어 들고 있어야 한다
    /// <summary> 단계형 별 빔의 예고, 단계 타격, 취소를 시전 번호별로 관리한다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossStarStageView : BossHardTelegraphViewBase
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int   MIN_VERTICES = 3;
        private const int   MAX_VERTICES = 8; // 별형 빔 표현의 꼭짓점 상한과 같다
        private const float FULL_TURN    = 360f;
        private const int   NO_STAGE     = -1;

        /****************************************
        *                Fields
        ****************************************/

        private sealed class Entry
        {
            public int                  CastId;
            public int                  StarId;
            public BossStarShape        Shape;
            public float                StepDegrees;
            public int                  StageCount;
            public double               CastStart;
            public float                FirstStrike;
            public float                Interval;
            public int                  StruckCount;
            public int                  ChargingStage = NO_STAGE;
            public float                Charge;
            public float                Rotation;
            public BossGroundShapeVFX[] Charging = new BossGroundShapeVFX[MAX_VERTICES];
            public BossGroundShapeVFX[] Flash    = new BossGroundShapeVFX[MAX_VERTICES];
            public bool                 HasFlash;
            public double               FlashAt;
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

        /// <summary> 시전을 시작한다. 이미 있는 시전 번호나 끝난 번호면 무시한다. struckCount는 이미 타격된 단계 수다 </summary>
        public bool Begin(int castInstanceId, int starId, in BossStarShape shape, float stepDegrees, int stageCount,
            double castStartServerTime, float firstStrikeSeconds, float strikeIntervalSeconds, int struckCount = 0)
        {
            if (IsServerBuild || IsKnown(castInstanceId))
            {
                return false;
            }

            if ((stageCount < 1) || (shape.VertexCount < MIN_VERTICES) || (shape.ConnectStep < 1))
            {
                return false;
            }

            Entry entry = new Entry
            {
                CastId      = castInstanceId,
                StarId      = starId,
                Shape       = shape,
                StepDegrees = stepDegrees,
                StageCount  = stageCount,
                CastStart   = castStartServerTime,
                FirstStrike = firstStrikeSeconds,
                Interval    = strikeIntervalSeconds,
                StruckCount = Mathf.Clamp(struckCount, 0, stageCount)
            };

            // 모든 단계가 끝났거나 확정을 기다릴 시간까지 지난 시전은 그리지 않고 늦게 온 알림이 되살리지도 못한다
            if ((entry.StruckCount >= stageCount)
                || (ReadServerTime() >= (GetStrikeTime(entry, entry.StruckCount) + CONFIRM_GRACE_SECONDS)))
            {
                _ended.Remember(castInstanceId);
                return false;
            }

            _entries.Add(entry);
            Wake();
            return true;
        }

        /// <summary> 단계 타격 확정. 이미 반영한 단계나 모르는 시전이면 무시하고, 건너뛴 단계가 있으면 따라잡는다 </summary>
        public bool Struck(int castInstanceId, int stageIndex, float rotationDegrees, double scheduledServerTime)
        {
            if ((IsServerBuild) || (!TryFind(castInstanceId, out Entry entry)))
            {
                return false;
            }

            if ((stageIndex < entry.StruckCount) || (stageIndex >= entry.StageCount))
            {
                return false;
            }

            ReleaseShapes(entry.Charging);
            ReleaseShapes(entry.Flash);
            entry.ChargingStage = NO_STAGE;
            entry.StruckCount   = stageIndex + 1;
            entry.HasFlash      = false;

            // 이미 번쩍임이 끝난 시각의 늦은 알림은 그리지 않는다
            if (ReadServerTime() < (scheduledServerTime + FLASH_SECONDS))
            {
                PlaceBeams(entry, entry.Flash, rotationDegrees);
                entry.HasFlash = true;
                entry.FlashAt  = scheduledServerTime;
            }

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
        public bool TryGetState(int castInstanceId, out int struckCount, out float charge, out float rotationDegrees,
            out int chargingBeamCount, out int flashBeamCount)
        {
            if (TryFind(castInstanceId, out Entry entry))
            {
                struckCount       = entry.StruckCount;
                charge            = entry.Charge;
                rotationDegrees   = entry.Rotation;
                chargingBeamCount = CountShapes(entry.Charging);
                flashBeamCount    = CountShapes(entry.Flash);
                return true;
            }

            struckCount       = 0;
            charge            = 0f;
            rotationDegrees   = 0f;
            chargingBeamCount = 0;
            flashBeamCount    = 0;
            return false;
        }

        /// <summary> 예고 중인 빔 도형, 시험과 진단용이다. 없으면 null </summary>
        public BossGroundShapeVFX GetChargingBeam(int castInstanceId, int beamIndex)
        {
            return (TryFind(castInstanceId, out Entry entry) && (beamIndex >= 0) && (beamIndex < entry.Charging.Length))
                ? entry.Charging[beamIndex]
                : null;
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

                if (entry.HasFlash)
                {
                    TickFlash(entry, now);
                }

                if (entry.StruckCount >= entry.StageCount)
                {
                    if (!entry.HasFlash)
                    {
                        Remove(entry);
                    }

                    continue;
                }

                int    stage  = entry.StruckCount;
                double strike = GetStrikeTime(entry, stage);

                // 서버 확정이 오지 않으면 예고만 남기지 않고 지운다
                if (now >= (strike + CONFIRM_GRACE_SECONDS))
                {
                    Remove(entry);
                    continue;
                }

                if (entry.ChargingStage != stage)
                {
                    entry.ChargingStage = stage;
                    entry.Rotation      = entry.StepDegrees * stage;
                    ReleaseShapes(entry.Charging);
                    PlaceBeams(entry, entry.Charging, entry.Rotation);
                }

                double from = (stage == 0) ? entry.CastStart : GetStrikeTime(entry, stage - 1);
                entry.Charge = Ratio(now, from, strike);
                for (int k = 0; k < entry.Charging.Length; ++k)
                {
                    if (entry.Charging[k] != null)
                    {
                        DrawCharging(entry.Charging[k], entry.Charge, 1f);
                    }
                }
            }
        }

        private void TickFlash(Entry entry, double now)
        {
            float age = Mathf.Max(0f, (float)(now - entry.FlashAt));
            if (age >= FLASH_SECONDS)
            {
                ReleaseShapes(entry.Flash);
                entry.HasFlash = false;
                return;
            }

            for (int k = 0; k < entry.Flash.Length; ++k)
            {
                if (entry.Flash[k] != null)
                {
                    DrawFlash(entry.Flash[k], age, FLASH_SECONDS);
                }
            }
        }

        // 단계 k의 타격 시각은 시전 시작 + 첫 타격 + k x 간격이다
        private static double GetStrikeTime(Entry entry, int stage)
        {
            return entry.CastStart + entry.FirstStrike + (stage * entry.Interval);
        }

        // 꼭짓점 i는 기준 방향에서 오른쪽으로 i x (360 / 개수)도, 빔 i는 꼭짓점 i와 (i + 연결 간격) 꼭짓점 사이다
        private void PlaceBeams(Entry entry, BossGroundShapeVFX[] beams, float rotationDegrees)
        {
            BossStarShape shape = entry.Shape;
            int           count = Mathf.Min(shape.VertexCount, MAX_VERTICES);

            Vector3 baseDirection = shape.BaseDirection;
            baseDirection.y = 0f;
            baseDirection   = (baseDirection.sqrMagnitude > (MIN_SPAN * MIN_SPAN)) ? baseDirection.normalized : Vector3.forward;

            for (int i = 0; i < count; ++i)
            {
                Vector3 start = GetVertex(shape, baseDirection, rotationDegrees, i, count);
                Vector3 end   = GetVertex(shape, baseDirection, rotationDegrees, (i + shape.ConnectStep) % count, count);
                Vector3 axis  = end - start;
                float   length = axis.magnitude;
                if (length < MIN_SPAN)
                {
                    continue;
                }

                BossGroundShapeVFX beam = ShapePool.Acquire();
                if (beam == null)
                {
                    continue;
                }

                beam.ShowRect(start, axis / length, length, shape.BeamWidth);
                beams[i] = beam;
            }
        }

        private static Vector3 GetVertex(BossStarShape shape, Vector3 baseDirection, float rotationDegrees, int index, int count)
        {
            float angle = rotationDegrees + (index * (FULL_TURN / count));
            return shape.ArenaCenter + ((Quaternion.AngleAxis(angle, Vector3.up) * baseDirection) * shape.RingRadius);
        }

        private static int CountShapes(BossGroundShapeVFX[] shapes)
        {
            int count = 0;
            for (int i = 0; i < shapes.Length; ++i)
            {
                count += (shapes[i] != null) ? 1 : 0;
            }

            return count;
        }

        private void Remove(Entry entry)
        {
            ReleaseShapes(entry.Charging);
            ReleaseShapes(entry.Flash);
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
