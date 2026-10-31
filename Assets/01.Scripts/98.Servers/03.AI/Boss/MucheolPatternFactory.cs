// System
using System;

// 프로젝트 내부
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 월백 뼈대(두뇌, 페이즈 FSM, 선택, 피해 게이트)를 그대로 쓰는 산문지기 무철의 팩토리다.
    // 지금 만드는 것은 P01~P06뿐이고 나머지 정의는 null이라 두뇌가 구현 없음으로 알리고 다음 선택으로 넘어간다
    /// <summary> 무철 패턴 정의로 실행 객체를 만든다, 1페이즈는 P01~P06이 있다 </summary>
    public sealed class MucheolPatternFactory : IBossPatternFactory, IBossPatternAvailability, IBossChargeCastTrackerSource
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
        private readonly MucheolComboSpec             _hardComboSpec;
        private readonly IBossComboTelegraphListener  _comboListener;
        private readonly MucheolChargeSpec            _chargeSpec;
        private readonly IBossChargeStructureQuery    _chargeStructures;
        private readonly IBossChargeOutcomeListener   _chargeListener;
        private readonly bool                         _shiftsBody;

        private static readonly float[] G_SingleStrike = { 0f }; // 단발 패턴의 타 지연, 실행 시작 순간 한 번이다

        private IBossChargeCastTracker _chargeTracker;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 연결된 시전 추적 수신자, 연결되지 않았으면 null이다. 기믹이 자신이 연결됐는지 확인하는 데 쓴다 </summary>
        public IBossChargeCastTracker ChargeCastTracker => _chargeTracker;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 이 전투의 Href다. 지역과 난이도로 정하는 일은 서버 연결이 하며 이 클래스는 기본값을 두지 않는다
        // movement는 돌진이 보스 몸체를 옮기는 창구다. 서버 몸체가 구현해 넘기며, 비어 있으면 P03을 만들지 않는다
        // comboSpec은 P05 콤보의 타별 형태와 피해다. PatternDefinition에 타별 값이 없어 따로 받으며, 비어 있으면 P05를 만들지 않는다
        // chargeSpec은 P06 돌진의 충돌 회복이다. 이동 창구와 함께 있어야 P06을 만들고, chargeStructures는 구조물 조회라 없으면 벽에만 막힌다
        // hardComboSpec은 하드 P05 콤보의 타 목록이며, 하드 정의(mucheol_hard_...)는 이지와 같은 실행 객체를 쓰되 이 목록으로 만든다. 비어 있으면 하드 P05를 만들지 않는다
        // shiftsBody를 켜면 P01, P02, P04, P05가 모션 경로대로 보스를 옮긴다(MucheolBodyShift). 이동 창구가 없으면 켜도 옮기지 않는다.
        // 기본값 꺼짐은 보스가 제자리에서 치는 기존 동작이고 운영 서버와 무철 시험 구동기가 켠다
        public MucheolPatternFactory(float referenceHealth, IBossFanTelegraphListener fanListener = null,
            IBossMovement movement = null, IBossDashTelegraphListener dashListener = null,
            IBossCircleTelegraphListener circleListener = null, MucheolComboSpec comboSpec = null,
            IBossComboTelegraphListener comboListener = null, MucheolChargeSpec chargeSpec = null,
            IBossChargeStructureQuery chargeStructures = null, IBossChargeOutcomeListener chargeListener = null,
            MucheolComboSpec hardComboSpec = null, bool shiftsBody = false)
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
            _hardComboSpec    = hardComboSpec;
            _comboListener    = comboListener;
            _chargeSpec       = chargeSpec;
            _chargeStructures = chargeStructures;
            _chargeListener   = chargeListener;
            _shiftsBody       = shiftsBody;
        }

        // 시전 추적 수신자(기믹)는 생성자가 아니라 따로 연결한다. 기믹이 팩토리로 P06을 고를 수 있는지 확인하고 팩토리가 기믹에 시전을 알리는
        // 서로를 아는 관계라 한쪽을 먼저 만들 수 없기 때문이다. 보스전이 시작되기 전에 한 번 연결하며, 이후 만드는 P06 시전부터 알린다
        public void SetChargeCastTracker(IBossChargeCastTracker tracker)
        {
            _chargeTracker = tracker;
        }

        // Create가 null을 돌려줄 조건과 같아야 한다
        public bool CanCreate(PatternDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            string id = ToBaseId(definition);
            if (id == P03_ID)
            {
                return _movement != null;
            }

            if (id == P05_ID)
            {
                return ResolveComboSpec(definition) != null;
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
        // P06(뿔 들이받기)은 같은 돌진 실행을 감싸 벽과 구조물 충돌의 결과를 정하는 무철 전용 실행이다.
    // 방향이 있는 P01, P02, P03, P05는 무철 전용 조준(naturalAim)을 켜 최단 경로로 부드럽게 돌아 고정 시각에 대상 방향에 닿고,
    // P06은 예고 시작부터 대상 쪽으로 바로 조준한다. P04는 방향이 없는 원형이다
        public IBossPattern Create(PatternDefinition definition)
        {
            if (!CanCreate(definition))
            {
                return null;
            }

            string id = ToBaseId(definition);
            if (id == P03_ID)
            {
                return new BossDashPattern(_referenceHealth, _movement, _dashListener, naturalAim: true);
            }

            if (id == P04_ID)
            {
                return new BossCircleBlastPattern(_referenceHealth, _circleListener, CreateBodyShift(id, G_SingleStrike));
            }

            if (id == P05_ID)
            {
                MucheolComboSpec combo = ResolveComboSpec(definition);
                return new MucheolComboPattern(_referenceHealth, combo, _comboListener, naturalAim: true,
                    bodyShift: CreateBodyShift(id, ToStrikeDelays(combo)));
            }

            if (id == P06_ID)
            {
                return new MucheolChargePattern(_referenceHealth, _movement, _chargeSpec, _chargeStructures, _dashListener,
                    _chargeListener, _chargeTracker);
            }

            return new BossFanBlastPattern(_referenceHealth, _fanListener, naturalAim: true,
                bodyShift: CreateBodyShift(id, G_SingleStrike));
        }

        // 켜져 있고 이동 창구가 있을 때만 만든다. 키가 기준으로 삼는 타가 타 목록에 없으면(타 수가 다른 데이터) 옮기지 않는다
        private MucheolBodyShift CreateBodyShift(string baseId, float[] strikeDelays)
        {
            if ((!_shiftsBody) || (_movement == null))
            {
                return null;
            }

            if ((!MucheolBodyShiftCatalog.TryGet(baseId, out MucheolBodyShiftSpec spec)) || (spec.MaxStrikeIndex >= strikeDelays.Length))
            {
                return null;
            }

            return new MucheolBodyShift(_movement, spec, strikeDelays);
        }

        private static float[] ToStrikeDelays(MucheolComboSpec combo)
        {
            var delays = new float[combo.HitCount];
            for (int i = 0; i < delays.Length; ++i)
            {
                delays[i] = combo.Hits[i].DelaySeconds;
            }

            return delays;
        }

        // 하드 정의(mucheol_hard_...)는 이지와 같은 번호의 실행 객체를 쓰므로 ID를 이지 ID로 되돌려 비교한다
        private static string ToBaseId(PatternDefinition definition)
        {
            return Shinshi.Data.BossPatternCatalogSO.ToMucheolBaseId(definition.DefinitionId);
        }

        // 하드 전용 ID(mucheol_hard_...)는 하드 콤보 목록으로, 그 밖에는 이지 목록으로 P05를 만든다. 사거리가 정의와 같은 값이어야 해서 목록을 나눠 둔다
        private MucheolComboSpec ResolveComboSpec(PatternDefinition definition)
        {
            return (definition.DefinitionId != ToBaseId(definition)) ? _hardComboSpec : _comboSpec;
        }
    }
}
