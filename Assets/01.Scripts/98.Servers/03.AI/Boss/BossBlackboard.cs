// System
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Combat;

using BossDefinition    = Shinshi.Data.BossPatternCatalogSO.BossDefinition;
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    /// <summary> 선택 BT가 평가 중에 읽고 쓰는 값 모음 </summary>
    public sealed class BossBlackboard
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int RECENT_HISTORY_SIZE = 3;

        private readonly string[] _recent = new string[RECENT_HISTORY_SIZE];

        /****************************************
        *              Properties
        ****************************************/

        // 최근 사용 배율 표의 마지막 열, 최근 3회 안에 없다는 뜻이다
        public static int RecentSlotNone => RECENT_HISTORY_SIZE;

        public double                     ServerTime    { get; set; }
        public ICombatTarget              Self          { get; set; }
        public BossDefinition             Boss          { get; set; }
        public int                        Phase         { get; set; }
        public ICombatTargetQuery         Targets       { get; set; }
        public IBossEffectLifetime        Effects       { get; set; }
        public IBossPatternCatalog        Catalog       { get; set; }
        public IBossRandom                Random        { get; set; }

        /// <summary> 이 서버 시각 전에는 새 선택을 하지 않는다 </summary>
        public double                     NextSelectionTime { get; set; }

        // 아래 값은 한 번의 평가 안에서만 유효하다. 참조를 보관하지 않는다
        public ICombatTarget              Target          { get; set; }
        public int                        TargetCombatId  { get; set; }
        public int                        DistanceBand    { get; set; }
        public int                        DirectionBand   { get; set; }
        public int                        NearbyBand      { get; set; }
        public PatternDefinition          SelectedPattern { get; set; }

        public List<ICombatTarget>        TargetBuffer { get; } = new List<ICombatTarget>();
        public List<PatternDefinition>    Candidates   { get; } = new List<PatternDefinition>();
        public List<float>                Weights      { get; } = new List<float>();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 예고를 실제로 시작한 패턴을 기록한다, 직접 제어로 끊겨도 기록은 남는다 </summary>
        public void RecordUse(string patternId)
        {
            for (int i = RECENT_HISTORY_SIZE - 1; i > 0; --i)
            {
                _recent[i] = _recent[i - 1];
            }

            _recent[0] = patternId;
        }

        /// <summary> 직전 0, 2회 전 1, 3회 전 2, 없으면 RecentSlotNone, 같은 ID가 여럿이면 가장 최근 것 </summary>
        public int GetRecentSlot(string patternId)
        {
            for (int i = 0; i < RECENT_HISTORY_SIZE; ++i)
            {
                if (_recent[i] == patternId)
                {
                    return i;
                }
            }

            return RECENT_HISTORY_SIZE;
        }
    }
}
