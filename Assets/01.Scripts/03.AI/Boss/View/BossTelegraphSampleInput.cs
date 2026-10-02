// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 네트워크 수신을 대신하는 샘플 입력이다. 서버가 보낼 알림 순서를 시각표로 흉내 내 표현 진입점만 부른다
    // 서버 AI와 판정은 실행하지 않고 타깃 추적도 하지 않는다. 방향과 길이와 시각은 모두 이 시각표가 정한 값이며 서버 확정 값이 아니다
    // 아래 시간과 크기는 월백 P01~P06의 현재 에셋 값을 보고 옮긴 샘플 값이라 밸런스 원본이 아니다. 시험과 눈 확인에만 쓴다
    /// <summary> 월백 여섯 패턴의 예고 시작, 갱신, 고정, 발동, 취소, 늦은 알림을 표현 진입점에 흘려 보내는 개발용 샘플 </summary>
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

        // 2페이즈 P06 원형 폭발, 1페이즈 P03과 같은 원형 예고를 쓰고 ID와 회복만 다르다
        private const string PHASE_TWO_CIRCLE_ID       = Shinshi.Data.BossPatternCatalogSO.WOLBAEK_P2_P06_ID;
        private const float  PHASE_TWO_CIRCLE_RECOVERY = 0.8f;

        // P06 직선 방출
        private const string P06_ID            = "wolbaek_p06";
        private const float  LINE_TELEGRAPH    = 1.6f;
        private const float  LINE_TRACKING     = 0.6f;
        private const float  LINE_TURN_RATE    = 90f;
        private const float  LINE_LENGTH       = 14f;
        private const float  LINE_WIDTH        = 4f;
        private const float  LINE_CANCEL_AT    = 1.3f;

        // P04 투사체
        private const string P04_ID            = "wolbaek_p04";
        private const float  PROJ_TELEGRAPH    = 0.9f;
        private const float  PROJ_TRACKING     = 0.45f;
        private const float  PROJ_TURN_RATE    = 120f;
        private const float  PROJ_RECOVERY     = 0.5f;
        private const float  PROJ_RANGE        = 35f;
        private const float  PROJ_WIDTH        = 1f;
        private const float  PROJ_SPEED        = 10f;
        private const float  PROJ_WALL_PATH    = 12f;
        private const float  PROJ_HIT_DISTANCE = 14f;
        private const float  PROJ_CANCEL_FLIGHT = 0.8f;
        private const int    LOOP_STEP_COUNT   = 21;

        // 2페이즈 P04 지정 장판, 한 시전이 장판 두 개를 동시에 설치한다. 1페이즈 P05와 같은 예고와 장판 값을 쓴다
        private const string PHASE_TWO_AREA_ID    = Shinshi.Data.BossPatternCatalogSO.WOLBAEK_P2_P04_ID;

        // 2페이즈 P05 순차 투사체, 예고 하나와 같은 방향의 독립된 세 발이다. 1페이즈 P04와 같은 예고와 비행 값을 쓴다
        private const string VOLLEY_ID             = Shinshi.Data.BossPatternCatalogSO.WOLBAEK_P2_P05_ID;
        private const int    VOLLEY_COUNT          = 3;
        private const float  VOLLEY_INTERVAL       = 0.3f;
        private const float  VOLLEY_RECOVERY       = 0.5f;
        private const float  VOLLEY_HIT_DISTANCE   = 4f;   // 개별 소멸 샘플에서 첫 발이 맞는 거리
        private const float  VOLLEY_CANCEL_AFTER   = 0.15f; // 둘째 발을 쏜 뒤 중간 취소가 오는 시간
        private const float  VOLLEY_LATE_SECONDS   = 0.5f; // 늦은 발사 알림이 발사 시각보다 늦게 도착하는 시간
        private const float  VOLLEY_RESTORE_STEP   = 0.3f; // 다중 복원 샘플에서 발 사이 발사 시각 차이
        private const float  VOLLEY_RESTORE_BASE   = 0.6f; // 다중 복원 샘플에서 가장 늦은 발이 이미 난 시간

        // P05 장판
        private const string P05_ID            = "wolbaek_p05";
        private const float  AREA_TELEGRAPH    = 1f;
        private const float  AREA_RADIUS       = 3f;
        private const float  AREA_DURATION     = 6f;
        private const float  AREA_INTERVAL     = 1f;
        private const float  AREA_RECOVERY     = 0.5f;
        private const float  AREA_DISTANCE     = 5f;   // 샘플 장판이 보스 앞 이만큼에 설치된다
        private const float  AREA_SPREAD       = 4f;   // 동시 장판 샘플의 좌우 간격
        private const float  AREA_STAGGER      = 1.5f; // 예고 1초 + 회복 0.5초마다 다음 시전이 시작한다
        private const float  AREA_CANCEL_AT    = 2.5f; // 활성 뒤 이 시점에 사망으로 제거된다
        private const float  AREA_LATE_SECONDS = 2.5f;
        private const int    AREA_RESTORE_TICK = 3;

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

        /// <summary> 2페이즈 P06 원형 폭발, 예고 1.2초 반경 5m, 서버 확정이 오면 터진다 </summary>
        [ContextMenu("Play Phase2 P06 Circle")]
        public void PlayPhaseTwoCircle()
        {
            PlayCircle(PHASE_TWO_CIRCLE_ID, PHASE_TWO_CIRCLE_RECOVERY, false);
        }

        /// <summary> 2페이즈 P06 예고 중 사망이나 페이즈 전환이나 전투 종료로 취소된다 </summary>
        [ContextMenu("Play Phase2 P06 Circle Cancelled")]
        public void PlayPhaseTwoCircleCancelled()
        {
            PlayCircle(PHASE_TWO_CIRCLE_ID, PHASE_TWO_CIRCLE_RECOVERY, true);
        }

        /// <summary> 2페이즈 P05 순차 투사체, 예고 하나 뒤 같은 방향으로 세 발이 0.3초 간격으로 나가 각자 날아간다 </summary>
        [ContextMenu("Play Phase2 P05 Volley")]
        public void PlayPhaseTwoVolley()
        {
            PlayVolley(false, false, false);
        }

        /// <summary> 2페이즈 P05 첫 발만 가까운 대상에 맞아 사라지고 나머지 두 발은 계속 난다 </summary>
        [ContextMenu("Play Phase2 P05 Volley One Hit")]
        public void PlayPhaseTwoVolleyOneHit()
        {
            PlayVolley(true, false, false);
        }

        /// <summary> 2페이즈 P05 둘째 발을 쏜 직후 사망이나 전투 종료로 날고 있는 발이 제거되고 셋째 발은 나가지 않는다 </summary>
        [ContextMenu("Play Phase2 P05 Volley Cancelled")]
        public void PlayPhaseTwoVolleyCancelled()
        {
            PlayVolley(false, true, false);
        }

        /// <summary> 2페이즈 P05 둘째 발의 발사 알림만 늦게 도착한다, 발사 시각 기준으로 이어서 그려야 한다 </summary>
        [ContextMenu("Play Phase2 P05 Volley Late Launch")]
        public void PlayPhaseTwoVolleyLateLaunch()
        {
            PlayVolley(false, false, true);
        }

        /// <summary> 2페이즈 P05 세 발이 비행 중일 때 스냅샷 셋으로 복원한 접속, 발마다 서버 시각 기준 위치부터 이어서 그려야 한다 </summary>
        [ContextMenu("Play Phase2 P05 Volley Restore")]
        public void PlayPhaseTwoVolleyRestore()
        {
            double now = ReadClock();
            int castId = NextCastId();
            Vector3 origin = transform.position;
            Vector3 direction = Quaternion.AngleAxis(AimDegrees(PROJ_TRACKING, PROJ_TURN_RATE), Vector3.up) * transform.forward;

            for (int i = 0; i < VOLLEY_COUNT; ++i)
            {
                int projectileId = NextCastId();
                double launch = now - VOLLEY_RESTORE_BASE - ((VOLLEY_COUNT - 1 - i) * (double)VOLLEY_RESTORE_STEP);
                BossProjectileSnapshot snapshot = new BossProjectileSnapshot
                {
                    Start = new BossProjectileStart(SAMPLE_BOSS_ID, projectileId, castId, origin, direction, PROJ_SPEED, PROJ_WIDTH,
                        PROJ_RANGE, launch),
                    Fate = EBossProjectileFate.Flying
                };

                _presenter.RestoreProjectile(in snapshot);
                Vector3 endPosition = origin + (direction * PROJ_RANGE);
                Schedule(launch + (PROJ_RANGE / PROJ_SPEED), () => _presenter.EndProjectile(projectileId, EBossProjectileFate.Range, endPosition));
            }
        }

        // 예고 하나와 서버가 한 발씩 확정하는 독립된 세 발을 서버 알림 순서대로 흘린다. 클라이언트가 세 발을 스스로 만들지 않고 알림마다 한 발이다
        private void PlayVolley(bool firstHits, bool cancelMidSequence, bool lateSecond)
        {
            double start = ReadClock();
            int castId = NextCastId();

            _presenter.BeginTelegraph(BossTelegraphStart.Projectile(SAMPLE_BOSS_ID, castId, VOLLEY_ID, transform.position,
                transform.forward, PROJ_RANGE, PROJ_WIDTH, start, PROJ_TELEGRAPH));
            ScheduleTracking(castId, start, PROJ_TRACKING, PROJ_TURN_RATE, PROJ_RANGE, null);

            double firstLaunch = start + PROJ_TELEGRAPH;
            Vector3 direction = Quaternion.AngleAxis(AimDegrees(PROJ_TRACKING, PROJ_TURN_RATE), Vector3.up) * transform.forward;
            Vector3 origin = transform.position;
            int shots = cancelMidSequence ? (VOLLEY_COUNT - 1) : VOLLEY_COUNT;
            int[] ids = new int[shots];

            for (int i = 0; i < shots; ++i)
            {
                int projectileId = NextCastId();
                ids[i] = projectileId;
                double launch = firstLaunch + (i * (double)VOLLEY_INTERVAL);
                BossProjectileStart shot = new BossProjectileStart(SAMPLE_BOSS_ID, projectileId, castId, origin, direction,
                    PROJ_SPEED, PROJ_WIDTH, PROJ_RANGE, launch);

                double arrive = (lateSecond && (i == 1)) ? (launch + VOLLEY_LATE_SECONDS) : launch;
                Schedule(arrive, () => _presenter.LaunchProjectile(in shot));

                if (cancelMidSequence)
                {
                    continue;
                }

                bool hit = firstHits && (i == 0);
                float endDistance = hit ? VOLLEY_HIT_DISTANCE : PROJ_RANGE;
                EBossProjectileFate fate = hit ? EBossProjectileFate.Hit : EBossProjectileFate.Range;
                Schedule(launch + (endDistance / PROJ_SPEED), () => _presenter.EndProjectile(projectileId, fate, origin + (direction * endDistance)));
            }

            if (cancelMidSequence)
            {
                double cancelAt = firstLaunch + VOLLEY_INTERVAL + VOLLEY_CANCEL_AFTER;
                for (int i = 0; i < shots; ++i)
                {
                    int projectileId = ids[i];
                    Schedule(cancelAt, () => _presenter.CancelProjectile(projectileId));
                }
            }

            ScheduleFinish(castId, firstLaunch + ((VOLLEY_COUNT - 1) * (double)VOLLEY_INTERVAL), VOLLEY_RECOVERY);
        }

        /// <summary> 2페이즈 P04 지정 장판, 한 시전이 예고 둘을 동시에 보이고 서버 활성 확정마다 장판이 따로 생긴다 </summary>
        [ContextMenu("Play Phase2 P04 Two Areas")]
        public void PlayPhaseTwoAreas()
        {
            PlayAreaPair(EAreaPairMode.Pair);
        }

        /// <summary> 2페이즈 P04 두 장판이 완전히 겹친다, 표현은 각 장판을 따로 그리고 피해 합산은 서버가 한다 </summary>
        [ContextMenu("Play Phase2 P04 Overlapping Areas")]
        public void PlayPhaseTwoAreasOverlap()
        {
            PlayAreaPair(EAreaPairMode.Overlap);
        }

        /// <summary> 2페이즈 P04 빈 슬롯이 하나라 장판 하나만 설치된다 </summary>
        [ContextMenu("Play Phase2 P04 Single Area")]
        public void PlayPhaseTwoAreaSingle()
        {
            PlayAreaPair(EAreaPairMode.Single);
        }

        /// <summary> 2페이즈 P04 장판 하나만 중간에 끝나고 다른 장판은 남아 끝까지 간다 </summary>
        [ContextMenu("Play Phase2 P04 One Area Ends")]
        public void PlayPhaseTwoAreaOneEnds()
        {
            PlayAreaPair(EAreaPairMode.OneEnds);
        }

        /// <summary> 2페이즈 P04 활성 중 사망이나 전투 종료로 두 장판이 모두 제거된다 </summary>
        [ContextMenu("Play Phase2 P04 Areas Cancelled")]
        public void PlayPhaseTwoAreasCancelled()
        {
            PlayAreaPair(EAreaPairMode.AllCancelled);
        }

        /// <summary> 2페이즈 P04 둘째 장판의 활성 알림만 2.5초 늦게 도착한다, 활성 시각 기준으로 이어서 그려야 한다 </summary>
        [ContextMenu("Play Phase2 P04 Late Activation")]
        public void PlayPhaseTwoAreasLateActivation()
        {
            PlayAreaPair(EAreaPairMode.LateSecond);
        }

        /// <summary> 2페이즈 P04 두 장판이 활성 중일 때 스냅샷 둘로 복원한 접속, 장판마다 이미 표시한 회차를 다시 표시하지 않는다 </summary>
        [ContextMenu("Play Phase2 P04 Areas Restore")]
        public void PlayPhaseTwoAreasRestore()
        {
            double now = ReadClock();
            int castId = NextCastId();
            for (int i = 0; i < 2; ++i)
            {
                int areaId = NextCastId();
                double activate = now - (AREA_RESTORE_TICK + 0.2) - (i * 0.4);
                BossAreaSnapshot snapshot = new BossAreaSnapshot
                {
                    Start = new BossAreaStart(SAMPLE_BOSS_ID, areaId, castId, AreaCenter((i == 0) ? -AREA_SPREAD : AREA_SPREAD),
                        AREA_RADIUS, AREA_DURATION, AREA_INTERVAL, activate),
                    Fate = EBossAreaFate.Active,
                    LastTickIndex = AREA_RESTORE_TICK,
                    LastTickServerTime = activate + AREA_RESTORE_TICK
                };
                _presenter.RestoreArea(in snapshot);
                int id = areaId;
                Schedule(activate + AREA_DURATION, () => _presenter.EndArea(id, EBossAreaFate.Expired));
            }
        }

        private enum EAreaPairMode
        {
            Pair,
            Overlap,
            Single,
            OneEnds,
            AllCancelled,
            LateSecond
        }

        // 한 시전이 장판을 하나씩 동시에 설치한다. 장판마다 장판 번호가 있고 예고는 장판 번호를 키로 시전 번호 묶음에 속한다
        // 클라이언트가 장판을 스스로 만들지 않고 서버 활성 알림마다 한 개씩 생긴다
        private void PlayAreaPair(EAreaPairMode mode)
        {
            double start = ReadClock();
            int castId = NextCastId();
            int count = (mode == EAreaPairMode.Single) ? 1 : 2;
            double activate = start + AREA_TELEGRAPH;

            for (int i = 0; i < count; ++i)
            {
                int areaId = NextCastId();
                float side = (mode == EAreaPairMode.Overlap) ? 0f : ((i == 0) ? -AREA_SPREAD : AREA_SPREAD);
                Vector3 center = AreaCenter(side);

                _presenter.BeginTelegraph(BossTelegraphStart.Circle(SAMPLE_BOSS_ID, areaId, PHASE_TWO_AREA_ID, center, AREA_RADIUS,
                    start, AREA_TELEGRAPH, castId));

                BossAreaStart area = new BossAreaStart(SAMPLE_BOSS_ID, areaId, castId, center, AREA_RADIUS, AREA_DURATION,
                    AREA_INTERVAL, activate);
                bool late = (mode == EAreaPairMode.LateSecond) && (i == 1);
                Schedule(late ? (activate + AREA_LATE_SECONDS) : activate, () => _presenter.ActivateArea(in area));

                // 패턴 정상 종료는 회복이 끝난 뒤이고 장판과 무관하다
                ScheduleFinish(areaId, activate, AREA_RECOVERY);

                bool cancelThis = (mode == EAreaPairMode.AllCancelled) || ((mode == EAreaPairMode.OneEnds) && (i == 0));
                if (cancelThis)
                {
                    ScheduleAreaTicks(areaId, activate, 0, true);
                    Schedule(activate + AREA_CANCEL_AT, () => _presenter.CancelArea(areaId));
                    continue;
                }

                ScheduleAreaTicks(areaId, activate, late ? Mathf.CeilToInt(AREA_LATE_SECONDS / AREA_INTERVAL) : 0, false);
                Schedule(activate + AREA_DURATION, () => _presenter.EndArea(areaId, EBossAreaFate.Expired));
            }
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

        /// <summary> P04 투사체, 발사 뒤 14m에서 첫 대상에 맞고 사라진다. 패턴이 끝나도 투사체 표시는 따로 간다 </summary>
        [ContextMenu("Play P04 Projectile")]
        public void PlayProjectile()
        {
            PlayProjectile(PROJ_RANGE, EBossProjectileFate.Hit, PROJ_HIT_DISTANCE, false, false);
        }

        /// <summary> P04 투사체, 벽이 가까워 짧은 경로를 표시하고 벽 앞에서 피해 없이 사라진다 </summary>
        [ContextMenu("Play P04 Projectile Near Wall")]
        public void PlayProjectileNearWall()
        {
            PlayProjectile(PROJ_WALL_PATH, EBossProjectileFate.Wall, PROJ_WALL_PATH, false, false);
        }

        /// <summary> P04 투사체, 아무것도 맞지 않고 최대 사거리에서 사라진다 </summary>
        [ContextMenu("Play P04 Projectile Range")]
        public void PlayProjectileRange()
        {
            PlayProjectile(PROJ_RANGE, EBossProjectileFate.Range, PROJ_RANGE, false, false);
        }

        /// <summary> P04 예고 중 취소, 발사 전이라 투사체는 없다 </summary>
        [ContextMenu("Play P04 Telegraph Cancelled")]
        public void PlayProjectileTelegraphCancelled()
        {
            PlayProjectile(PROJ_RANGE, EBossProjectileFate.Range, PROJ_RANGE, true, false);
        }

        /// <summary> P04 투사체가 나는 중 보스 사망이나 전투 종료로 제거된다 </summary>
        [ContextMenu("Play P04 Projectile Cancelled In Flight")]
        public void PlayProjectileCancelledInFlight()
        {
            PlayProjectile(PROJ_RANGE, EBossProjectileFate.Range, PROJ_RANGE, false, true);
        }

        /// <summary> P04 회복이 끝나 다음 패턴(P06)의 예고가 시작돼도 먼저 쏜 투사체는 계속 난다 </summary>
        [ContextMenu("Play P04 Then Next Cast")]
        public void PlayProjectileThenNextCast()
        {
            double start = ReadClock();
            PlayProjectile(PROJ_RANGE, EBossProjectileFate.Range, PROJ_RANGE, false, false);

            // 같은 보스의 다음 시전, P04 예고 표현은 마무리되고 투사체는 남는다
            double nextStart = start + PROJ_TELEGRAPH + PROJ_RECOVERY + 0.1;
            int castId = NextCastId();
            Schedule(nextStart, () => _presenter.BeginTelegraph(BossTelegraphStart.Line(SAMPLE_BOSS_ID, castId, P06_ID,
                transform.position, transform.right, LINE_LENGTH, LINE_WIDTH, nextStart, LINE_TELEGRAPH)));
            Schedule(nextStart + 0.5, () => _presenter.Cancel(castId));
        }

        /// <summary> 투사체가 이미 절반쯤 날아간 뒤에 발사 알림을 받은 늦은 접속, 처음부터 다시 날리지 않고 이어서 그려야 한다 </summary>
        [ContextMenu("Play P04 Late Launch")]
        public void PlayProjectileLateLaunch()
        {
            double now = ReadClock();
            double launch = now - 1.0;
            int castId = NextCastId();

            BossProjectileStart start = new BossProjectileStart(SAMPLE_BOSS_ID, castId, transform.position, transform.forward,
                PROJ_SPEED, PROJ_WIDTH, PROJ_RANGE, launch);
            _presenter.LaunchProjectile(in start);
        }

        /// <summary> 날고 있는 투사체를 스냅샷으로 복원한 접속 </summary>
        [ContextMenu("Play P04 Restore Mid Flight")]
        public void PlayProjectileRestore()
        {
            double now = ReadClock();
            int castId = NextCastId();
            BossProjectileSnapshot snapshot = new BossProjectileSnapshot
            {
                Start = new BossProjectileStart(SAMPLE_BOSS_ID, castId, transform.position, transform.forward, PROJ_SPEED,
                    PROJ_WIDTH, PROJ_RANGE, now - 1.5),
                Fate = EBossProjectileFate.Flying
            };

            _presenter.RestoreProjectile(in snapshot);
        }

        // 예고, 추적, 고정, 발사, 소멸을 서버 알림 순서대로 흘린다. 패턴 종료 알림은 투사체 소멸과 별개로 보낸다
        private void PlayProjectile(float pathLength, EBossProjectileFate endFate, float endDistance, bool cancelTelegraph,
            bool cancelInFlight)
        {
            double start = ReadClock();
            int castId = NextCastId();
            float firstLength = (pathLength < PROJ_RANGE) ? (pathLength * 0.6f) : pathLength;

            _presenter.BeginTelegraph(BossTelegraphStart.Projectile(SAMPLE_BOSS_ID, castId, P04_ID, transform.position,
                transform.forward, firstLength, PROJ_WIDTH, start, PROJ_TELEGRAPH));
            ScheduleTracking(castId, start, PROJ_TRACKING, PROJ_TURN_RATE, pathLength, firstLength);

            if (cancelTelegraph)
            {
                Schedule(start + (PROJ_TELEGRAPH * 0.6), () => _presenter.Cancel(castId));
                return;
            }

            double launch = start + PROJ_TELEGRAPH;
            Vector3 direction = Quaternion.AngleAxis(AimDegrees(PROJ_TRACKING, PROJ_TURN_RATE), Vector3.up) * transform.forward;
            Vector3 origin = transform.position;
            BossProjectileStart shot = new BossProjectileStart(SAMPLE_BOSS_ID, castId, origin, direction, PROJ_SPEED, PROJ_WIDTH,
                pathLength, launch);
            Schedule(launch, () => _presenter.LaunchProjectile(in shot));

            if (cancelInFlight)
            {
                Schedule(launch + PROJ_CANCEL_FLIGHT, () => _presenter.CancelProjectile(castId));
            }
            else
            {
                double endAt = launch + (endDistance / PROJ_SPEED);
                Schedule(endAt, () => _presenter.EndProjectile(castId, endFate, origin + (direction * endDistance)));
            }

            ScheduleFinish(castId, launch, PROJ_RECOVERY);
        }

        /// <summary> P05 장판, 예고 1초 뒤 활성(첫 타격)하고 1초마다 타격해 6회, 6초에 추가 타격 없이 사라진다 </summary>
        [ContextMenu("Play P05 Area")]
        public void PlayArea()
        {
            PlayArea(0f, false, false);
        }

        /// <summary> P05 장판 셋이 1.5초 간격으로 겹쳐 동시에 유지된다, 패턴이 끝나고 다음 패턴이 시작돼도 먼저 만든 장판은 남는다 </summary>
        [ContextMenu("Play P05 Three Areas")]
        public void PlayAreaThree()
        {
            PlayArea(-AREA_SPREAD, false, false);
            Schedule(ReadClock() + AREA_STAGGER, () => PlayArea(0f, false, false));
            Schedule(ReadClock() + (AREA_STAGGER * 2f), () => PlayArea(AREA_SPREAD, false, false));
        }

        /// <summary> P05 예고 중 취소, 활성 전이라 장판은 없다 </summary>
        [ContextMenu("Play P05 Telegraph Cancelled")]
        public void PlayAreaTelegraphCancelled()
        {
            PlayArea(0f, true, false);
        }

        /// <summary> P05 장판이 유지되는 중 보스 사망이나 전투 종료로 제거된다 </summary>
        [ContextMenu("Play P05 Area Cancelled")]
        public void PlayAreaCancelled()
        {
            PlayArea(0f, false, true);
        }

        /// <summary> 장판이 이미 2.5초 지난 뒤에 활성 알림을 받은 늦은 접속, 처음부터 다시 그리지 않고 이어서 그려야 한다 </summary>
        [ContextMenu("Play P05 Late Activation")]
        public void PlayAreaLateActivation()
        {
            double now = ReadClock();
            double activate = now - AREA_LATE_SECONDS;
            int areaId = NextCastId();

            BossAreaStart start = new BossAreaStart(SAMPLE_BOSS_ID, areaId, AreaCenter(0f), AREA_RADIUS, AREA_DURATION, AREA_INTERVAL, activate);
            _presenter.ActivateArea(in start);
            ScheduleAreaTicks(areaId, activate, 3, false);
        }

        /// <summary> 진행 중인 장판을 스냅샷으로 복원한 접속, 이미 표시한 회차는 다시 표시하지 않는다 </summary>
        [ContextMenu("Play P05 Restore Mid Area")]
        public void PlayAreaRestore()
        {
            double now = ReadClock();
            double activate = now - (AREA_RESTORE_TICK + 0.2);
            int areaId = NextCastId();

            BossAreaSnapshot snapshot = new BossAreaSnapshot
            {
                Start = new BossAreaStart(SAMPLE_BOSS_ID, areaId, AreaCenter(0f), AREA_RADIUS, AREA_DURATION, AREA_INTERVAL, activate),
                Fate = EBossAreaFate.Active,
                LastTickIndex = AREA_RESTORE_TICK,
                LastTickServerTime = activate + AREA_RESTORE_TICK
            };
            _presenter.RestoreArea(in snapshot);
            ScheduleAreaTicks(areaId, activate, AREA_RESTORE_TICK + 1, false);
        }

        private Vector3 AreaCenter(float sideOffset)
        {
            return transform.position + (transform.forward * AREA_DISTANCE) + (transform.right * sideOffset);
        }

        // 예고에서 활성, 타격 회차, 소멸까지를 서버 알림 순서대로 흘린다. 장판 한 개의 수명은 패턴 종료와 별개다
        private void PlayArea(float sideOffset, bool cancelTelegraph, bool cancelActive)
        {
            double start = ReadClock();
            int castId = NextCastId();
            Vector3 center = AreaCenter(sideOffset);

            _presenter.BeginTelegraph(BossTelegraphStart.Circle(SAMPLE_BOSS_ID, castId, P05_ID, center, AREA_RADIUS, start, AREA_TELEGRAPH));

            if (cancelTelegraph)
            {
                Schedule(start + (AREA_TELEGRAPH * 0.6), () => _presenter.Cancel(castId));
                return;
            }

            double activate = start + AREA_TELEGRAPH;
            BossAreaStart area = new BossAreaStart(SAMPLE_BOSS_ID, castId, center, AREA_RADIUS, AREA_DURATION, AREA_INTERVAL, activate);
            Schedule(activate, () => _presenter.ActivateArea(in area));

            // 패턴 정상 종료는 회복이 끝난 뒤이고 장판과 무관하다
            ScheduleFinish(castId, activate, AREA_RECOVERY);

            if (cancelActive)
            {
                ScheduleAreaTicks(castId, activate, 0, true);
                Schedule(activate + AREA_CANCEL_AT, () => _presenter.CancelArea(castId));
                return;
            }

            ScheduleAreaTicks(castId, activate, 0, false);
            Schedule(activate + AREA_DURATION, () => _presenter.EndArea(castId, EBossAreaFate.Expired));
        }

        // 활성 시각 기준 firstIndex 회차부터 서버 타격 알림을 보낸다, 이미 지난 회차는 보내지 않는다
        private void ScheduleAreaTicks(int areaId, double activate, int firstIndex, bool onlyBeforeCancel)
        {
            int count = Mathf.CeilToInt(AREA_DURATION / AREA_INTERVAL);
            double now = ReadClock();
            for (int i = firstIndex; i < count; ++i)
            {
                int index = i;
                double at = activate + (index * AREA_INTERVAL);
                if (onlyBeforeCancel && (index * AREA_INTERVAL) >= AREA_CANCEL_AT)
                {
                    break;
                }

                if (at < now)
                {
                    continue;
                }

                Schedule(at, () => _presenter.TickArea(areaId, index, at));
            }
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
            PlayCircle(P03_ID, 0f, cancel);
        }

        private void PlayCircle(string patternId, float recovery, bool cancel)
        {
            double start = ReadClock();
            int castId = NextCastId();

            _presenter.BeginTelegraph(BossTelegraphStart.Circle(SAMPLE_BOSS_ID, castId, patternId, transform.position,
                CIRCLE_RADIUS, start, CIRCLE_TELEGRAPH));

            if (cancel)
            {
                Schedule(start + CIRCLE_TELEGRAPH * 0.5, () => _presenter.Cancel(castId));
                return;
            }

            Schedule(start + CIRCLE_TELEGRAPH, () => _presenter.Strike(castId, start + CIRCLE_TELEGRAPH));
            ScheduleFinish(castId, start + CIRCLE_TELEGRAPH, recovery);
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

            switch (_loopStep % LOOP_STEP_COUNT)
            {
                case 0: PlayDash(); break;
                case 1: PlayFan(); break;
                case 2: PlayCircle(); break;
                case 3: PlayLine(); break;
                case 4: PlayDashNearWall(); break;
                case 5: PlayFanCancelled(); break;
                case 6: PlayLineCancelled(); break;
                case 7: PlayDashCancelled(); break;
                case 8: PlayProjectile(); break;
                case 9: PlayProjectileNearWall(); break;
                case 10: PlayProjectileCancelledInFlight(); break;
                case 11: PlayArea(); break;
                case 12: PlayAreaCancelled(); break;
                case 13: PlayPhaseTwoCircle(); break;
                case 14: PlayPhaseTwoCircleCancelled(); break;
                case 15: PlayPhaseTwoVolley(); break;
                case 16: PlayPhaseTwoVolleyOneHit(); break;
                case 17: PlayPhaseTwoVolleyCancelled(); break;
                case 18: PlayPhaseTwoAreas(); break;
                case 19: PlayPhaseTwoAreaOneEnds(); break;
                default: PlayPhaseTwoAreasCancelled(); break;
            }

            ++_loopStep;
            double settle = ReadClock() + 4.5;
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
