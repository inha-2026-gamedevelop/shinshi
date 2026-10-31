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
    // 방향이 있는 P01, P02, P05는 무철 전용 조준(naturalAim)을 켜 첫 조준이 최단 경로로 부드럽게 돌아 고정 시각에 대상 방향에 닿는다.
    // P04는 방향이 없는 원형이라 켜지 않고, P03과 P06은 돌진마다 예고 시작에 대상 쪽으로 바로 조준하는 연속 돌진 실행이다.
    // P07은 하드 전용 전장 공격이다. 맞은 대상을 미는 창구(IBossHitDisplacer)가 있어야만 만든다. 운영 서버에는 그 창구의 구현이 아직 없어
    // 창구 없이 만들면 밀림 규칙이 빠진 채로 시전되기 때문이다. 어떤 난이도에서 후보가 되는지는 카탈로그가 정하고 이 팩토리는 모른다.
    // 운영 서버 두뇌는 아직 이 팩토리를 쓰지 않는다. 폭주 돌진(P06)은 공용 공격 FSM이 실행 시간을 고정해 횟수별 실행 시간을 줄 수 없어
    // 운영 후보에 넣지 않는다(승인 요청은 전달 문서에 있다)
    /// <summary> 무철 2페이즈 패턴 정의로 실행 객체를 만든다, mucheol_ph2_p01~p07 </summary>
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
        public const string P07_ID = "mucheol_ph2_p07";

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                             _referenceHealth;
        private readonly MucheolPhase2Spec                 _spec;
        private readonly MucheolPhase2Spec                 _hardSpec;
        private readonly IBossVitals                       _vitals;
        private readonly IBossPatternCatalog               _catalog;
        private readonly IBossMovement                     _movement;
        private readonly IMucheolSequenceTelegraphListener _sequenceListener;
        private readonly IMucheolMultiDashListener         _dashListener;
        private readonly IBossHitDisplacer                 _displacer;
        private readonly IMucheolBattlefieldListener       _battlefieldListener;
        private readonly bool                              _shiftsBody;

        private static readonly float[] G_SingleStrike = { 0f }; // 단발 패턴의 타 지연, 실행 시작 순간 한 번이다

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 이 전투의 Href다. 지역과 난이도로 정하는 일은 서버 연결이 하며 이 클래스는 기본값을 두지 않는다
        // hardSpec은 하드 전용 ID(mucheol_hard_ph2_...)의 실행 데이터다. 하드 정의는 이지와 같은 실행 객체를 쓰되 이 데이터로 만들며, 비어 있으면 하드 정의를 만들지 않는다.
        // P07은 하드 전용이라 이지 데이터(spec)의 P07을 그대로 쓴다
        // vitals와 catalog는 연속 돌진의 강화 구간 판정에 쓴다. movement는 연속 돌진이 보스 몸체를 옮기는 창구이며 없으면 P03과 P06을 만들지 않는다
        // shiftsBody를 켜면 P01, P02, P04, P05, P07이 모션 경로대로 보스를 옮긴다(MucheolBodyShift). 이동 창구가 없으면 켜도 옮기지 않는다
        public MucheolPhase2PatternFactory(float referenceHealth, MucheolPhase2Spec spec, IBossVitals vitals,
            IBossPatternCatalog catalog, IBossMovement movement = null, IMucheolSequenceTelegraphListener sequenceListener = null,
            IMucheolMultiDashListener dashListener = null, IBossHitDisplacer displacer = null,
            IMucheolBattlefieldListener battlefieldListener = null, MucheolPhase2Spec hardSpec = null, bool shiftsBody = false)
        {
            if (referenceHealth <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(referenceHealth));
            }

            _referenceHealth     = referenceHealth;
            _spec                = spec ?? throw new ArgumentNullException(nameof(spec));
            _hardSpec            = hardSpec;
            _vitals              = vitals ?? throw new ArgumentNullException(nameof(vitals));
            _catalog             = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _movement            = movement;
            _sequenceListener    = sequenceListener;
            _dashListener        = dashListener;
            _displacer           = displacer;
            _battlefieldListener = battlefieldListener;
            _shiftsBody          = shiftsBody;
        }

        // Create가 null을 돌려줄 조건과 같아야 한다
        public bool CanCreate(PatternDefinition definition)
        {
            if (definition == null)
            {
                return false;
            }

            MucheolPhase2Spec spec = ResolveSpec(definition);
            if (spec == null)
            {
                return false;
            }

            switch (ToBaseId(definition))
            {
                case P01_ID:
                    return spec.P01 != null;
                case P02_ID:
                    return spec.P02 != null;
                case P04_ID:
                    return spec.P04 != null;
                case P05_ID:
                    return spec.P05 != null;
                case P03_ID:
                    return (spec.P03 != null) && (_movement != null);
                case P06_ID:
                    return (spec.P06 != null) && (_movement != null);
                case P07_ID:
                    return (spec.P07 != null) && (_displacer != null);
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

            MucheolPhase2Spec spec = ResolveSpec(definition);
            string            id   = ToBaseId(definition);
            switch (id)
            {
                case P01_ID:
                    return new MucheolSequencePattern(_referenceHealth, spec.P01, _sequenceListener, naturalAim: true,
                        bodyShift: CreateBodyShift(id, ToStrikeDelays(spec.P01)));
                case P02_ID:
                    return new MucheolSequencePattern(_referenceHealth, spec.P02, _sequenceListener, naturalAim: true,
                        bodyShift: CreateBodyShift(id, ToStrikeDelays(spec.P02)));
                case P04_ID:
                    return new MucheolSequencePattern(_referenceHealth, spec.P04, _sequenceListener,
                        bodyShift: CreateBodyShift(id, ToStrikeDelays(spec.P04)));
                case P05_ID:
                    return new MucheolSequencePattern(_referenceHealth, spec.P05, _sequenceListener, naturalAim: true,
                        bodyShift: CreateBodyShift(id, ToStrikeDelays(spec.P05)));
                case P03_ID:
                    return new MucheolMultiDashPattern(_referenceHealth, _movement, spec.P03, ResolveDashCount(spec.P03), _dashListener);
                case P07_ID:
                    return new MucheolBattlefieldBlastPattern(_referenceHealth, spec.P07, _displacer, _battlefieldListener,
                        CreateBodyShift(id, G_SingleStrike));
                default:
                    return new MucheolMultiDashPattern(_referenceHealth, _movement, spec.P06, ResolveDashCount(spec.P06), _dashListener);
            }
        }

        // 하드 전용 ID(mucheol_hard_ph2_...)는 이지 ID로 되돌려 같은 실행 객체를 고른다
        private static string ToBaseId(PatternDefinition definition)
        {
            return Shinshi.Data.BossPatternCatalogSO.ToMucheolBaseId(definition.DefinitionId);
        }

        // 하드 전용 ID면 하드 데이터를, 그 밖에는(P07 포함) 이지 데이터를 쓴다. 하드 데이터가 없으면 null이라 만들지 않는다
        private MucheolPhase2Spec ResolveSpec(PatternDefinition definition)
        {
            return (definition.DefinitionId != ToBaseId(definition)) ? _hardSpec : _spec;
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

        private static float[] ToStrikeDelays(MucheolSequenceSpec sequence)
        {
            var delays = new float[sequence.StepCount];
            for (int i = 0; i < delays.Length; ++i)
            {
                delays[i] = sequence.Steps[i].DelaySeconds;
            }

            return delays;
        }

        // 보스 정의가 없거나 강화 기준이 미지정이면 강화 구간이 아닌 것으로 본다
        private int ResolveDashCount(MucheolMultiDashSpec spec)
        {
            bool isEnhanced = (_catalog.TryGetBoss(MucheolPatternFactory.BOSS_ID, out var boss)) && (boss.IsEnhanced(_vitals.HealthRate));
            return spec.GetDashCount(isEnhanced);
        }
    }
}
