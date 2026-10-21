// System
using System;
using System.Collections.Generic;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 무철 기믹 "방향 봉인"의 서버 진행이다. 체력이 발동 비율에 닿으면 그 비율에서 체력을 고정하고, 진행 중이던 패턴이 끝난 뒤
    // 오른쪽, 왼쪽, 후방 순서로 방향마다 정해진 타수를 채우면 봉인이 깨진다. 기믹 중에도 보스는 기존 패턴을 그대로 쓴다.
    //
    // 체력 고정은 이 클래스가 체력을 되돌려서 하지 않는다. 발동 전에는 하한이 없어 피해가 평소대로 들어가고, 정상 피해로 체력이 0이 되면
    // 기믹 없이 기존 처치와 승리 경로를 따른다. 체력이 0이 아닌 채 발동 비율 이하에 도착한 순간(타격 보고 또는 틱에서 처음 읽은 시점)에만
    // 그때 남은 실제 체력 비율을 하한으로 잡는다(35% 에서 20%로 내려왔으면 20%). 서버 피해 판정기는 IMucheolSealGuard로 받은 하한 아래로
    // 깎지 않고 하한에서 막힌 피해를 치명이나 처치로 알리지 않는다. 하한은 봉인이 깨질 때까지 유지되고 깨진 뒤의 다음 타격부터는 없다
    //
    // 표식은 진행 중인 패턴이 끝난 뒤에 나온다. 공용 두뇌는 공격이 비어 있고 새 선택이 가능할 때만 선택 규칙을 평가하므로,
    // 발동 뒤 처음 TrySelect가 불린 시점을 패턴이 끝난 시점으로 본다. 이 규칙은 아무것도 고르지 않아 선택을 막지도 가로채지도 않는다.
    // 표식이 나오기 전의 타격은 타수에 넣지 않는다. 페이즈 전환도 보류하지 않는다
    //
    // 같은 공격 동작의 중복 충돌 보고는 한 번만 센다. 동작 번호 기록은 상한이 있어 오래된 번호부터 잊지만, 번호는 늘어나는 값이라
    // 상한을 넘겨 늦게 오는 같은 동작의 보고는 현실적으로 없다. 요구 타수는 만들 때 받은 출정 시작 인원으로 한 번 정하고 바꾸지 않는다
    /// <summary> 무철 방향 봉인 기믹, 체력 고정과 방향 순서와 타수 집계 </summary>
    public sealed class MucheolSealGimmick : IBossGimmick, IBossGimmickCleanup, IMucheolSealGuard
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int SEEN_ACTION_CAP = 128;

        private static readonly EMucheolSealDirection[] Order =
        {
            EMucheolSealDirection.Right,
            EMucheolSealDirection.Left,
            EMucheolSealDirection.Rear
        };

        /****************************************
        *                Fields
        ****************************************/

        private readonly IBossVitals              _vitals;
        private readonly MucheolSealGimmickSpec   _spec;
        private readonly int                      _hitsPerDirection;
        private readonly HashSet<int>             _seenActions = new HashSet<int>();
        private readonly Queue<int>               _seenOrder   = new Queue<int>();

        private EMucheolSealState _state = EMucheolSealState.Dormant;
        private int               _directionIndex;
        private int               _hitCount;
        private int               _countedHitSerial;
        private float             _frozenHealthRate;

        /****************************************
        *              Properties
        ****************************************/

        public EMucheolSealState State => _state;

        /// <summary> 지금 활성인 방향, 표식이 나와 있지 않으면 None이다 </summary>
        public EMucheolSealDirection CurrentDirection
        {
            get { return (_state == EMucheolSealState.Active) ? Order[_directionIndex] : EMucheolSealDirection.None; }
        }

        /// <summary> 현재 방향에서 센 타수 </summary>
        public int HitCount => (_state == EMucheolSealState.Active) ? _hitCount : 0;

        /// <summary> 방향마다 필요한 타수, 출정 시작 인원으로 한 번 정해진다 </summary>
        public int RequiredHits => _hitsPerDirection;

        /// <summary> 유효타가 날 때마다 오르는 번호, 표식이 짧게 반응할 시점을 클라이언트가 알아채는 값이다 </summary>
        public int CountedHitSerial => _countedHitSerial;

        /// <summary> 표식이 나와 있다 </summary>
        public bool IsMarkerVisible => _state == EMucheolSealState.Active;

        /// <summary> 보스 체력이 내려갈 수 있는 최저 비율, 0이면 하한이 없다. 발동 전과 봉인이 깨진 뒤에는 0이고 봉인 중에는 도착 순간 남은 체력이다 </summary>
        public float HealthFloorRate
        {
            get
            {
                bool isSealing = (_state == EMucheolSealState.Pending) || (_state == EMucheolSealState.Active);
                return isSealing ? _frozenHealthRate : 0f;
            }
        }

        // 봉인은 일반 선택을 가로채지도 막지도 않고 페이즈 전환도 보류하지 않는다
        public bool DefersPhaseTransition => false;

        public bool BlocksSelection => false;

        /****************************************
        *                Methods
        ****************************************/

        // startingCount는 출정 시작 때 고정된 인원이다. 이탈과 사망과 재접속으로 달라진 현재 인원을 넘기지 않는다
        public MucheolSealGimmick(IBossVitals vitals, MucheolSealGimmickSpec spec, int startingCount)
        {
            if (vitals == null)
            {
                throw new ArgumentNullException(nameof(vitals));
            }

            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            _vitals           = vitals;
            _spec             = spec;
            _hitsPerDirection = spec.GetHitsPerDirection(startingCount);
        }

        public void Tick(double serverTime, int phase)
        {
            TryArm();
        }

        // 살아서 발동 비율 이하에 도착했을 때만 그 실제 남은 체력을 동결한다. 체력 0은 사망 경로이므로 발동하지 않는다
        private void TryArm()
        {
            if ((_state != EMucheolSealState.Dormant) || (_vitals.IsHealthZero))
            {
                return;
            }

            float rate = _vitals.HealthRate;
            if ((rate > 0f) && (rate <= _spec.TriggerHealthRate))
            {
                _frozenHealthRate = rate;
                _state            = EMucheolSealState.Pending;
            }
        }

        // 아무것도 고르지 않는다. 이 평가가 불렸다는 것은 공격이 비어 있다는 뜻이라 대기 중이던 표식을 오른쪽에 낸다
        public bool TrySelect(BossBlackboard blackboard, out PatternDefinition pattern)
        {
            pattern = null;

            if (_state == EMucheolSealState.Pending)
            {
                _state          = EMucheolSealState.Active;
                _directionIndex = 0;
                _hitCount       = 0;
            }

            return false;
        }

        public void OnCastExecuted(int castInstanceId, double serverTime)
        {
        }

        public void OnCastFinished(int castInstanceId, double serverTime)
        {
        }

        public bool ConsumeCleanupRequest()
        {
            return false;
        }

        public void Stop(EBossInterruptReason reason)
        {
            _state = EMucheolSealState.Stopped;
        }

        /// <summary> 보스에 정상 적중한 타격 한 건을 보고받아 타수와 방향을 처리한다 </summary>
        public MucheolSealVerdict ReportHit(in MucheolSealHit hit)
        {
            // 앞선 타격으로 이미 도착했는데 틱이 아직 읽지 못한 경우를 위해 먼저 확인한다
            TryArm();

            // 하한은 처리 전의 값이다, 봉인을 깨는 타격에도 체력은 하한 아래로 내려가지 않는다
            float floor = HealthFloorRate;
            return new MucheolSealVerdict(Evaluate(in hit), floor);
        }

        private EMucheolSealHitResult Evaluate(in MucheolSealHit hit)
        {
            if (_state != EMucheolSealState.Active)
            {
                return EMucheolSealHitResult.NotActive;
            }

            if ((hit.IsDamageOverTime) || (hit.IsSummonAttack))
            {
                return EMucheolSealHitResult.Excluded;
            }

            if (hit.ActionInstanceId <= 0)
            {
                return EMucheolSealHitResult.Unclassifiable;
            }

            if (_seenActions.Contains(hit.ActionInstanceId))
            {
                return EMucheolSealHitResult.Duplicate;
            }

            EMucheolSealDirection zone = MucheolSealGeometry.Classify(hit.BossForward, hit.IncomingDirection,
                _spec.FrontHalfAngle, _spec.SideLimitAngle);
            if (zone == EMucheolSealDirection.None)
            {
                return EMucheolSealHitResult.Unclassifiable;
            }

            RememberAction(hit.ActionInstanceId);

            if (zone != Order[_directionIndex])
            {
                return EMucheolSealHitResult.WrongDirection;
            }

            ++_hitCount;
            ++_countedHitSerial;

            if (_hitCount < _hitsPerDirection)
            {
                return EMucheolSealHitResult.Counted;
            }

            _hitCount = 0;
            ++_directionIndex;

            if (_directionIndex < Order.Length)
            {
                return EMucheolSealHitResult.DirectionCleared;
            }

            _state = EMucheolSealState.Broken;
            return EMucheolSealHitResult.Broken;
        }

        private void RememberAction(int actionInstanceId)
        {
            _seenActions.Add(actionInstanceId);
            _seenOrder.Enqueue(actionInstanceId);

            if (_seenOrder.Count > SEEN_ACTION_CAP)
            {
                _seenActions.Remove(_seenOrder.Dequeue());
            }
        }
    }
}
