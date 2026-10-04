// System
using System;

// 프로젝트 내부
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 월백 뼈대(두뇌, 페이즈 FSM, 선택, 피해 게이트)를 그대로 쓰는 산문지기 무철의 팩토리다.
    // 지금 만드는 것은 P01, P02뿐이고 나머지 정의는 null이라 두뇌가 구현 없음으로 알리고 다음 선택으로 넘어간다
    /// <summary> 무철 패턴 정의로 실행 객체를 만든다, 1페이즈는 P01, P02가 있다 </summary>
    public sealed class MucheolPatternFactory : IBossPatternFactory, IBossPatternAvailability
    {
        /****************************************
        *              Constants
        ****************************************/

        public const string BOSS_ID = "boss_mucheol";
        public const string P01_ID  = "mucheol_p01";
        public const string P02_ID  = "mucheol_p02";

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                     _referenceHealth;
        private readonly IBossFanTelegraphListener _fanListener;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 이 전투의 Href다. 지역과 난이도로 정하는 일은 서버 연결이 하며 이 클래스는 기본값을 두지 않는다
        public MucheolPatternFactory(float referenceHealth, IBossFanTelegraphListener fanListener = null)
        {
            if (referenceHealth <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(referenceHealth));
            }

            _referenceHealth = referenceHealth;
            _fanListener     = fanListener;
        }

        // Create가 null을 돌려줄 조건과 같아야 한다
        public bool CanCreate(PatternDefinition definition)
        {
            return (definition != null) && ((definition.DefinitionId == P01_ID) || (definition.DefinitionId == P02_ID));
        }

        // P01(팔 휘두르기)과 P02(양손 내려찍기)는 월백 P02와 같은 부채꼴 예고, 방향 고정, 단발 타격이고
        // 사거리, 각도, 시간, 피해는 정의 값이 정한다. 무철만의 동작이 생기기 전까지 별도 클래스를 두지 않는다
        public IBossPattern Create(PatternDefinition definition)
        {
            if (!CanCreate(definition))
            {
                return null;
            }

            return new BossFanBlastPattern(_referenceHealth, _fanListener);
        }
    }
}
