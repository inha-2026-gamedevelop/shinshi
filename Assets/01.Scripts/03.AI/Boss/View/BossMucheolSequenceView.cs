// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 무철 2페이즈 순차 타격(P01, P02, P04, P05)의 표시다. 타마다 형태(부채꼴, 직사각형, 원형)와 조준 방향에서 돌린 오프셋이 달라
    // 타마다 예고 하나씩 그린다. 타 i는 예고 시간 + 그 타의 지연이 지난 시각에 확정된다. 원형은 조준 방향과 오프셋을 쓰지 않는다
    // 입력은 서버의 IMucheolSequenceTelegraphListener 인자에서 클라이언트 안전한 값만 옮긴 것이다. 타 목록은 형태, 사거리, 부채꼴 각도, 폭, 오프셋, 지연의
    // 같은 길이 목록으로 받고 형태는 클라이언트 예고 형태(EBossTelegraphShape의 Fan, Line, Circle)로 옮겨 넘긴다
    // 오프셋은 위에서 볼 때 시계 방향이 양수다. 타격은 서버가 알린 타만 터뜨리고, 늦게 접속하면 원래 시작 서버 시각과 확정된 타 수로 Begin을 부른다
    /// <summary> 순차 타격의 타별 예고, 조준 추적, 방향 고정, 타 확정, 취소를 시전 번호별로 관리한다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossMucheolSequenceView : BossPresenterViewBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private sealed class Cast
        {
            public EBossTelegraphShape[] Shapes;
            public float[]               Ranges;
            public float[]               AimOffsets;
            public double[]              StrikeTimes;
            public int                   StepCount;
            public int                   StruckCount;
        }

        private readonly Dictionary<int, Cast> _casts = new Dictionary<int, Cast>();

        /****************************************
        *              Properties
        ****************************************/

        public int CastCount => _casts.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 순차 타격 예고를 시작한다. 이미 있는 시전이거나 취소된 시전이면 무시한다. 지원하지 않는 형태가 있으면 시작하지 않는다 </summary>
        public bool Begin(int bossId, int castInstanceId, string patternId, Vector3 origin, Vector3 direction, double startServerTime,
            float telegraphSeconds, IReadOnlyList<EBossTelegraphShape> shapes, IReadOnlyList<float> ranges, IReadOnlyList<float> arcAngles,
            IReadOnlyList<float> widths, IReadOnlyList<float> aimOffsetDegrees, IReadOnlyList<float> delaySeconds, int struckCount = 0)
        {
            if (IsServerBuild || _casts.ContainsKey(castInstanceId))
            {
                return false;
            }

            if (!IsValidInput(shapes, ranges, arcAngles, widths, aimOffsetDegrees, delaySeconds))
            {
                return false;
            }

            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter == null)
            {
                return false;
            }

            int count = shapes.Count;
            Cast cast = new Cast
            {
                Shapes      = new EBossTelegraphShape[count],
                Ranges      = new float[count],
                AimOffsets  = new float[count],
                StrikeTimes = new double[count],
                StepCount   = count
            };

            bool any = false;
            for (int i = 0; i < count; ++i)
            {
                float seconds = telegraphSeconds + delaySeconds[i];
                cast.Shapes[i]      = shapes[i];
                cast.Ranges[i]      = ranges[i];
                cast.AimOffsets[i]  = aimOffsetDegrees[i];
                cast.StrikeTimes[i] = startServerTime + seconds;

                int key = Keys.Get(castInstanceId, i);
                Vector3 stepDirection = StepDirection(direction, shapes[i], aimOffsetDegrees[i]);
                BossTelegraphStart start;
                if (shapes[i] == EBossTelegraphShape.Fan)
                {
                    start = BossTelegraphStart.Fan(bossId, key, patternId, origin, stepDirection, ranges[i], arcAngles[i], startServerTime,
                        seconds, castInstanceId);
                }
                else if (shapes[i] == EBossTelegraphShape.Line)
                {
                    start = new BossTelegraphStart(bossId, key, castInstanceId, patternId, EBossTelegraphShape.Line, origin, stepDirection,
                        ranges[i], widths[i], 0f, startServerTime, seconds);
                }
                else
                {
                    start = BossTelegraphStart.Circle(bossId, key, patternId, origin, ranges[i], startServerTime, seconds, castInstanceId);
                }

                any |= presenter.BeginTelegraph(in start);
            }

            // 취소됐거나 모두 지난 시전이라 예고 하나도 받아들이지 않았으면 기록을 남기지 않는다
            if (!any)
            {
                ForgetCast(castInstanceId);
                return false;
            }

            _casts.Add(castInstanceId, cast);
            TrackGroup(castInstanceId);

            int confirmed = Mathf.Clamp(struckCount, 0, count);
            for (int i = 0; i < confirmed; ++i)
            {
                Hit(castInstanceId, i, cast.StrikeTimes[i]);
            }

            return any;
        }

        /// <summary> 추적 중 조준 방향이 바뀌었다, 타별 방향은 이 방향에 오프셋을 더한 것이다 </summary>
        public bool AimChanged(int castInstanceId, Vector3 direction, double serverTime)
        {
            if ((IsServerBuild) || (!_casts.TryGetValue(castInstanceId, out Cast cast)) || (Presenter == null))
            {
                return false;
            }

            bool any = false;
            for (int i = 0; i < cast.StepCount; ++i)
            {
                if ((cast.Shapes[i] != EBossTelegraphShape.Circle) && (Keys.TryGet(castInstanceId, i, out int key)))
                {
                    any |= Presenter.UpdateAim(key, StepDirection(direction, cast.Shapes[i], cast.AimOffsets[i]), cast.Ranges[i], serverTime);
                }
            }

            return any;
        }

        /// <summary> 방향이 고정됐다 </summary>
        public bool DirectionLocked(int castInstanceId, Vector3 direction)
        {
            if ((IsServerBuild) || (!_casts.TryGetValue(castInstanceId, out Cast cast)) || (Presenter == null))
            {
                return false;
            }

            bool any = false;
            for (int i = 0; i < cast.StepCount; ++i)
            {
                if ((cast.Shapes[i] != EBossTelegraphShape.Circle) && (Keys.TryGet(castInstanceId, i, out int key)))
                {
                    any |= Presenter.LockDirection(key, StepDirection(direction, cast.Shapes[i], cast.AimOffsets[i]), cast.Ranges[i]);
                }
            }

            return any;
        }

        /// <summary> stepIndex번째 타가 확정됐다. 이미 확정된 타나 모르는 타면 무시한다 </summary>
        public bool StepHit(int castInstanceId, int stepIndex, double serverTime)
        {
            return Hit(castInstanceId, stepIndex, serverTime);
        }

        /// <summary> 중단, 아직 확정되지 않은 타의 예고를 모두 지운다. 이미 확정된 타는 연출을 마친다 </summary>
        public bool Cancel(int castInstanceId)
        {
            if (IsServerBuild)
            {
                return false;
            }

            _casts.Remove(castInstanceId);
            return CancelCast(castInstanceId);
        }

        /// <summary> 한 타의 예고 표현 번호, 시험과 진단용이다 </summary>
        public bool TryGetKey(int castInstanceId, int stepIndex, out int key)
        {
            return Keys.TryGet(castInstanceId, stepIndex, out key);
        }

        public override void ClearAll()
        {
            _casts.Clear();
            base.ClearAll();
        }

        private bool Hit(int castInstanceId, int stepIndex, double serverTime)
        {
            if ((IsServerBuild) || (!_casts.TryGetValue(castInstanceId, out Cast cast)) || (Presenter == null))
            {
                return false;
            }

            if ((stepIndex < 0) || (stepIndex >= cast.StepCount) || (!Keys.TryGet(castInstanceId, stepIndex, out int key)))
            {
                return false;
            }

            bool struck = Presenter.Strike(key, serverTime);
            if (struck)
            {
                ++cast.StruckCount;
            }

            if (cast.StruckCount >= cast.StepCount)
            {
                _casts.Remove(castInstanceId);
                ForgetCast(castInstanceId);
            }

            return struck;
        }

        // 부채꼴과 직사각형은 조준 방향에서 오프셋만큼 돌린 방향이고 원형은 방향이 없다
        private static Vector3 StepDirection(Vector3 aim, EBossTelegraphShape shape, float offsetDegrees)
        {
            return (shape == EBossTelegraphShape.Circle) ? aim : (Quaternion.AngleAxis(offsetDegrees, Vector3.up) * aim);
        }

        private static bool IsValidInput(IReadOnlyList<EBossTelegraphShape> shapes, IReadOnlyList<float> ranges, IReadOnlyList<float> arcAngles,
            IReadOnlyList<float> widths, IReadOnlyList<float> aimOffsets, IReadOnlyList<float> delays)
        {
            if ((shapes == null) || (ranges == null) || (arcAngles == null) || (widths == null) || (aimOffsets == null) || (delays == null)
                || (shapes.Count < 1))
            {
                return false;
            }

            int count = shapes.Count;
            if ((ranges.Count != count) || (arcAngles.Count != count) || (widths.Count != count) || (aimOffsets.Count != count)
                || (delays.Count != count))
            {
                return false;
            }

            for (int i = 0; i < count; ++i)
            {
                bool supported = (shapes[i] == EBossTelegraphShape.Fan) || (shapes[i] == EBossTelegraphShape.Line)
                    || (shapes[i] == EBossTelegraphShape.Circle);
                if (!supported)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
