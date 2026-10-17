// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 하드 2페이즈 P02, 격자 순차 빔의 표시다. 입력은 서버의 IBossGridBeamListener 인자와 같은 값이다
    // 줄은 가로 N줄이 먼저고 세로 N줄이 뒤다. 줄 하나는 (가로 여부, 축 안의 줄 번호)로 가리키며 서버가 발급한 줄 번호는 진단용으로만 둔다
    // 줄의 위치는 서버와 같은 BossBeamGeometry.Place로 놓는다. 가로줄은 월드 -X 방향, 세로줄은 월드 +Z 방향으로 뻗는다
    // 예고된 줄은 서버가 알린 예고 시각부터 타격 시각까지 차오르고, 타격이 확정되면 번쩍인 뒤 안전한 줄로 남는다. 확정이 없으면 스스로 타격하지 않는다
    // 늦게 접속하면 Begin에 예고한 줄 수와 타격한 줄 수를 넘긴다. 두 수는 복제 계층이 알림으로 세어 들고 있어야 하고, 그 줄의 시각은 시작 값의 공식으로 다시 계산한다
    /// <summary> 격자 순차 빔의 줄별 예고, 타격, 안전 전환, 취소를 시전 번호별로 관리한다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossGridBeamView : BossHardTelegraphViewBase
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int   MAX_LINES_PER_AXIS = 16;
        private const float SAFE_LINGER_SECONDS = 1f; // 마지막 줄이 타격된 뒤 안전한 줄을 더 보여 주는 시간
        private const float HALF               = 0.5f;
        private const int   NO_LINE_ID         = 0;  // 서버 줄 번호를 모른다, 공용 번호의 없음 값과 같다

        /****************************************
        *                Fields
        ****************************************/

        private enum ELineState
        {
            Idle,
            Telegraphed,
            Struck,
            Expired
        }

        private sealed class Entry
        {
            public int                  CastId;
            public Vector3              ArenaCenter;
            public float                ArenaRadius;
            public int                  LineCount;
            public float                LineWidth;
            public float                LineSpacing;
            public double               CastStart;
            public float                FirstStrike;
            public float                Interval;
            public float                AxisGap;
            public ELineState[]         States;
            public int[]                LineIds;
            public double[]             TelegraphAt;
            public double[]             StrikeAt;
            public double[]             StruckAt;
            public BossGroundShapeVFX[] Shapes;
            public int                  StruckCount;
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

        // firstLineId는 서버가 발사 순서 0번 줄에 발급한 번호다. 줄 번호가 이어서 발급되므로 복원한 줄도 서버 번호를 갖는다. 0이면 발사 순서를 번호로 쓴다
        /// <summary> 시전을 시작한다. 이미 있는 시전 번호나 끝난 번호면 무시한다. telegraphedCount와 struckCount는 발사 순서로 센 지금까지의 예고와 타격 수다 </summary>
        public bool Begin(int castInstanceId, Vector3 arenaCenter, float arenaRadius, int lineCount, float lineWidth, float lineSpacing,
            double castStartServerTime, float firstStrikeSeconds, float strikeIntervalSeconds, float axisGapSeconds,
            int telegraphedCount = 0, int struckCount = 0, int firstLineId = NO_LINE_ID)
        {
            if (IsServerBuild || IsKnown(castInstanceId))
            {
                return false;
            }

            if ((lineCount < 1) || (lineCount > MAX_LINES_PER_AXIS))
            {
                return false;
            }

            int total = lineCount * 2;
            Entry entry = new Entry
            {
                CastId      = castInstanceId,
                ArenaCenter = arenaCenter,
                ArenaRadius = arenaRadius,
                LineCount   = lineCount,
                LineWidth   = lineWidth,
                LineSpacing = lineSpacing,
                CastStart   = castStartServerTime,
                FirstStrike = firstStrikeSeconds,
                Interval    = strikeIntervalSeconds,
                AxisGap     = axisGapSeconds,
                States      = new ELineState[total],
                LineIds     = new int[total],
                TelegraphAt = new double[total],
                StrikeAt    = new double[total],
                StruckAt    = new double[total],
                Shapes      = new BossGroundShapeVFX[total]
            };

            double now = ReadServerTime();
            if (now >= (GetStrikeTime(entry, total - 1) + SAFE_LINGER_SECONDS + CONFIRM_GRACE_SECONDS))
            {
                _ended.Remember(castInstanceId);
                return false;
            }

            int struck     = Mathf.Clamp(struckCount, 0, total);
            int telegraphed = Mathf.Clamp(telegraphedCount, struck, total);
            entry.StruckCount = struck;
            for (int k = 0; k < telegraphed; ++k)
            {
                double strike = GetStrikeTime(entry, k);
                entry.StrikeAt[k]    = strike;
                entry.TelegraphAt[k] = strike - entry.FirstStrike;
                entry.LineIds[k]     = (firstLineId != NO_LINE_ID) ? (firstLineId + k) : k;
                if (k < struck)
                {
                    entry.States[k]  = ELineState.Struck;
                    entry.StruckAt[k] = strike;
                }
                else
                {
                    entry.States[k] = ELineState.Telegraphed;
                }
            }

            _entries.Add(entry);
            Wake();
            return true;
        }

        /// <summary> 줄 하나의 예고 시작. 이미 예고했거나 타격한 줄이면 무시한다. 시각은 서버가 알린 예정 시각이다 </summary>
        public bool Telegraph(int castInstanceId, int lineId, bool isHorizontal, int lineIndex, double telegraphServerTime,
            double strikeServerTime)
        {
            if ((IsServerBuild) || (!TryFind(castInstanceId, out Entry entry)) || (!TryGetSlot(entry, isHorizontal, lineIndex, out int slot)))
            {
                return false;
            }

            if (entry.States[slot] != ELineState.Idle)
            {
                return false;
            }

            entry.States[slot]      = ELineState.Telegraphed;
            entry.LineIds[slot]     = lineId;
            entry.TelegraphAt[slot] = telegraphServerTime;
            entry.StrikeAt[slot]    = strikeServerTime;
            Wake();
            return true;
        }

        /// <summary> 줄 하나의 타격 확정. 그 줄은 번쩍인 뒤 안전해진다. 예고를 못 받은 줄도 타격은 그리고, 이미 타격한 줄이면 무시한다 </summary>
        public bool Strike(int castInstanceId, int lineId, bool isHorizontal, int lineIndex, double scheduledServerTime)
        {
            if ((IsServerBuild) || (!TryFind(castInstanceId, out Entry entry)) || (!TryGetSlot(entry, isHorizontal, lineIndex, out int slot)))
            {
                return false;
            }

            if (entry.States[slot] == ELineState.Struck)
            {
                return false;
            }

            entry.States[slot]   = ELineState.Struck;
            entry.LineIds[slot]  = lineId;
            entry.StruckAt[slot] = scheduledServerTime;
            ++entry.StruckCount;
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

        /// <summary> 줄 하나의 상태, 시험과 진단용이다. 도형이 없으면 shape는 null이다 </summary>
        public bool TryGetLine(int castInstanceId, bool isHorizontal, int lineIndex, out bool isTelegraphed, out bool isStruck,
            out BossGroundShapeVFX shape)
        {
            if (TryFind(castInstanceId, out Entry entry) && TryGetSlot(entry, isHorizontal, lineIndex, out int slot))
            {
                isTelegraphed = entry.States[slot] == ELineState.Telegraphed;
                isStruck      = entry.States[slot] == ELineState.Struck;
                shape         = entry.Shapes[slot];
                return true;
            }

            isTelegraphed = false;
            isStruck      = false;
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
                if (TickEntry(entry, now))
                {
                    Remove(entry);
                }
            }
        }

        // 한 시전을 그린다, 끝났으면 참이다
        private bool TickEntry(Entry entry, double now)
        {
            int total = entry.States.Length;

            if ((!entry.EndSeen) && (entry.StruckCount >= total))
            {
                entry.EndSeen   = true;
                entry.EndSeenAt = now;
            }

            // 마지막 줄의 타격 확정이 끝내 오지 않아도 안전한 줄이 영원히 남지 않는다
            double lastStrike = GetStrikeTime(entry, total - 1);
            if ((!entry.EndSeen) && (now >= (lastStrike + CONFIRM_GRACE_SECONDS + SAFE_LINGER_SECONDS)))
            {
                entry.EndSeen   = true;
                entry.EndSeenAt = now;
            }

            float fadeAge = entry.EndSeen ? Mathf.Max(0f, (float)(now - entry.EndSeenAt - SAFE_LINGER_SECONDS)) : 0f;
            float fade    = entry.EndSeen ? (1f - Mathf.Clamp01(fadeAge / FADE_SECONDS)) : 1f;
            if (entry.EndSeen && (fadeAge >= FADE_SECONDS))
            {
                return true;
            }

            for (int slot = 0; slot < total; ++slot)
            {
                switch (entry.States[slot])
                {
                    case ELineState.Telegraphed:
                        if (now >= (entry.StrikeAt[slot] + CONFIRM_GRACE_SECONDS))
                        {
                            entry.States[slot] = ELineState.Expired;
                            ReleaseShape(ref entry.Shapes[slot]);
                            break;
                        }

                        if (EnsureShape(entry, slot))
                        {
                            DrawCharging(entry.Shapes[slot], Ratio(now, entry.TelegraphAt[slot], entry.StrikeAt[slot]), 1f);
                        }

                        break;
                    case ELineState.Struck:
                        if (EnsureShape(entry, slot))
                        {
                            float age = Mathf.Max(0f, (float)(now - entry.StruckAt[slot]));
                            if (age < FLASH_SECONDS)
                            {
                                DrawFlash(entry.Shapes[slot], age, FLASH_SECONDS);
                            }
                            else
                            {
                                DrawSafe(entry.Shapes[slot], fade);
                            }
                        }

                        break;
                    default:
                        break;
                }
            }

            return false;
        }

        // 도형이 없으면 빌려 줄의 위치에 놓는다, 풀이 모자라면 그 줄은 모델만 따라간다
        private bool EnsureShape(Entry entry, int slot)
        {
            if (entry.Shapes[slot] != null)
            {
                return true;
            }

            BossGroundShapeVFX shape = ShapePool.Acquire();
            if (shape == null)
            {
                return false;
            }

            bool isHorizontal = slot < entry.LineCount;
            int  index        = isHorizontal ? slot : (slot - entry.LineCount);
            Vector3 direction = isHorizontal ? Vector3.left : Vector3.forward;

            // 서버 BossGridBeamLayout.GetLine과 같은 인자다. Place의 offset은 줄 간격의 절반이다
            BossBeamShape line = BossBeamGeometry.Place(entry.ArenaCenter, entry.ArenaRadius, direction, index, entry.LineCount,
                entry.LineSpacing * HALF, entry.LineWidth);
            shape.ShowBand(line.ArenaCenter, line.ArenaRadius, line.Direction, line.LateralOffset, line.Width);
            entry.Shapes[slot] = shape;
            return true;
        }

        // 가로 k줄은 시전 시작 + 첫 타격 + k x 간격이고, 세로줄은 가로 마지막 줄의 타격 + 축 사이 간격 + (k - N) x 간격이다
        private static double GetStrikeTime(Entry entry, int order)
        {
            if (order < entry.LineCount)
            {
                return entry.CastStart + entry.FirstStrike + (order * entry.Interval);
            }

            double lastHorizontal = entry.CastStart + entry.FirstStrike + ((entry.LineCount - 1) * entry.Interval);
            return lastHorizontal + entry.AxisGap + ((order - entry.LineCount) * entry.Interval);
        }

        private static bool TryGetSlot(Entry entry, bool isHorizontal, int lineIndex, out int slot)
        {
            slot = isHorizontal ? lineIndex : (entry.LineCount + lineIndex);
            return (lineIndex >= 0) && (lineIndex < entry.LineCount);
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
