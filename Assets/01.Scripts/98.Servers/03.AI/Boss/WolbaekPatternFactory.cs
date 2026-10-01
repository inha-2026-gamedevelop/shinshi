// System
using System;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // TODO(기웅): P01, P03~P06은 구현이 없어 null을 돌려준다. 수치가 정해지기 전에는 만들지 않는다
    /// <summary> 월백 패턴 정의로 실행 객체를 만든다, 지금은 P02만 있다 </summary>
    public sealed class WolbaekPatternFactory : IBossPatternFactory
    {
        /****************************************
        *              Constants
        ****************************************/

        public const string P02_ID = "wolbaek_p02";

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                      _referenceHealth;
        private readonly IBossFanTelegraphListener  _fanListener;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 이 전투의 Href다. 지역과 난이도로 정하는 일은 서버 연결이 하며 이 클래스는 기본값을 두지 않는다
        public WolbaekPatternFactory(float referenceHealth, IBossFanTelegraphListener fanListener = null)
        {
            if (referenceHealth <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(referenceHealth));
            }

            _referenceHealth = referenceHealth;
            _fanListener     = fanListener;
        }

        public IBossPattern Create(PatternDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            if (definition.DefinitionId == P02_ID)
            {
                return new BossFanBlastPattern(_referenceHealth, _fanListener);
            }

            return null;
        }
    }
}
