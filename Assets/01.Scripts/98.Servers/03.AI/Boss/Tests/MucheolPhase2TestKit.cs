// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 무철 2페이즈 시험이 함께 쓰는 가짜 의존성이다. 월백과 무철 1페이즈 시험이 각자 비공개로 둔 같은 모양의 가짜를 한 곳에 모았다.
    // 수치는 여기에 없다. 각 시험이 자기 데이터를 넣는다

    /// <summary> 반경 조회를 하는 가짜 대상 조회, 보스 시험 기본 가짜(FakeTargetQuery)는 반경 조회가 비어 있어 따로 둔다 </summary>
    internal sealed class Phase2Query : ICombatTargetQuery
    {
        public List<FakeCombatTarget> Players { get; } = new List<FakeCombatTarget>();

        public int GetLivingPlayers(List<ICombatTarget> results)
        {
            results.Clear();
            for (int i = 0; i < Players.Count; ++i)
            {
                if (Players[i].LifeState == EParticipantLifeState.Alive)
                {
                    results.Add(Players[i]);
                }
            }

            return results.Count;
        }

        public int GetLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons,
            List<ICombatTarget> results)
        {
            results.Clear();
            for (int i = 0; i < Players.Count; ++i)
            {
                FakeCombatTarget candidate = Players[i];
                Vector3 offset = candidate.Position - center;
                offset.y = 0f;

                if ((candidate.LifeState == EParticipantLifeState.Alive) && (candidate.Faction == faction)
                    && (includeSummons || !candidate.IsSummon) && (offset.sqrMagnitude <= (radius * radius)))
                {
                    results.Add(candidate);
                }
            }

            return results.Count;
        }

        public int CountLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons)
        {
            return GetLivingInRadius(center, radius, faction, includeSummons, new List<ICombatTarget>());
        }

        public bool TryGet(int combatId, out ICombatTarget target)
        {
            for (int i = 0; i < Players.Count; ++i)
            {
                if (Players[i].CombatId == combatId)
                {
                    target = Players[i];
                    return true;
                }
            }

            target = null;
            return false;
        }
    }

    /// <summary> 벽이 z = WallZ 평면인 가짜 이동 창구, 몸체 반경만큼 앞에서 멈추고 보스 위치를 실제로 옮긴다 </summary>
    internal sealed class Phase2Movement : IBossMovement
    {
        public FakeCombatTarget Self;
        public float            WallZ = float.PositiveInfinity;
        public int              MoveCalls;

        public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
        {
            return Mathf.Clamp(LimitAlong(origin, direction, bodyRadius, maxDistance), 0f, maxDistance);
        }

        public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
        {
            ++MoveCalls;

            Vector3 delta    = to - from;
            float   distance = delta.magnitude;
            Vector3 reached  = to;

            if (distance > 0f)
            {
                Vector3 direction = delta / distance;
                reached = from + (direction * Mathf.Clamp(LimitAlong(from, direction, bodyRadius, distance), 0f, distance));
            }

            Self.Position = reached;
            return reached;
        }

        private float LimitAlong(Vector3 origin, Vector3 direction, float bodyRadius, float maxDistance)
        {
            if ((float.IsPositiveInfinity(WallZ)) || (direction.z <= 0.0001f))
            {
                return maxDistance;
            }

            return (WallZ - bodyRadius - origin.z) / direction.z;
        }
    }

    /// <summary> 순차 타격 알림을 그대로 기록한다 </summary>
    internal sealed class SequenceRecorder : IMucheolSequenceTelegraphListener
    {
        public int                        StartCount;
        public float                      StartSeconds;
        public Vector3                    StartOrigin;
        public IReadOnlyList<MucheolSequenceStep> StartSteps;
        public int                        AimChangeCount;
        public int                        LockCount;
        public Vector3                    LockDirection;
        public List<int>                  HitIndices = new List<int>();
        public List<int>                  HitCounts  = new List<int>();
        public List<EBossInterruptReason> Cancels    = new List<EBossInterruptReason>();

        public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float telegraphSeconds,
            IReadOnlyList<MucheolSequenceStep> steps)
        {
            ++StartCount;
            StartSeconds = telegraphSeconds;
            StartOrigin  = origin;
            StartSteps   = steps;
        }

        public void OnAimChanged(int castInstanceId, Vector3 direction)
        {
            ++AimChangeCount;
        }

        public void OnDirectionLocked(int castInstanceId, Vector3 direction)
        {
            ++LockCount;
            LockDirection = direction;
        }

        public void OnStepHit(int castInstanceId, int stepIndex, int hitCount)
        {
            HitIndices.Add(stepIndex);
            HitCounts.Add(hitCount);
        }

        public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            Cancels.Add(reason);
        }
    }

    /// <summary> 연속 돌진 알림을 순서대로 문자열로 기록한다, 돌진 번호가 맞는지 순서까지 한 번에 비교하려는 용도다 </summary>
    internal sealed class DashRecorder : IMucheolMultiDashListener
    {
        public List<string> Log = new List<string>();

        public int     StartedCount;
        public int     TruncatedPlanned;
        public int     TruncatedCompleted = -1;
        public int     TelegraphDashCount;
        public float   TelegraphLength;
        public Vector3 LastReaimDirection;
        public Vector3 TelegraphDirection;
        public List<Vector3>              ReaimDirections = new List<Vector3>();
        public List<Vector3>              LockDirections  = new List<Vector3>();
        public List<Vector3>              StartDirections = new List<Vector3>();
        public List<EBossInterruptReason> Cancels = new List<EBossInterruptReason>();
        public List<bool>                 StoppedByWall = new List<bool>();
        public List<double>               StopTimes = new List<double>();
        public List<float>                StartDistances = new List<float>();
        public List<double>               StartTimes = new List<double>();
        public List<float>                StartSeconds = new List<float>();

        public void OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float length, float width,
            float telegraphSeconds, int dashCount)
        {
            TelegraphDashCount = dashCount;
            TelegraphLength    = length;
            TelegraphDirection = direction;
            Log.Add("telegraph");
        }

        public void OnAimChanged(int castInstanceId, int dashIndex, Vector3 direction, float length)
        {
            LastReaimDirection = direction;
            Log.Add("aim" + dashIndex);
        }

        public void OnDirectionLocked(int castInstanceId, int dashIndex, Vector3 direction, float length)
        {
            LastReaimDirection = direction;
            LockDirections.Add(direction);
            Log.Add("lock" + dashIndex);
        }

        public void OnReaimStarted(int castInstanceId, int dashIndex, Vector3 origin, Vector3 direction, float length, float width,
            float reaimSeconds)
        {
            ReaimDirections.Add(direction);
            Log.Add("reaim" + dashIndex);
        }

        public void OnDashStarted(int castInstanceId, int dashIndex, Vector3 origin, Vector3 direction, float distance, float seconds,
            float speed, double startServerTime)
        {
            ++StartedCount;
            StartDistances.Add(distance);
            StartTimes.Add(startServerTime);
            StartSeconds.Add(seconds);
            StartDirections.Add(direction);
            Log.Add("start" + dashIndex);
        }

        public void OnDashStopped(int castInstanceId, int dashIndex, Vector3 position, bool stoppedByWall,
            double stopServerTime)
        {
            StoppedByWall.Add(stoppedByWall);
            StopTimes.Add(stopServerTime);
            Log.Add("stop" + dashIndex);
        }

        public void OnHit(int castInstanceId, int dashIndex, int hitCount)
        {
            Log.Add("hit" + dashIndex + "x" + hitCount);
        }

        public void OnTruncated(int castInstanceId, int plannedDashCount, int completedDashCount)
        {
            TruncatedPlanned   = plannedDashCount;
            TruncatedCompleted = completedDashCount;
            Log.Add("truncated");
        }

        public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            Cancels.Add(reason);
            Log.Add("cancelled");
        }
    }

    // 제안하는 FSM 변경의 수용 기준을 확인하는 가짜 FSM, 실제 BossAttackFsm이 아니다.
    // 실제와 다른 점은 실행 단계에 들어간 직후(EnterState 호출 뒤)에 패턴이 정한 실행 시간을 읽어 단계 끝을 정하는 것뿐이다
    internal sealed class ProposedExecuteFsm
    {
        private const int MAX_TRANSITIONS_PER_TICK = 4;

        private PatternDefinition _definition;
        private IBossPattern      _pattern;
        private double            _stateEnd;

        public EBossAttackState State { get; private set; } = EBossAttackState.Ready;

        /// <summary> 실행에서 회복으로 넘어간 경계 시각, 아직 넘어가지 않았으면 음수다 </summary>
        public double RecoverBoundary { get; private set; } = -1.0;

        public void Start(PatternDefinition definition, IBossPattern pattern, in BossPatternContext context, double serverTime)
        {
            _definition     = definition;
            _pattern        = pattern;
            RecoverBoundary = -1.0;
            State           = EBossAttackState.Telegraph;
            _stateEnd       = serverTime + definition.TelegraphSeconds;
            _pattern.Begin(in context, serverTime);
        }

        public void Tick(double serverTime)
        {
            for (int i = 0; (i < MAX_TRANSITIONS_PER_TICK) && (State != EBossAttackState.Ready); ++i)
            {
                _pattern.Tick(State, serverTime);
                if (serverTime < _stateEnd)
                {
                    return;
                }

                Advance();
            }
        }

        private void Advance()
        {
            double boundary = _stateEnd;

            switch (State)
            {
                case EBossAttackState.Telegraph:
                    State     = EBossAttackState.Execute;
                    _stateEnd = boundary + _definition.ActiveSeconds;
                    _pattern.EnterState(State, boundary);
                    if (_pattern is IBossExecuteDuration provider)
                    {
                        _stateEnd = boundary + provider.GetExecuteSeconds(_definition.ActiveSeconds);
                    }

                    break;
                case EBossAttackState.Execute:
                    State           = EBossAttackState.Recover;
                    RecoverBoundary = boundary;
                    _stateEnd       = boundary + _definition.RecoverySeconds;
                    _pattern.EnterState(State, boundary);
                    break;
                case EBossAttackState.Recover:
                    IBossPattern pattern = _pattern;
                    State       = EBossAttackState.Ready;
                    _definition = null;
                    _pattern    = null;
                    pattern.Finish();
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary> 시험 도우미, 좌표와 요청 목록 </summary>
    internal static class Phase2Helpers
    {
        /// <summary> 위에서 볼 때 +z에서 시계 방향 각도(도)와 거리로 수평 위치를 만든다, 양수 각도는 오른쪽(+x)이다 </summary>
        public static Vector3 Polar(float degrees, float distance)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * distance;
        }

        public static List<DamageRequest> RequestsFor(FakeDamageResolver resolver, int targetId)
        {
            List<DamageRequest> list = new List<DamageRequest>();
            for (int i = 0; i < resolver.Requests.Count; ++i)
            {
                if (resolver.Requests[i].HitTargetId == targetId)
                {
                    list.Add(resolver.Requests[i]);
                }
            }

            return list;
        }
    }
}
