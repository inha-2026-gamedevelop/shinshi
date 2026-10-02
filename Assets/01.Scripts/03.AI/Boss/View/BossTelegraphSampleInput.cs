// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 네트워크 수신을 대신하는 샘플 입력이다. 서버가 보낼 알림 순서를 시각표로 흉내 내 표현 진입점만 부른다
    // 서버 AI와 판정은 실행하지 않고 타깃 추적도 하지 않는다. 방향과 길이와 시각은 모두 이 시각표가 정한 값이며 서버 확정 값이 아니다
    // 아래 시간과 크기는 월백 P01, P02, P03, P06의 현재 에셋 값을 보고 옮긴 샘플 값이라 밸런스 원본이 아니다. 시험과 눈 확인에만 쓴다
    /// <summary> 월백 네 패턴의 예고 시작, 갱신, 고정, 발동, 취소, 늦은 알림을 표현 진입점에 흘려 보내는 개발용 샘플 </summary>
    public sealed class BossTelegraphSampleInput : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    SAMPLE_BOSS_ID   = 1;
        private const int    FIRST_CAST_ID    = 1000;
        private const float  AIM_STEP_SECONDS = 0.05f;
        private const float  DEFAULT_GAP      = 1f;

        // P01 돌진
        private const string P01_ID            = "wolbaek_p01";
        private const float  DASH_TELEGRAPH    = 0.8f;
        private const float  DASH_TRACKING     = 0.4f;
        private const float  DASH_TURN_RATE    = 180f;
        private const float  DASH_ACTIVE       = 0.45f;
        private const float  DASH_RECOVERY     = 0.8f;
        private const float  DASH_RANGE        = 8f;
        private const float  DASH_WIDTH        = 2f;
        private const float  DASH_WALL_RANGE   = 5f;

        // P02 부채꼴
        private const string P02_ID            = "wolbaek_p02";
        private const float  FAN_TELEGRAPH     = 1f;
        private const float  FAN_TRACKING      = 0.5f;
        private const float  FAN_TURN_RATE     = 120f;
        private const float  FAN_RADIUS        = 6f;
        private const float  FAN_ARC           = 100f;

        // P03 원형
        private const string P03_ID            = "wolbaek_p03";
        private const float  CIRCLE_TELEGRAPH  = 1.2f;
        private const float  CIRCLE_RADIUS     = 5f;

        // P06 직선 방출
        private const string P06_ID            = "wolbaek_p06";
        private const float  LINE_TELEGRAPH    = 1.6f;
        private const float  LINE_TRACKING     = 0.6f;
        private const float  LINE_TURN_RATE    = 90f;
        private const float  LINE_LENGTH       = 14f;
        private const float  LINE_WIDTH        = 4f;
        private const float  LINE_CANCEL_AT    = 1.3f;

        private const float  SAMPLE_AIM_TARGET_DEGREES = 60f; // 샘플이 가정한 타깃 쪽 각도, 추적 한도까지만 돈다
        private const float  CANCEL_PHASE_AT   = 0.7f;
        private const float  LATE_JOIN_SECONDS = 0.5f;
        private const float  NOISE_DELAY       = 0.2f;

        /****************************************
        *                Fields
        ****************************************/

        // 시각표의 한 줄, 서버 시각에 부를 동작이다
        private struct ScheduledCall
        {
            public double Time;
            public Action Call;
        }

        [SerializeField] private BossTelegraphPresenter _presenter;
        [SerializeField] private bool _autoLoop = true;
        [SerializeField, Min(0f)] private float _loopGap = DEFAULT_GAP;

        private readonly List<ScheduledCall> _queue = new List<ScheduledCall>();

        private int    _nextCastId = FIRST_CAST_ID;
        private int    _loopStep;
        private double _loopNextAt;
        private bool   _isLoopWaiting;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 아직 부르지 않은 시각표 줄 수 </summary>
        public int PendingCalls => _queue.Count;

        /// <summary> 샘플이 만든 시전 번호 중 가장 최근 것 </summary>
        public int LastCastId => _nextCastId - 1;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            if (_presenter == null)
            {
                Debug.LogError("[BossTelegraphSample] 표현 진입점이 지정되지 않았다", this);
                enabled = false;
                return;
            }

            _presenter.SetServerClock(ReadClock);
            _loopNextAt = ReadClock();
            _isLoopWaiting = true;
        }

        private void Update()
        {
            double now = ReadClock();

            // 시각이 된 줄을 순서대로 부른다, 부르는 동안 새 줄이 더해질 수 있어 앞에서부터 하나씩 뺀다
            while ((_queue.Count > 0) && (_queue[0].Time <= now))
            {
                Action call = _queue[0].Call;
                _queue.RemoveAt(0);
                call();
            }

            if (_autoLoop && _isLoopWaiting && (now >= _loopNextAt))
            {
                StartLoopStep();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> P02 부채꼴, 추적하며 돌다가 고정하고 발동한다 </summary>
        [ContextMenu("Play P02 Fan")]
        public void PlayFan()
        {
            PlayFan(false);
        }

        /// <summary> P03 원형 </summary>
        [ContextMenu("Play P03 Circle")]
        public void PlayCircle()
        {
            PlayCircle(false);
        }

        /// <summary> P06 직선 방출 </summary>
        [ContextMenu("Play P06 Line")]
        public void PlayLine()
        {
            PlayLine(false);
        }

        /// <summary> P01 돌진, 벽이 멀어 최대 거리까지 간다 </summary>
        [ContextMenu("Play P01 Dash")]
        public void PlayDash()
        {
            PlayDash(DASH_RANGE, false);
        }

        /// <summary> P01 돌진, 벽이 가까워 짧은 길이를 표시하고 벽 앞에서 멈춘다 </summary>
        [ContextMenu("Play P01 Dash Near Wall")]
        public void PlayDashNearWall()
        {
            PlayDash(DASH_WALL_RANGE, false);
        }

        /// <summary> P06 예고 마지막 구간에 직접 제어가 성공해 취소된다 </summary>
        [ContextMenu("Play P06 Line Cancelled")]
        public void PlayLineCancelled()
        {
            PlayLine(true);
        }

        /// <summary> P02 예고 중 페이즈 전환으로 취소된다 </summary>
        [ContextMenu("Play P02 Fan Cancelled")]
        public void PlayFanCancelled()
        {
            PlayFan(true);
        }

        /// <summary> P03 예고 중 전투 종료로 취소된다 </summary>
        [ContextMenu("Play P03 Circle Cancelled")]
        public void PlayCircleCancelled()
        {
            PlayCircle(true);
        }

        /// <summary> P01 돌진 중 사망이나 전투 종료로 취소된다 </summary>
        [ContextMenu("Play P01 Dash Cancelled")]
        public void PlayDashCancelled()
        {
            PlayDash(DASH_RANGE, true);
        }

        /// <summary> 예고가 이미 절반쯤 흐른 뒤에 시작 알림을 받은 늦은 접속, 처음부터 다시 재생하지 않고 이어서 그려야 한다 </summary>
        [ContextMenu("Play P02 Late Join")]
        public void PlayLateJoin()
        {
            double now = ReadClock();
            double start = now - LATE_JOIN_SECONDS;
            int castId = NextCastId();

            _presenter.BeginTelegraph(BossTelegraphStart.Fan(SAMPLE_BOSS_ID, castId, P02_ID, transform.position, transform.forward,
                FAN_RADIUS, FAN_ARC, start, FAN_TELEGRAPH));
            _presenter.LockDirection(castId, transform.forward, FAN_RADIUS);
            Schedule(start + FAN_TELEGRAPH, () => _presenter.Strike(castId, start + FAN_TELEGRAPH));
        }

        /// <summary> 돌진이 한창일 때 스냅샷으로 복원한 접속, 머리가 이미 간 거리부터 이어서 그려야 한다 </summary>
        [ContextMenu("Play P01 Restore Mid Dash")]
        public void PlayRestoreMidDash()
        {
            double now = ReadClock();
            double dashStart = now - (DASH_ACTIVE * 0.5);
            int castId = NextCastId();
            float speed = DASH_RANGE / DASH_ACTIVE;

            BossTelegraphSnapshot snapshot = new BossTelegraphSnapshot
            {
                Start               = BossTelegraphStart.Dash(SAMPLE_BOSS_ID, castId, P01_ID, transform.position, transform.forward,
                    DASH_RANGE, DASH_WIDTH, dashStart - DASH_TELEGRAPH, DASH_TELEGRAPH),
                Direction           = transform.forward,
                Length              = DASH_RANGE,
                IsDirectionLocked   = true,
                Phase               = EBossTelegraphPhase.Execute,
                ExecuteServerTime   = dashStart,
                DashDistance        = DASH_RANGE,
                DashSeconds         = DASH_ACTIVE,
                DashSpeed           = speed,
                DashStoppedDistance = -1f
            };

            _presenter.Restore(in snapshot);
        }

        /// <summary> 취소된 뒤에 늦게 도착한 갱신과 발동과 중복 시작, 아무것도 되살아나지 않아야 한다 </summary>
        [ContextMenu("Play Late Noise After Cancel")]
        public void PlayLateNoise()
        {
            double now = ReadClock();
            int castId = NextCastId();

            _presenter.BeginTelegraph(BossTelegraphStart.Circle(SAMPLE_BOSS_ID, castId, P03_ID, transform.position,
                CIRCLE_RADIUS, now, CIRCLE_TELEGRAPH));
            Schedule(now + CANCEL_PHASE_AT, () => _presenter.Cancel(castId));
            Schedule(now + CANCEL_PHASE_AT + NOISE_DELAY, () =>
            {
                double late = ReadClock();
                _presenter.UpdateAim(castId, transform.right, CIRCLE_RADIUS, late);
                _presenter.Strike(castId, late);
                _presenter.BeginTelegraph(BossTelegraphStart.Circle(SAMPLE_BOSS_ID, castId, P03_ID, transform.position,
                    CIRCLE_RADIUS, late, CIRCLE_TELEGRAPH));
            });
        }

        private void PlayFan(bool cancel)
        {
            double start = ReadClock();
            int castId = NextCastId();

            _presenter.BeginTelegraph(BossTelegraphStart.Fan(SAMPLE_BOSS_ID, castId, P02_ID, transform.position, transform.forward,
                FAN_RADIUS, FAN_ARC, start, FAN_TELEGRAPH));
            ScheduleTracking(castId, start, FAN_TRACKING, FAN_TURN_RATE, FAN_RADIUS, null);

            if (cancel)
            {
                Schedule(start + CANCEL_PHASE_AT, () => _presenter.Cancel(castId));
                return;
            }

            Schedule(start + FAN_TELEGRAPH, () => _presenter.Strike(castId, start + FAN_TELEGRAPH));
            ScheduleFinish(castId, start + FAN_TELEGRAPH, 0f);
        }

        private void PlayCircle(bool cancel)
        {
            double start = ReadClock();
            int castId = NextCastId();

            _presenter.BeginTelegraph(BossTelegraphStart.Circle(SAMPLE_BOSS_ID, castId, P03_ID, transform.position,
                CIRCLE_RADIUS, start, CIRCLE_TELEGRAPH));

            if (cancel)
            {
                Schedule(start + CIRCLE_TELEGRAPH * 0.5, () => _presenter.Cancel(castId));
                return;
            }

            Schedule(start + CIRCLE_TELEGRAPH, () => _presenter.Strike(castId, start + CIRCLE_TELEGRAPH));
            ScheduleFinish(castId, start + CIRCLE_TELEGRAPH, 0f);
        }

        private void PlayLine(bool cancel)
        {
            double start = ReadClock();
            int castId = NextCastId();

            _presenter.BeginTelegraph(BossTelegraphStart.Line(SAMPLE_BOSS_ID, castId, P06_ID, transform.position, transform.forward,
                LINE_LENGTH, LINE_WIDTH, start, LINE_TELEGRAPH));
            ScheduleTracking(castId, start, LINE_TRACKING, LINE_TURN_RATE, LINE_LENGTH, null);

            if (cancel)
            {
                Schedule(start + LINE_CANCEL_AT, () => _presenter.Cancel(castId));
                return;
            }

            Schedule(start + LINE_TELEGRAPH, () => _presenter.Strike(castId, start + LINE_TELEGRAPH));
            ScheduleFinish(castId, start + LINE_TELEGRAPH, 0f);
        }

        // 벽이 가까우면 방향이 돌수록 갈 수 있는 거리가 달라진다, 샘플은 최대 거리까지 선형으로 늘어난다고 흉내 낸다
        private void PlayDash(float finalLength, bool cancel)
        {
            double start = ReadClock();
            int castId = NextCastId();
            float firstLength = (finalLength < DASH_RANGE) ? (finalLength * 0.6f) : finalLength;

            _presenter.BeginTelegraph(BossTelegraphStart.Dash(SAMPLE_BOSS_ID, castId, P01_ID, transform.position, transform.forward,
                firstLength, DASH_WIDTH, start, DASH_TELEGRAPH));
            ScheduleTracking(castId, start, DASH_TRACKING, DASH_TURN_RATE, finalLength, firstLength);

            double dashStart = start + DASH_TELEGRAPH;
            float speed = DASH_RANGE / DASH_ACTIVE;
            double stopAt = dashStart + (finalLength / speed);

            Schedule(dashStart, () => _presenter.BeginDash(castId, finalLength, DASH_ACTIVE, speed, dashStart));

            if (cancel)
            {
                Schedule(dashStart + (DASH_ACTIVE * 0.4), () => _presenter.Cancel(castId));
                return;
            }

            Vector3 stopPosition = transform.position + (Quaternion.AngleAxis(AimDegrees(DASH_TRACKING, DASH_TURN_RATE), Vector3.up)
                * transform.forward * finalLength);
            Schedule(stopAt, () => _presenter.StopDash(castId, stopPosition, finalLength < DASH_RANGE));
            ScheduleFinish(castId, dashStart + DASH_ACTIVE, DASH_RECOVERY);
        }

        // 추적 구간 동안 일정 간격으로 방향 갱신을 보내고 끝에 고정을 보낸다. 서버의 OnAimChanged와 OnDirectionLocked 순서와 같다
        // startLength가 있으면 길이도 함께 늘려 보낸다
        private void ScheduleTracking(int castId, double start, float trackingSeconds, float turnRate, float finalLength, float? startLength)
        {
            int steps = Mathf.Max(1, Mathf.RoundToInt(trackingSeconds / AIM_STEP_SECONDS));
            for (int i = 1; i <= steps; ++i)
            {
                float t = trackingSeconds * i / steps;
                float degrees = AimDegrees(t, turnRate);
                float length = startLength.HasValue ? Mathf.Lerp(startLength.Value, finalLength, (float)i / steps) : finalLength;
                Vector3 direction = Quaternion.AngleAxis(degrees, Vector3.up) * transform.forward;
                double at = start + t;

                if (i < steps)
                {
                    Schedule(at, () => _presenter.UpdateAim(castId, direction, length, at));
                }
                else
                {
                    Schedule(at, () => _presenter.LockDirection(castId, direction, length));
                }
            }
        }

        private void ScheduleFinish(int castId, double executeAt, float recovery)
        {
            Schedule(executeAt + recovery + NOISE_DELAY, () => _presenter.Finish(castId));
        }

        private static float AimDegrees(float elapsedSeconds, float turnRate)
        {
            return Mathf.Min(SAMPLE_AIM_TARGET_DEGREES, elapsedSeconds * turnRate);
        }

        private void Schedule(double time, Action call)
        {
            int index = _queue.Count;
            while ((index > 0) && (_queue[index - 1].Time > time))
            {
                --index;
            }

            _queue.Insert(index, new ScheduledCall { Time = time, Call = call });
        }

        // 자동 반복은 네 패턴과 취소 변형을 차례로 돌린다. 한 패턴이 끝난 뒤 간격을 두고 다음으로 넘어가 풀 재사용을 눈으로 본다
        private void StartLoopStep()
        {
            _isLoopWaiting = false;

            switch (_loopStep % 8)
            {
                case 0: PlayDash(); break;
                case 1: PlayFan(); break;
                case 2: PlayCircle(); break;
                case 3: PlayLine(); break;
                case 4: PlayDashNearWall(); break;
                case 5: PlayFanCancelled(); break;
                case 6: PlayLineCancelled(); break;
                default: PlayDashCancelled(); break;
            }

            ++_loopStep;
            double settle = ReadClock() + 3.5;
            Schedule(settle, () =>
            {
                _loopNextAt = ReadClock() + _loopGap;
                _isLoopWaiting = true;
            });
        }

        private int NextCastId()
        {
            int id = _nextCastId;
            ++_nextCastId;
            return id;
        }

        private static double ReadClock()
        {
            return Time.timeAsDouble;
        }
    }
}
