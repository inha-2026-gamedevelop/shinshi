// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 프롤로그 입장 교환이 준 캐릭터의 저장 상태, 세션을 어느 구간의 처음부터 다시 시작할지 정한다 </summary>
    public readonly struct PrologueResume
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly bool _hasValue;
        private readonly int  _stage;
        private readonly int  _wolbaekPotions;
        private readonly bool _talismanChecked;

        /****************************************
        *              Properties
        ****************************************/

        // 프롤로그 입장 교환이 아니면 거짓이다
        public bool HasValue        => _hasValue;

        // PrologueCodes.Stage
        public int  Stage           => _stage;
        public int  WolbaekPotions  => _wolbaekPotions;
        public bool TalismanChecked => _talismanChecked;

        /****************************************
        *                Methods
        ****************************************/

        public PrologueResume(int stage, int wolbaekPotions, bool talismanChecked)
        {
            _hasValue        = true;
            _stage           = stage;
            _wolbaekPotions  = wolbaekPotions;
            _talismanChecked = talismanChecked;
        }

        /// <summary> 단계와 물약 수가 계약 범위 안인가 </summary>
        public bool IsValid()
        {
            return (_hasValue)
                && (_stage >= PrologueCodes.Stage.INTRO) && (_stage <= PrologueCodes.Stage.DONE)
                && (_wolbaekPotions >= 0) && (_wolbaekPotions <= PrologueCodes.MAX_POTIONS);
        }
    }
}
