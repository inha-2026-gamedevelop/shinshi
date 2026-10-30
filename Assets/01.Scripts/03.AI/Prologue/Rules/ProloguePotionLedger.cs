namespace Shinshi.AI
{
    /// <summary> 튜토리얼 전용 임시 물약 수량, 기본 지급품과 별개이며 구간별 시작 수량 규칙을 한곳에 둔다 </summary>
    public sealed class ProloguePotionLedger
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int INITIAL_COUNT = 3;

        private int _count             = 0;
        private int _wolbaekEntryCount = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int Count             => _count;

        // 월백 구간에 처음 진입했을 때의 수량, 중단 후 재개의 시작 수량이다
        public int WolbaekEntryCount => _wolbaekEntryCount;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 숲 구간 시작이나 재개, 기존 수량에 더하지 않고 총 3개로 초기화한다 </summary>
        public void StartForest()
        {
            _count             = INITIAL_COUNT;
            _wolbaekEntryCount = 0;
        }

        /// <summary> 숲에서 월백으로 진입한다, 남긴 수량을 그대로 이어받고 진입 수량으로 기록한다 </summary>
        public void EnterWolbaek()
        {
            _wolbaekEntryCount = _count;
        }

        /// <summary> 월백 구간을 중단 후 재개한다, 사용한 물약은 진입 당시 수량으로 되돌린다 </summary>
        public void ResumeWolbaek(int storedEntryCount)
        {
            _wolbaekEntryCount = (storedEntryCount < 0) ? 0 : storedEntryCount;
            _count             = _wolbaekEntryCount;
        }

        public bool TryUse()
        {
            if (_count <= 0)
            {
                return false;
            }

            --_count;
            return true;
        }

        /// <summary> 프롤로그가 끝났다, 남은 임시 물약을 제거한다 </summary>
        public void Clear()
        {
            _count             = 0;
            _wolbaekEntryCount = 0;
        }

        /// <summary> 체력이 줄었고 물약이 남았을 때만 사용을 안내한다 </summary>
        public bool ShouldHintUse(bool isDamaged)
        {
            return (isDamaged) && (_count > 0);
        }
    }
}
