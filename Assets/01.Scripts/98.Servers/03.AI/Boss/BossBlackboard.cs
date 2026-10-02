// System
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Combat;

using BossDefinition    = Shinshi.Data.BossPatternCatalogSO.BossDefinition;
using EPatternGroup     = Shinshi.Data.BossPatternCatalogSO.EPatternGroup;
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

        /// <summary> 2페이즈 선택의 체력 구간 판정이 읽는다 </summary>
        public IBossVitals                Vitals        { get; set; }

        /// <summary> 2페이즈 선택이 실행 가능한 후보만 남길 때 읽는다 </summary>
        public IBossPatternFactory        Patterns      { get; set; }

        /// <summary> 직전에 예고를 시작한 패턴의 그룹, 기록이 없으면 None이고 첫 선택의 흐름 배율은 1이다 </summary>
        public EPatternGroup              LastGroup     { get; private set; }

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

        /// <summary> 예고를 실제로 시작한 패턴과 그 그룹을 기록한다, 직접 제어로 끊겨도 기록은 남는다 </summary>
        public void RecordUse(string patternId, EPatternGroup group = EPatternGroup.None)
        {
            LastGroup = group;

            for (int i = RECENT_HISTORY_SIZE - 1; i > 0; --i)
            {
                _recent[i] = _recent[i - 1];
            }

            _recent[0] = patternId;
        }

        // 데이터가 지정됐고 팩토리가 만들 수 있는 정의만 실행 가능하다. 확인 인터페이스가 없는 쪽은 가능한 것으로 본다
        /// <summary> 2페이즈 이상 선택이 후보로 쓸 수 있는 정의인가, 선택 중 null 생성 오류가 반복되지 않게 미리 거른다 </summary>
        public bool IsExecutable(PatternDefinition pattern)
        {
            if ((Catalog is IBossPatternReadiness data) && (!data.IsPatternReady(Boss.DefinitionId, pattern)))
            {
                return false;
            }

            return (!(Patterns is IBossPatternAvailability implementation)) || implementation.CanCreate(pattern);
        }

        /// <summary> 현재 페이즈에 실행 가능한 후보가 하나라도 있는가, 후보 버퍼를 덮어쓴다 </summary>
        public bool HasExecutableCandidate()
        {
            Catalog.CollectPatterns(Boss.DefinitionId, Phase, Candidates);
            for (int i = 0; i < Candidates.Count; ++i)
            {
                if (IsExecutable(Candidates[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary> 최근 사용 기록과 직전 그룹을 비운다, 페이즈 전환이 부른다 </summary>
        public void ResetHistory()
        {
            for (int i = 0; i < RECENT_HISTORY_SIZE; ++i)
            {
                _recent[i] = null;
            }

            LastGroup = EPatternGroup.None;
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
