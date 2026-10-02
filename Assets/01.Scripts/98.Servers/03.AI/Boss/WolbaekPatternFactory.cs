// System
using System;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    /// <summary> 월백 패턴 정의로 실행 객체를 만든다, 1페이즈 여섯 패턴(P01~P06)이 모두 있다 </summary>
    public sealed class WolbaekPatternFactory : IBossPatternFactory
    {
        /****************************************
        *              Constants
        ****************************************/

        public const string P01_ID = "wolbaek_p01";
        public const string P02_ID = "wolbaek_p02";
        public const string P03_ID = "wolbaek_p03";
        public const string P04_ID = "wolbaek_p04";
        public const string P05_ID = "wolbaek_p05";
        public const string P06_ID = "wolbaek_p06";

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                          _referenceHealth;
        private readonly IBossFanTelegraphListener      _fanListener;
        private readonly IBossCircleTelegraphListener   _circleListener;
        private readonly IBossLineTelegraphListener     _lineListener;
        private readonly IBossDashTelegraphListener     _dashListener;
        private readonly IBossMovement                  _movement;
        private readonly IBossProjectileLauncher        _projectiles;
        private readonly IBossProjectileTelegraphListener _projectileListener;
        private readonly IBossAreaHost                  _areas;
        private readonly IBossAreaTelegraphListener     _areaListener;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 이 전투의 Href다. 지역과 난이도로 정하는 일은 서버 연결이 하며 이 클래스는 기본값을 두지 않는다
        // movement는 돌진이 보스 몸체를 옮기는 창구다. 서버 몸체가 구현해 넘기며, 비어 있으면 P01을 만들지 않는다
        // projectiles는 투사체 발사 창구(지속물 관리자)다. 비어 있으면 P04를 만들지 않는다
        // areas는 장판 예약과 활성 창구(장판 관리자)다. 비어 있으면 P05를 만들지 않는다
        public WolbaekPatternFactory(float referenceHealth, IBossFanTelegraphListener fanListener = null,
            IBossCircleTelegraphListener circleListener = null, IBossLineTelegraphListener lineListener = null,
            IBossDashTelegraphListener dashListener = null, IBossMovement movement = null,
            IBossProjectileLauncher projectiles = null, IBossProjectileTelegraphListener projectileListener = null,
            IBossAreaHost areas = null, IBossAreaTelegraphListener areaListener = null)
        {
            if (referenceHealth <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(referenceHealth));
            }

            _referenceHealth = referenceHealth;
            _fanListener     = fanListener;
            _circleListener  = circleListener;
            _lineListener    = lineListener;
            _dashListener    = dashListener;
            _movement        = movement;
            _projectiles     = projectiles;
            _projectileListener = projectileListener;
            _areas              = areas;
            _areaListener       = areaListener;
        }

        public IBossPattern Create(PatternDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

            // 이동 창구 없이는 돌진을 만들 수 없다. 만들지 않으면 두뇌가 구현 없음으로 알리고 다음 선택으로 넘어간다
            if (definition.DefinitionId == P01_ID)
            {
                return (_movement == null) ? null : new BossDashPattern(_referenceHealth, _movement, _dashListener);
            }

            if (definition.DefinitionId == P02_ID)
            {
                return new BossFanBlastPattern(_referenceHealth, _fanListener);
            }

            if (definition.DefinitionId == P03_ID)
            {
                return new BossCircleBlastPattern(_referenceHealth, _circleListener);
            }

            // 발사 창구 없이는 투사체를 낼 수 없다. 만들지 않으면 두뇌가 구현 없음으로 알리고 다음 선택으로 넘어간다
            if (definition.DefinitionId == P04_ID)
            {
                return (_projectiles == null) ? null : new BossProjectilePattern(_referenceHealth, _projectiles, _projectileListener);
            }

            // 장판 창구 없이는 장판을 만들 수 없다. 만들지 않으면 두뇌가 구현 없음으로 알리고 다음 선택으로 넘어간다
            if (definition.DefinitionId == P05_ID)
            {
                return (_areas == null) ? null : new BossAreaPattern(_referenceHealth, _areas, _areaListener);
            }

            if (definition.DefinitionId == P06_ID)
            {
                return new BossLineBlastPattern(_referenceHealth, _lineListener);
            }

            return null;
        }
    }
}
