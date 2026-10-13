// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    /// <summary> 공격 FSM, 예고와 실행과 회복의 단계 타이머를 서버 시각으로 진행한다 </summary>
    public sealed class BossAttackFsm
    {
        /****************************************
        *                Fields
        ****************************************/

        // 한 틱에 넘을 수 있는 단계 경계 수, 예고와 실행과 회복을 모두 지나도 이 값을 넘지 않는다
        private const int MAX_TRANSITIONS_PER_TICK = 4;

        private readonly IBossCastListener _casts;

        private PatternDefinition   _definition;
        private IBossPattern        _pattern;
        private int                 _castInstanceId;
        private double              _stateEnd;
        private bool                _controlUsed;

        /****************************************
        *              Properties
        ****************************************/

        public EBossAttackState State { get; private set; } = EBossAttackState.Ready;

        public PatternDefinition Definition => _definition;

        public int CastInstanceId => _castInstanceId;

        /// <summary> 이번 패턴에서 직접 제어를 이미 받았다 </summary>
        public bool ControlUsed => _controlUsed;

        /****************************************
        *                Methods
        ****************************************/

        // casts는 발동과 정상 종료를 받는 쪽이다. 비우면 알리지 않는다
        public BossAttackFsm(IBossCastListener casts = null)
        {
            _casts = casts;
        }

        /// <summary> 예고를 시작한다, Ready에서만 시작할 수 있다 </summary>
        public bool Start(PatternDefinition definition, IBossPattern pattern, in BossPatternContext context, double serverTime)
        {
            if (State != EBossAttackState.Ready)
            {
                return false;
            }

            _definition     = definition;
            _pattern        = pattern;
            _castInstanceId = context.CastInstanceId;
            _controlUsed    = false;
            State           = EBossAttackState.Telegraph;
            _stateEnd       = serverTime + definition.TelegraphSeconds;

            _pattern.Begin(in context, serverTime);
            return true;
        }

        /// <summary> 단계 타이머를 진행한다, 경계를 넘으면 같은 틱 안에서 다음 단계까지 이어서 처리한다 </summary>
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

        // TODO(기웅): 기획 미정 - 구간의 끝 경계를 포함할지 정해지지 않았다. 지금은 포함하며 이 값에 기대는 테스트는 두지 않았다
        /// <summary> 직접 제어를 받을 수 있는 구간인가, 구간은 지정한 단계의 마지막 n초다 </summary>
        public bool IsControlWindowOpen(double serverTime)
        {
            if (State == EBossAttackState.Ready)
            {
                return false;
            }

            if (!_definition.HasControlWindow)
            {
                return false;
            }

            if (!MatchesControlPhase(_definition.ControlPhase, State))
            {
                return false;
            }

            return (serverTime >= (_stateEnd - _definition.ControlWindowSeconds)) && (serverTime <= _stateEnd);
        }

        public void MarkControlUsed()
        {
            _controlUsed = true;
        }

        /// <summary> 진행 중인 패턴을 끊는다, 끊은 시전 번호를 돌려주고 진행 중이 아니면 CombatIds.NONE이다 </summary>
        public int Cancel(EBossInterruptReason reason)
        {
            if (State == EBossAttackState.Ready)
            {
                return CombatIds.NONE;
            }

            int castInstanceId = _castInstanceId;
            IBossPattern pattern = _pattern;
            Reset();

            // FSM이 먼저 Ready로 돌아간 뒤 패턴에 알려, 패턴이 정리 중에 이 FSM을 다시 부르지 못하게 한다
            pattern.Cancel(reason);
            return castInstanceId;
        }

        // 단계 시작은 틱 시각이 아니라 이전 단계의 끝 경계로 잡아 틱이 거칠어도 시간이 밀리지 않게 한다
        private void Advance()
        {
            double boundary = _stateEnd;

            switch (State)
            {
                case EBossAttackState.Telegraph:
                    State     = EBossAttackState.Execute;
                    _stateEnd = boundary + _definition.ActiveSeconds;
                    _pattern.EnterState(State, boundary);
                    _casts?.OnCastExecuted(_castInstanceId, boundary);
                    break;
                case EBossAttackState.Execute:
                    // 회복 시간을 패턴이 정하면(무철 P06의 충돌 회복) 실행 끝 경계의 결과가 정해진 뒤에 읽는다
                    State = EBossAttackState.Recover;
                    _pattern.EnterState(State, boundary);
                    if (State != EBossAttackState.Recover)
                    {
                        break;
                    }

                    _stateEnd = boundary + ((_pattern is IBossRecoveryDuration recovery)
                        ? recovery.GetRecoverySeconds(_definition.RecoverySeconds)
                        : _definition.RecoverySeconds);
                    break;
                case EBossAttackState.Recover:
                    IBossPattern pattern        = _pattern;
                    int          castInstanceId = _castInstanceId;
                    Reset();
                    pattern.Finish();
                    _casts?.OnCastFinished(castInstanceId, boundary);
                    break;
                default:
                    break;
            }
        }

        private void Reset()
        {
            State           = EBossAttackState.Ready;
            _definition     = null;
            _pattern        = null;
            _castInstanceId = CombatIds.NONE;
            _controlUsed    = false;
        }

        private static bool MatchesControlPhase(EBossActionState phase, EBossAttackState state)
        {
            switch (phase)
            {
                case EBossActionState.Telegraph:
                    return state == EBossAttackState.Telegraph;
                case EBossActionState.Execute:
                    return state == EBossAttackState.Execute;
                case EBossActionState.Recover:
                    return state == EBossAttackState.Recover;
                default:
                    return false;
            }
        }
    }
}
