// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 선택 창이 바깥에 알리는 값이다. 카탈로그 데이터를 그대로 옮긴 것이고 해금이나 입장 가능 여부는 담지 않는다.
    // 그 판정은 서버가 한다. AvailableInTrial은 카탈로그가 적은 체험판 포함 여부일 뿐 입장 허가가 아니다
    /// <summary> 보스 ID와 난이도 선택의 결과, 정확히 대응하는 보스전 ID를 함께 담는다 </summary>
    public readonly struct BossBattleSelection
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly EBossBattleSelectState _state;
        private readonly string                 _bossId;
        private readonly EBossDifficulty        _difficulty;
        private readonly string                 _battleId;
        private readonly bool                   _availableInTrial;

        /****************************************
        *              Properties
        ****************************************/

        public EBossBattleSelectState State => _state;

        /// <summary> 보스전 ID에 정확히 대응한다 </summary>
        public bool IsResolved => _state == EBossBattleSelectState.Resolved;

        public string BossId => _bossId;

        public EBossDifficulty Difficulty => _difficulty;

        /// <summary> 진행 카탈로그의 보스전 ID, IsResolved가 아니면 비어 있다 </summary>
        public string BattleId => _battleId;

        public bool AvailableInTrial => _availableInTrial;

        /****************************************
        *                Methods
        ****************************************/

        public BossBattleSelection(EBossBattleSelectState state, string bossId, EBossDifficulty difficulty, string battleId,
            bool availableInTrial)
        {
            _state            = state;
            _bossId           = bossId;
            _difficulty       = difficulty;
            _battleId         = battleId;
            _availableInTrial = availableInTrial;
        }
    }
}
