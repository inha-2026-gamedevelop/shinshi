// System
using System;

namespace Shinshi.AI
{
    /// <summary> 성장 메뉴 안내 한 번의 진행, 끝까지 보거나 건너뛰면 끝나고 도중에 그만두면 끝난 것이 아니다 </summary>
    public sealed class MenuGuideSession
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int _stepCount;

        private int  _index      = 0;
        private bool _isFinished = false;
        private bool _wasSkipped = false;

        /****************************************
        *              Properties
        ****************************************/

        public int  StepCount  => _stepCount;
        public int  Index      => _index;
        public bool IsFinished => _isFinished;
        public bool WasSkipped => _wasSkipped;
        public bool IsLastStep => _index == (_stepCount - 1);

        // 완료 기록을 남길 조건, 끝까지 봤거나 건너뛰었을 때다. 안내 도중에 게임을 끝내면 거짓이라 다음에 다시 안내한다
        public bool CountsAsCompleted => _isFinished;

        /****************************************
        *                Methods
        ****************************************/

        public MenuGuideSession(int stepCount)
        {
            if (stepCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(stepCount));
            }

            _stepCount = stepCount;
        }

        /// <summary> 다음 단계로 간다, 마지막 단계에서 부르면 안내가 끝난다. 이미 끝났으면 거짓이다 </summary>
        public bool Next()
        {
            if (_isFinished)
            {
                return false;
            }

            if (IsLastStep)
            {
                _isFinished = true;
                return true;
            }

            ++_index;
            return true;
        }

        public void Skip()
        {
            if (_isFinished)
            {
                return;
            }

            _isFinished = true;
            _wasSkipped = true;
        }
    }
}
