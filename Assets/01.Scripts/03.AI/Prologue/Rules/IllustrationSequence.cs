// System
using System;

namespace Shinshi.AI
{
    /// <summary> 일러스트 여러 장의 클릭 흐름, 출력 중 클릭은 글 완성이고 완성 뒤 클릭이 다음 장이다 </summary>
    public sealed class IllustrationSequence
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int  _pageCount;
        private readonly bool _startRevealed;

        private int  _pageIndex  = 0;
        private bool _isTyping   = false;
        private bool _isFinished = false;

        /****************************************
        *              Properties
        ****************************************/

        public int  PageIndex  => _pageIndex;
        public int  PageCount  => _pageCount;
        public bool IsFinished => _isFinished;

        // 참이면 "클릭하여 전체 표시", 거짓이면 "클릭하여 다음으로"를 안내한다
        public bool IsTyping   => _isTyping;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> startRevealed가 참이면 각 장의 글을 처음부터 전부 보여 준다, 부적 조사 자막처럼 </summary>
        public IllustrationSequence(int pageCount, bool startRevealed = false)
        {
            if (pageCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pageCount));
            }

            _pageCount     = pageCount;
            _startRevealed = startRevealed;
            _isTyping      = !startRevealed;
        }

        /// <summary> 클릭 한 번을 처리한다, 글을 완성한 클릭이 다음 장 이동까지 같이 하지는 않는다 </summary>
        public EIllustrationClick Click()
        {
            if (_isFinished)
            {
                return EIllustrationClick.None;
            }

            if (_isTyping)
            {
                _isTyping = false;
                return EIllustrationClick.RevealedText;
            }

            if ((_pageIndex + 1) >= _pageCount)
            {
                _isFinished = true;
                return EIllustrationClick.Finished;
            }

            ++_pageIndex;
            _isTyping = !_startRevealed;
            return EIllustrationClick.NextPage;
        }

        /// <summary> 글이 클릭 없이 끝까지 출력됐을 때 뷰가 부른다 </summary>
        public void OnTextCompleted()
        {
            _isTyping = false;
        }

        /// <summary> 이 일러스트 구간 전체를 건너뛴다 </summary>
        public void Skip()
        {
            _isTyping   = false;
            _isFinished = true;
        }
    }
}
