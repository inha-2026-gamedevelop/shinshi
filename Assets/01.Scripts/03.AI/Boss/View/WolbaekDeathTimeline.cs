namespace Shinshi.AI
{
    // 시험 씬 DeathSequencePreview가 하던 순서를 코루틴 없이 시계로 옮긴 것이다
    //   쓰러짐(처치 클립 상태가 끝날 때까지) -> 정지 0.5초 -> 디졸브 3초 -> 소멸 완료
    // 시작은 클라이언트가 처치를 처음 본 순간이다. 재접속으로 이미 쓰러진 보스를 받으면 쓰러짐을 건너뛰고 정지부터 시작하고,
    // 서버 처치 시각(BossState.DefeatServerTime)을 알면 그만큼 지난 지점부터 잇는다. 쓰러짐 길이는 클립이 정해 서버가 모르므로 한계값(DEATH_FALL_LIMIT_SECONDS)으로 본다
    /// <summary> 처치 후 정지와 디졸브 단계를 시간으로 진행하는 순수 계산, 엔진 오브젝트를 만들지 않아 EditMode에서 시험한다 </summary>
    public sealed class WolbaekDeathTimeline
    {
        /****************************************
        *               Types
        ****************************************/

        public enum EStage
        {
            None = 0,
            Falling = 1,
            Holding = 2,
            Dissolving = 3,
            Dissolved = 4
        }

        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 처치 뒤 지난 시간을 모른다 </summary>
        public const float UNKNOWN_ELAPSED = -1f;

        private EStage _stage = EStage.None;
        private float  _stageElapsed;
        private bool   _hasDissolveStarted;
        private bool   _hasSeenFall;
        private bool   _isDissolveStartPending; // 디졸브 중간부터 시작했어도 효과 시작 신호는 다음 Tick에 한 번 낸다

        /****************************************
        *              Properties
        ****************************************/

        public EStage Stage => _stage;

        public float DissolveProgress => _stage == EStage.Dissolved ? 1f
            : (_stage == EStage.Dissolving ? UnityEngine.Mathf.Clamp01(_stageElapsed / WolbaekMotionPlan.DEATH_DISSOLVE_SECONDS) : 0f);

        /// <summary> 디졸브를 시작하라는 신호가 한 번 났다 </summary>
        public bool HasDissolveStarted => _hasDissolveStarted;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 처치 연출을 시작한다. isLate가 참이면 쓰러짐을 건너뛰고, elapsedSinceDefeat가 0 이상이면 처치 뒤 그만큼 지난 지점부터 시작한다 </summary>
        public void Begin(bool isLate, float elapsedSinceDefeat = UNKNOWN_ELAPSED)
        {
            Reset();
            _stage = isLate ? EStage.Holding : EStage.Falling;
            if ((!isLate) || (elapsedSinceDefeat < 0f))
            {
                return;
            }

            float afterFall = elapsedSinceDefeat - WolbaekMotionPlan.DEATH_FALL_LIMIT_SECONDS;
            if (afterFall < WolbaekMotionPlan.DEATH_HOLD_SECONDS)
            {
                _stageElapsed = UnityEngine.Mathf.Max(0f, afterFall);
                return;
            }

            _stage                  = EStage.Dissolving;
            _stageElapsed           = UnityEngine.Mathf.Min(afterFall - WolbaekMotionPlan.DEATH_HOLD_SECONDS,
                WolbaekMotionPlan.DEATH_DISSOLVE_SECONDS);
            _isDissolveStartPending = true;
        }

        public void Reset()
        {
            _stage                  = EStage.None;
            _stageElapsed           = 0f;
            _hasDissolveStarted     = false;
            _hasSeenFall            = false;
            _isDissolveStartPending = false;
        }

        /// <summary> 시간을 진행한다. 이 호출로 디졸브를 시작해야 하면 참이다. isFallPlaying은 처치 클립 상태가 재생 중인가다 </summary>
        public bool Tick(float deltaTime, bool isFallPlaying)
        {
            bool shouldStartDissolve = false;
            if (_isDissolveStartPending)
            {
                _isDissolveStartPending = false;
                _hasDissolveStarted     = true;
                shouldStartDissolve     = true;
            }

            _stageElapsed += deltaTime;

            if (_stage == EStage.Falling)
            {
                _hasSeenFall |= isFallPlaying;
                // 전환이 끝나 처치 상태에 들어간 뒤 상태가 끝나면 정지로 넘어간다. 상태가 없어도 멈추지 않게 한계를 둔다
                bool isEntered = _stageElapsed >= WolbaekMotionPlan.DEATH_ENTER_SECONDS;
                bool isOver    = isEntered && _hasSeenFall && (!isFallPlaying);

                if (isOver || (_stageElapsed >= WolbaekMotionPlan.DEATH_FALL_LIMIT_SECONDS))
                {
                    Advance(EStage.Holding);
                }
            }

            if ((_stage == EStage.Holding) && (_stageElapsed >= WolbaekMotionPlan.DEATH_HOLD_SECONDS))
            {
                Advance(EStage.Dissolving);
                _hasDissolveStarted = true;
                shouldStartDissolve = true;
            }

            if ((_stage == EStage.Dissolving) && (_stageElapsed >= WolbaekMotionPlan.DEATH_DISSOLVE_SECONDS))
            {
                Advance(EStage.Dissolved);
            }

            return shouldStartDissolve;
        }

        private void Advance(EStage next)
        {
            _stage        = next;
            _stageElapsed = 0f;
        }
    }
}
