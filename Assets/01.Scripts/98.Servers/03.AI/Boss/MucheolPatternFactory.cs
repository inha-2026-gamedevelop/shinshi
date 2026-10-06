using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 월백 뼈대(두뇌, 페이즈 FSM, 선택, 피해 게이트)를 그대로 쓰고 패턴만 비워 둔 산문지기 무철의 팩토리다.
    // 패턴을 하나도 만들지 않으므로 두뇌는 모든 정의를 구현 없음으로 보고 건너뛴다. 패턴을 구현하면 ID 상수와 분기를 여기에 더한다
    /// <summary> 산문지기 무철의 패턴 팩토리, 지금은 만들 수 있는 패턴이 없다 </summary>
    public sealed class MucheolPatternFactory : IBossPatternFactory, IBossPatternAvailability
    {
        /****************************************
        *              Constants
        ****************************************/

        public const string BOSS_ID = "boss_mucheol";

        /****************************************
        *                Methods
        ****************************************/

        // TODO(기웅): 무철 패턴 기획이 정해지면 정의 ID별 실행 객체를 만든다
        public bool CanCreate(PatternDefinition definition)
        {
            return false;
        }

        public IBossPattern Create(PatternDefinition definition)
        {
            return null;
        }
    }
}
