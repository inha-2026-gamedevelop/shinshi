// System
using System;

// 프로젝트 내부
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 월백 뼈대(두뇌, 페이즈 FSM, 선택, 피해 게이트)를 그대로 쓰는 산문지기 무철의 팩토리다.
    // 지금 만드는 것은 P01~P06뿐이고 나머지 정의는 null이라 두뇌가 구현 없음으로 알리고 다음 선택으로 넘어간다
    /// <summary> 무철 패턴 정의로 실행 객체를 만든다, 1페이즈는 P01~P06이 있다 </summary>
    public sealed class MucheolPatternFactory : IBossPatternFactory, IBossPatternAvailability
    {
        /****************************************
        *              Constants
        ****************************************/

        public const string BOSS_ID = "boss_mucheol";
        public const string P01_ID  = "mucheol_p01";
        public const string P02_ID  = "mucheol_p02";
        public const string P03_ID  = "mucheol_p03";
        public const string P04_ID  = "mucheol_p04";
        public const string P05_ID  = "mucheol_p05";
        public const string P06_ID  = "mucheol_p06";

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                        _referenceHealth;
        private readonly IBossFanTelegraphListener    _fanListener;
        private readonly IBossMovement                _movement;
        private readonly IBossDashTelegraphListener   _dashListener;
        private readonly IBossCircleTelegraphListener _circleListener;
        private readonly MucheolComboSpec             _comboSpec;
        private readonly IBossComboTelegraphListener  _comboListener;
        private readonly MucheolChargeSpec            _chargeSpec;
        private readonly IBossChargeStructureQuery    _chargeStructures;
        private readonly IBossChargeOutcomeListener   _chargeListener;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 이 전투의 Href다. 지역과 난이도로 정하는 일은 서버 연결이 하며 이 클래스는 기본값을 두지 않는다
        // movement는 돌진이 보스 몸체를 옮기는 창구다. 서버 몸체가 구현해 넘기며, 비어 있으면 P03을 만들지 않는다
        // comboSpec은 P05 콤보의 타별 형태와 피해다. PatternDefinition에 타별 값이 없어 따로 받으며, 비어 있으면 P05를 만들지 않는다
        // chargeSpec은 P06 돌진의 충돌 회복이다. 이동 창구와 함께 있어야 P06을 만들고, chargeStructures는 구조물 조회라 없으면 벽에만 막힌다
        public MucheolPatternFactory(float referenceHealth, IBossFanTelegraphListener fanListener = null,
            IBossMovement movement = null, IBossDashTelegraphListener dashListener = null,
            IBossCircleTelegraphListener circleListener = null, MucheolComboSpec comboSpec = null,
            IBossComboTelegraphListener comboListener = null, MucheolChargeSpec chargeSpec = null,
            IBossChargeStructureQuery chargeStructures = null, IBossChargeOutcomeListener chargeListener = null)
        {
            if (referenceHealth <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(referenceHealth));
            }

            _referenceHealth  = referenceHealth;
            _fanListener      = fanListener;
            _movement         = movement;
            _dashListener     = dashListener;
            _circleListener   = circleListener;
            _comboSpec        = comboSpec;
            _comboListener    = comboListener;
            _chargeSpec       = chargeSpec;
            _chargeStructures = chargeStructures;
            _chargeListener   = chargeListener;
        }

        // Create가 null을 돌려줄 조건과 같아야 한다
        public bool CanCreate(PatternDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            string id = definition.DefinitionId;
            if (id == P03_ID)
            {
                return _movement != null;
            }

            if (id == P05_ID)
            {
                return _comboSpec != null;
            }

            if (id == P06_ID)
            {
                return (_movement != null) && (_chargeSpec != null);
            }

            return (id == P01_ID) || (id == P02_ID) || (id == P04_ID);
        }

        // P01(팔 휘두르기)과 P02(양손 내려찍기)는 월백 P02와 같은 부채꼴 예고, 방향 고정, 단발 타격이고
        // P03(어깨 돌진)은 월백 P01과 같은 단발 직선 돌진, P04(발구르기 충격파)는 월백 P03과 같은 보스 중심 원형 단발이다.
        // 사거리, 각도, 거리, 폭, 반경, 시간, 피해는 정의 값이 정한다. P05(2연타)는 한 시전에서 두 타를 관리하는 무철 전용 실행이고,
        // P06(뿔 들이받기)은 같은 돌진 실행을 감싸 벽과 구조물 충돌의 결과를 정하는 무철 전용 실행이다
        public IBossPattern Create(PatternDefinition definition)
        {
            if (!CanCreate(definition))
            {
                return null;
            }

            if (definition.DefinitionId == P03_ID)
            {
                return new BossDashPattern(_referenceHealth, _movement, _dashListener);
            }

            if (definition.DefinitionId == P04_ID)
            {
                return new BossCircleBlastPattern(_referenceHealth, _circleListener);
            }

            if (definition.DefinitionId == P05_ID)
            {
                return new MucheolComboPattern(_referenceHealth, _comboSpec, _comboListener);
            }

            if (definition.DefinitionId == P06_ID)
            {
                return new MucheolChargePattern(_referenceHealth, _movement, _chargeSpec, _chargeStructures, _dashListener,
                    _chargeListener);
            }

            return new BossFanBlastPattern(_referenceHealth, _fanListener);
        }
    }
}
