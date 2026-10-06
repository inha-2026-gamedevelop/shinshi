// System
using System;

// 프로젝트 내부
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 무철 2페이즈 여섯 패턴의 팩토리다. 1페이즈 팩토리(MucheolPatternFactory)는 고치지 않고 정의 ID가 다른 이 팩토리를 따로 둔다.
    // 두 팩토리를 한 두뇌에 넣는 일은 MucheolPatternFactoryChain이 한다.
    // 연속 돌진의 횟수는 시전을 만드는 순간의 체력으로 정한다. 시전은 만들어진 직후 시작되므로 시전 시작 시점의 체력이다.
    // 강화 구간 기준은 보스 정의의 EnhancedHealthRate를 카탈로그에서 읽어 선택 가중치의 강화 구간과 같은 기준을 쓴다.
    // 운영 서버 두뇌는 아직 이 팩토리를 쓰지 않는다. 폭주 돌진(P06)은 공용 공격 FSM이 실행 시간을 고정해 횟수별 실행 시간을 줄 수 없어
    // 운영 후보에 넣지 않는다(승인 요청은 전달 문서에 있다)
    /// <summary> 무철 2페이즈 패턴 정의로 실행 객체를 만든다, mucheol_ph2_p01~p06 </summary>
    public sealed class MucheolPhase2PatternFactory : IBossPatternFactory, IBossPatternAvailability
    {
        /****************************************
        *              Constants
        ****************************************/

        public const string P01_ID = "mucheol_ph2_p01";
        public const string P02_ID = "mucheol_ph2_p02";
        public const string P03_ID = "mucheol_ph2_p03";
        public const string P04_ID = "mucheol_ph2_p04";
        public const string P05_ID = "mucheol_ph2_p05";
        public const string P06_ID = "mucheol_ph2_p06";

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                             _referenceHealth;
        private readonly MucheolPhase2Spec                 _spec;
        private readonly IBossVitals                       _vitals;
        private readonly IBossPatternCatalog               _catalog;
        private readonly IBossMovement                     _movement;
        private readonly IMucheolSequenceTelegraphListener _sequenceListener;
        private readonly IMucheolMultiDashListener         _dashListener;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 이 전투의 Href다. 지역과 난이도로 정하는 일은 서버 연결이 하며 이 클래스는 기본값을 두지 않는다
        // vitals와 catalog는 연속 돌진의 강화 구간 판정에 쓴다. movement는 연속 돌진이 보스 몸체를 옮기는 창구이며 없으면 P03과 P06을 만들지 않는다
        public MucheolPhase2PatternFactory(float referenceHealth, MucheolPhase2Spec spec, IBossVitals vitals,
            IBossPatternCatalog catalog, IBossMovement movement = null, IMucheolSequenceTelegraphListener sequenceListener = null,
            IMucheolMultiDashListener dashListener = null)
        {
            if (referenceHealth <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(referenceHealth));
            }

            _referenceHealth  = referenceHealth;
            _spec             = spec ?? throw new ArgumentNullException(nameof(spec));
            _vitals           = vitals ?? throw new ArgumentNullException(nameof(vitals));
            _catalog          = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _movement         = movement;
            _sequenceListener = sequenceListener;
            _dashListener     = dashListener;
        }

        // Create가 null을 돌려줄 조건과 같아야 한다
        public bool CanCreate(PatternDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            switch (definition.DefinitionId)
            {
                case P01_ID:
                    return _spec.P01 != null;
                case P02_ID:
                    return _spec.P02 != null;
                case P04_ID:
                    return _spec.P04 != null;
                case P05_ID:
                    return _spec.P05 != null;
                case P03_ID:
                    return (_spec.P03 != null) && (_movement != null);
                case P06_ID:
                    return (_spec.P06 != null) && (_movement != null);
                default:
                    return false;
            }
        }

        public IBossPattern Create(PatternDefinition definition)
        {
            if (!CanCreate(definition))
            {
                return null;
            }

            switch (definition.DefinitionId)
            {
                case P01_ID:
                    return new MucheolSequencePattern(_referenceHealth, _spec.P01, _sequenceListener);
                case P02_ID:
                    return new MucheolSequencePattern(_referenceHealth, _spec.P02, _sequenceListener);
                case P04_ID:
                    return new MucheolSequencePattern(_referenceHealth, _spec.P04, _sequenceListener);
                case P05_ID:
                    return new MucheolSequencePattern(_referenceHealth, _spec.P05, _sequenceListener);
                case P03_ID:
                    return new MucheolMultiDashPattern(_referenceHealth, _movement, _spec.P03, ResolveDashCount(_spec.P03), _dashListener);
                default:
                    return new MucheolMultiDashPattern(_referenceHealth, _movement, _spec.P06, ResolveDashCount(_spec.P06), _dashListener);
            }
        }

        // 보스 정의가 없거나 강화 기준이 미지정이면 강화 구간이 아닌 것으로 본다
        private int ResolveDashCount(MucheolMultiDashSpec spec)
        {
            bool isEnhanced = (_catalog.TryGetBoss(MucheolPatternFactory.BOSS_ID, out var boss)) && (boss.IsEnhanced(_vitals.HealthRate));
            return spec.GetDashCount(isEnhanced);
        }
    }
}
