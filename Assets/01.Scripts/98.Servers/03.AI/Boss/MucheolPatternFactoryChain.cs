// 프로젝트 내부
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 두뇌는 팩토리 하나만 받는다. 무철은 1페이즈와 2페이즈 팩토리를 정의 ID가 다른 별도 클래스로 두었으므로 이 묶음이 순서대로 시도한다.
    // 두 팩토리가 같은 정의를 만들 수 있게 되면 앞의 것이 이긴다
    /// <summary> 무철 1페이즈 팩토리와 2페이즈 팩토리를 한 팩토리로 묶는다 </summary>
    public sealed class MucheolPatternFactoryChain : IBossPatternFactory, IBossPatternAvailability
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly MucheolPatternFactory       _phase1;
        private readonly MucheolPhase2PatternFactory _phase2;

        /****************************************
        *                Methods
        ****************************************/

        public MucheolPatternFactoryChain(MucheolPatternFactory phase1, MucheolPhase2PatternFactory phase2)
        {
            _phase1 = phase1;
            _phase2 = phase2;
        }

        public bool CanCreate(PatternDefinition definition)
        {
            return _phase1.CanCreate(definition) || _phase2.CanCreate(definition);
        }

        public IBossPattern Create(PatternDefinition definition)
        {
            if (_phase1.CanCreate(definition))
            {
                return _phase1.Create(definition);
            }

            return _phase2.Create(definition);
        }
    }
}
