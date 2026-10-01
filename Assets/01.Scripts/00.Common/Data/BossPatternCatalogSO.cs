// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> 보스 정의와 보스 공격 패턴의 문맥 배율, 형태, 회복, 방어, 제어 창. 수치 0은 미지정이며 보스 담당이 지정한다 </summary>
    [CreateAssetMenu(fileName = "BossPatternCatalog", menuName = "Shinshi/BossPatternCatalogSO")]
    public class BossPatternCatalogSO : GameDataSO
    {
        /// <summary> 보스 한 종의 전장, 체력, 패턴 선택 문맥의 경계 </summary>
        [Serializable]
        public class BossDefinition : IDefinition
        {
            /****************************************
            *                Fields
            ****************************************/

            // 수치 필드는 0이 미지정이다. 보스 담당이 지정하기 전에는 IsTuned가 거짓이라 보스전을 열지 않는다
            [SerializeField] private string _definitionId;
            [SerializeField] private string _displayKey;
            [SerializeField] private float  _arenaDiameter;              // m, 원형 전장
            [SerializeField] private float  _healthRef;                  // 지역 고정 권장 공격력 Aref의 배수, 시작 총체력
            [SerializeField] private float  _defense;                    // 피해 감소 공식에 넣는 방어
            [SerializeField] private float  _phaseTransitionHealthRate;  // 이 체력 비율에 닿으면 다음 페이즈 전환을 요청한다
            [SerializeField] private float  _openingDelaySeconds;        // 초, 전투 시작 뒤 첫 패턴 선택까지
            [SerializeField] private float  _nearDistanceMax;            // m, 이 거리 미만이 근거리
            [SerializeField] private float  _midDistanceMax;             // m, 이 거리 미만이 중거리, 이상은 원거리
            [SerializeField] private float  _frontAngleMax;              // 도, 정면 대비 절대각이 이 값 이하면 전방
            [SerializeField] private float  _sideAngleMax;               // 도, 이 값 이하면 측면, 초과는 후방
            [SerializeField] private float  _nearbyRadius;               // m, 주변 인원을 세는 반경
            [SerializeField] private float  _emptyWeightRetrySeconds;    // 초, 가중치 합이 0일 때 다시 조회하기까지
            [SerializeField] private float  _directControlMaxSeconds;    // 초, 한 패턴에 한 번 받는 직접 제어의 최대 길이
            [SerializeField] private float  _directControlImmuneSeconds; // 초, 직접 제어가 성공한 뒤 추가 직접 제어를 받지 않는 시간

            public string DefinitionId               => _definitionId;
            public string DisplayKey                 => _displayKey;
            public float  ArenaDiameter              => _arenaDiameter;
            public float  HealthRef                  => _healthRef;
            public float  Defense                    => _defense;
            public float  PhaseTransitionHealthRate  => _phaseTransitionHealthRate;
            public float  OpeningDelaySeconds        => _openingDelaySeconds;
            public float  NearDistanceMax            => _nearDistanceMax;
            public float  MidDistanceMax             => _midDistanceMax;
            public float  FrontAngleMax              => _frontAngleMax;
            public float  SideAngleMax               => _sideAngleMax;
            public float  NearbyRadius               => _nearbyRadius;
            public float  EmptyWeightRetrySeconds    => _emptyWeightRetrySeconds;
            public float  DirectControlMaxSeconds    => _directControlMaxSeconds;
            public float  DirectControlImmuneSeconds => _directControlImmuneSeconds;

            /****************************************
            *                Methods
            ****************************************/

            /// <summary> 수치를 모두 미지정으로 둔 보스 정의 </summary>
            public BossDefinition(string definitionId)
                : this(definitionId, UNSET, UNSET, UNSET,
                    UNSET, UNSET, UNSET, UNSET,
                    UNSET, UNSET, UNSET, UNSET,
                    UNSET, UNSET)
            {
            }

            public BossDefinition(string definitionId, float arenaDiameter, float healthRef, float defense,
                float phaseTransitionHealthRate, float openingDelaySeconds, float nearDistanceMax, float midDistanceMax,
                float frontAngleMax, float sideAngleMax, float nearbyRadius, float emptyWeightRetrySeconds,
                float directControlMaxSeconds, float directControlImmuneSeconds)
            {
                _definitionId               = definitionId;
                _displayKey                 = definitionId + DISPLAY_KEY_SUFFIX;
                _arenaDiameter              = arenaDiameter;
                _healthRef                  = healthRef;
                _defense                    = defense;
                _phaseTransitionHealthRate  = phaseTransitionHealthRate;
                _openingDelaySeconds        = openingDelaySeconds;
                _nearDistanceMax            = nearDistanceMax;
                _midDistanceMax             = midDistanceMax;
                _frontAngleMax              = frontAngleMax;
                _sideAngleMax               = sideAngleMax;
                _nearbyRadius               = nearbyRadius;
                _emptyWeightRetrySeconds    = emptyWeightRetrySeconds;
                _directControlMaxSeconds    = directControlMaxSeconds;
                _directControlImmuneSeconds = directControlImmuneSeconds;
            }
        }

        /// <summary> 보스 패턴 하나의 선택 가중치, 시간축, 형태, 피해, 방어와 제어 창 </summary>
        [Serializable]
        public class PatternDefinition : IDefinition
        {
            /****************************************
            *                Fields
            ****************************************/

            // 수치 필드와 형태의 크기 값은 0이 미지정이다. 형태 종류, 방어 가능 여부, 제어 단계는 기획에서 정한 범주라 미리 둔다
            [SerializeField] private string                         _definitionId;
            [SerializeField] private string                         _displayKey;
            [SerializeField] private string                         _bossId;
            [SerializeField] private int                            _phase;                 // 1부터, 이 페이즈에서만 후보가 된다

            [SerializeField] private float                          _baseWeight;
            [SerializeField] private float[]                        _distanceMultipliers;   // 근, 중, 원
            [SerializeField] private float[]                        _directionMultipliers;  // 전, 측, 후
            [SerializeField] private float[]                        _nearbyMultipliers;     // 주변 1, 2, 3명 이상
            [SerializeField] private float[]                        _recentUseMultipliers;  // 직전, 2회 전, 3회 전, 최근 3회 안에 없음

            [SerializeField] private float                          _telegraphSeconds;      // 초, 예고 시작부터 발동까지
            [SerializeField] private float                          _trackingSeconds;       // 초, 예고 시작부터 방향, 위치 고정까지
            [SerializeField] private float                          _turnRate;              // 도/초, 고정 전까지 타깃을 따라 도는 속도
            [SerializeField] private float                          _activeSeconds;         // 초, 보스가 발동에 묶이는 시간, 즉발이면 0
            [SerializeField] private float                          _recoverySeconds;       // 초, 발동 뒤 다음 선택까지
            [SerializeField] private EnemyCatalogSO.AttackShapeSpec _shape;
            [SerializeField] private float                          _damageRef;             // 지역 고정 권장 최대 체력 Href의 배수, 방어 전, 장판은 회당
            [SerializeField] private bool                           _guardable;             // 전방 반격 방어로 막을 수 있는가

            // 예고, 발동, 회복 단계를 지정한 패턴만 직접 제어 구간이 있다. 구간은 그 단계의 마지막 n초다
            [SerializeField] private EBossActionState               _controlPhase;
            [SerializeField] private float                          _controlWindowSeconds;
            [SerializeField] private bool                           _controlCancelsPattern; // 구간 안에서 제어에 성공하면 이 공격을 취소한다

            // 장판 형태만 쓴다
            [SerializeField] private float                          _areaDurationSeconds;   // 초, 활성 뒤 유지
            [SerializeField] private float                          _areaTickInterval;      // 초, 지속 피해 간격
            [SerializeField] private int                            _maxConcurrentAreas;    // 이 수만큼 남아 있으면 후보에서 뺀다

            public string                         DefinitionId          => _definitionId;
            public string                         DisplayKey            => _displayKey;
            public string                         BossId                => _bossId;
            public int                            Phase                 => _phase;
            public float                          BaseWeight            => _baseWeight;
            public IReadOnlyList<float>           DistanceMultipliers   => _distanceMultipliers;
            public IReadOnlyList<float>           DirectionMultipliers  => _directionMultipliers;
            public IReadOnlyList<float>           NearbyMultipliers     => _nearbyMultipliers;
            public IReadOnlyList<float>           RecentUseMultipliers  => _recentUseMultipliers;
            public float                          TelegraphSeconds      => _telegraphSeconds;
            public float                          TrackingSeconds       => _trackingSeconds;
            public float                          TurnRate              => _turnRate;
            public float                          ActiveSeconds         => _activeSeconds;
            public float                          RecoverySeconds       => _recoverySeconds;
            public EnemyCatalogSO.AttackShapeSpec Shape                 => _shape;
            public float                          DamageRef             => _damageRef;
            public bool                           Guardable             => _guardable;
            public EBossActionState               ControlPhase          => _controlPhase;
            public float                          ControlWindowSeconds  => _controlWindowSeconds;
            public bool                           ControlCancelsPattern => _controlCancelsPattern;
            public float                          AreaDurationSeconds   => _areaDurationSeconds;
            public float                          AreaTickInterval      => _areaTickInterval;
            public int                            MaxConcurrentAreas    => _maxConcurrentAreas;
            public bool                           HasControlWindow      => _controlWindowSeconds > 0f;

            /****************************************
            *                Methods
            ****************************************/

            /// <summary> 범주만 정하고 수치를 모두 미지정으로 둔 패턴, 배율 표는 열 수만 맞춘다 </summary>
            public PatternDefinition(string definitionId, string bossId, int phase, EEnemyAttackShape shape, bool guardable,
                EBossActionState controlPhase = NO_CONTROL_PHASE, bool controlCancelsPattern = false)
                : this(definitionId, bossId, phase, UNSET,
                    new float[DISTANCE_BAND_COUNT], new float[DIRECTION_BAND_COUNT], new float[NEARBY_BAND_COUNT], new float[RECENT_USE_SLOTS],
                    UNSET, UNSET, UNSET, UNSET, UNSET,
                    new EnemyCatalogSO.AttackShapeSpec(shape, UNSET, UNSET, UNSET, UNSET), UNSET, guardable,
                    controlPhase, UNSET, controlCancelsPattern)
            {
            }

            public PatternDefinition(string definitionId, string bossId, int phase, float baseWeight,
                float[] distanceMultipliers, float[] directionMultipliers, float[] nearbyMultipliers, float[] recentUseMultipliers,
                float telegraphSeconds, float trackingSeconds, float turnRate, float activeSeconds, float recoverySeconds,
                EnemyCatalogSO.AttackShapeSpec shape, float damageRef, bool guardable,
                EBossActionState controlPhase = NO_CONTROL_PHASE, float controlWindowSeconds = 0f, bool controlCancelsPattern = false,
                float areaDurationSeconds = 0f, float areaTickInterval = 0f, int maxConcurrentAreas = 0)
            {
                _definitionId          = definitionId;
                _displayKey            = definitionId + DISPLAY_KEY_SUFFIX;
                _bossId                = bossId;
                _phase                 = phase;
                _baseWeight            = baseWeight;
                _distanceMultipliers   = distanceMultipliers;
                _directionMultipliers  = directionMultipliers;
                _nearbyMultipliers     = nearbyMultipliers;
                _recentUseMultipliers  = recentUseMultipliers;
                _telegraphSeconds      = telegraphSeconds;
                _trackingSeconds       = trackingSeconds;
                _turnRate              = turnRate;
                _activeSeconds         = activeSeconds;
                _recoverySeconds       = recoverySeconds;
                _shape                 = shape;
                _damageRef             = damageRef;
                _guardable             = guardable;
                _controlPhase          = controlPhase;
                _controlWindowSeconds  = controlWindowSeconds;
                _controlCancelsPattern = controlCancelsPattern;
                _areaDurationSeconds   = areaDurationSeconds;
                _areaTickInterval      = areaTickInterval;
                _maxConcurrentAreas    = maxConcurrentAreas;
            }
        }

        /****************************************
        *              Constants
        ****************************************/

        private const string OWNER              = nameof(BossPatternCatalogSO);
        private const string DISPLAY_KEY_SUFFIX = ".name";
        private const float  UNSET              = 0f;
        private const float  MAX_RATE           = 1f;
        private const float  MAX_ANGLE          = 180f;
        private const float  MAX_ARC_ANGLE      = 360f;  // 부채꼴 전체 각도, AttackShapeSpec 검사와 같은 상한

        // 선택 배율 표의 열 수
        private const int DISTANCE_BAND_COUNT  = 3;
        private const int DIRECTION_BAND_COUNT = 3;
        private const int NEARBY_BAND_COUNT    = 3;
        private const int RECENT_USE_SLOTS     = 4;

        // 예고, 발동, 회복이 아닌 단계는 직접 제어 구간이 없다는 뜻이다.
        // enum 기본값이라 범주를 정하지 않은 패턴은 제어 구간 없이 시작한다
        private const EBossActionState NO_CONTROL_PHASE = EBossActionState.SelectTarget;

        private const int    FIRST_PHASE                     = 1;
        private const string WOLBAEK_ID                      = "boss_wolbaek";
        private const string CHEOLGAK_ID                     = "boss_cheolgak";
        private const int    WOLBAEK_PHASE_ONE_PATTERN_COUNT = 6;

        /****************************************
        *                Fields
        ****************************************/

        // 보스 수치는 보스 담당이 에셋에서 지정한다. 비어 있어도 GameDB 부트는 통과하고, 보스전 시작만 IsTuned로 막는다
        // 월백 50%는 전환 요청 자리만 둔다. 실제 2페이즈 실행은 데이터가 들어온 뒤에 연결한다
        // TODO(기웅): 월백 보스 수치와 6패턴 수치를 지정한다. 패턴 사이 이동 여부와 속도는 기획 미정이다
        [Header("보스")]
        [SerializeField] private BossDefinition[] _bosses =
        {
            new BossDefinition(WOLBAEK_ID),
            // 보상과 진행 카탈로그가 철각 보스전을 참조하므로 ID만 둔다. 체험판에 넣지 않고 패턴도 없다
            new BossDefinition(CHEOLGAK_ID)
        };

        // 번호는 기획표의 P01~P06 순서와 같다. ID 앞에 보스를 붙여 다음 보스 패턴과 겹치지 않게 한다
        // TODO(기웅): 2페이즈 패턴은 담당자 입력 대기, 들어오면 phase 2로 추가한다
        [Header("패턴")]
        [SerializeField] private PatternDefinition[] _patterns =
        {
            new PatternDefinition("wolbaek_p01", WOLBAEK_ID, FIRST_PHASE, EEnemyAttackShape.Dash, guardable: true),
            new PatternDefinition("wolbaek_p02", WOLBAEK_ID, FIRST_PHASE, EEnemyAttackShape.Fan, guardable: true),
            new PatternDefinition("wolbaek_p03", WOLBAEK_ID, FIRST_PHASE, EEnemyAttackShape.Circle, guardable: true),
            new PatternDefinition("wolbaek_p04", WOLBAEK_ID, FIRST_PHASE, EEnemyAttackShape.Projectile, guardable: true,
                controlPhase: EBossActionState.Recover),
            new PatternDefinition("wolbaek_p05", WOLBAEK_ID, FIRST_PHASE, EEnemyAttackShape.TargetArea, guardable: false),
            new PatternDefinition("wolbaek_p06", WOLBAEK_ID, FIRST_PHASE, EEnemyAttackShape.Line, guardable: false,
                controlPhase: EBossActionState.Telegraph, controlCancelsPattern: true)
        };

        [NonSerialized] private readonly DefinitionLookup<BossDefinition>    _bossLookup    = new DefinitionLookup<BossDefinition>();
        [NonSerialized] private readonly DefinitionLookup<PatternDefinition> _patternLookup = new DefinitionLookup<PatternDefinition>();

        public IReadOnlyList<BossDefinition>    Bosses   => _bosses;
        public IReadOnlyList<PatternDefinition> Patterns => _patterns;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            InvalidateLookups();
        }

        protected override void OnValidate()
        {
            InvalidateLookups();
            base.OnValidate();
        }

        /****************************************
        *                Methods
        ****************************************/

        public bool ContainsBoss(string bossId)
        {
            return _bossLookup.Contains(_bosses, bossId);
        }

        public bool ContainsPattern(string patternId)
        {
            return _patternLookup.Contains(_patterns, patternId);
        }

        public bool TryGetBoss(string bossId, out BossDefinition boss)
        {
            return _bossLookup.TryGet(_bosses, bossId, out boss);
        }

        public bool TryGetPattern(string patternId, out PatternDefinition pattern)
        {
            return _patternLookup.TryGet(_patterns, patternId, out pattern);
        }

        /// <summary> 보스와 페이즈에 속한 패턴을 결과 목록에 채운다, 서버 선택기가 전투 시작 때 한 번 만든다 </summary>
        public void CollectPatterns(string bossId, int phase, List<PatternDefinition> results)
        {
            results.Clear();
            if (_patterns == null)
            {
                return;
            }

            for (int i = 0; i < _patterns.Length; ++i)
            {
                PatternDefinition pattern = _patterns[i];
                if ((pattern != null) && (pattern.BossId == bossId) && (pattern.Phase == phase))
                {
                    results.Add(pattern);
                }
            }
        }

        /// <summary> 보스와 그 패턴의 필수 수치가 모두 지정됐고 범위가 맞는가. 서버 세션은 이 값이 거짓인 보스의 전투를 열지 않는다 </summary>
        public bool IsTuned(string bossId)
        {
            var errors = new List<string>();
            ValidateTuning(bossId, errors);
            return errors.Count == 0;
        }

        /// <summary> 보스와 그 패턴의 미지정 수치와 범위 오류를 결과 목록에 더한다. 미지정 수치를 기본값으로 대신 채워 전투를 열지 않는다 </summary>
        public void ValidateTuning(string bossId, List<string> errors)
        {
            if (!TryGetBoss(bossId, out BossDefinition boss))
            {
                errors.Add($"{OWNER}.{nameof(_bosses)}: 보스 '{bossId}'가 없다");
                return;
            }

            ValidateBoss(errors, boss, true);

            int patternCount = 0;
            if (_patterns != null)
            {
                for (int i = 0; i < _patterns.Length; ++i)
                {
                    PatternDefinition pattern = _patterns[i];
                    if ((pattern == null) || (pattern.BossId != bossId))
                    {
                        continue;
                    }

                    ++patternCount;
                    ValidatePattern(errors, pattern, true);
                }
            }

            if (patternCount == 0)
            {
                errors.Add($"{OWNER}.{nameof(_patterns)}: 보스 '{bossId}'의 패턴이 없다");
            }
        }

        private void InvalidateLookups()
        {
            _bossLookup.Invalidate();
            _patternLookup.Invalidate();
        }

        // GameDB 부트를 막는 검사다. 구조는 항상 막고, 수치는 0이면 미지정으로 넘기며 넣은 값의 범위만 본다
        protected override void ValidateValues(List<string> errors)
        {
            DataValidation.RequireDefinitions(errors, OWNER, nameof(_bosses), _bosses);
            DataValidation.RequireDefinitions(errors, OWNER, nameof(_patterns), _patterns);

            if (_bosses != null)
            {
                for (int i = 0; i < _bosses.Length; ++i)
                {
                    if (_bosses[i] != null)
                    {
                        ValidateBoss(errors, _bosses[i], false);
                    }
                }
            }

            if (!ContainsBoss(WOLBAEK_ID))
            {
                errors.Add($"{OWNER}.{nameof(_bosses)}: 보스 '{WOLBAEK_ID}'가 없다");
            }

            if (_patterns == null)
            {
                return;
            }

            int wolbaekPhaseOneCount = 0;
            for (int i = 0; i < _patterns.Length; ++i)
            {
                PatternDefinition pattern = _patterns[i];
                if (pattern == null)
                {
                    continue;
                }

                if ((pattern.BossId == WOLBAEK_ID) && (pattern.Phase == FIRST_PHASE))
                {
                    ++wolbaekPhaseOneCount;
                }

                ValidatePattern(errors, pattern, false);
            }

            if (wolbaekPhaseOneCount != WOLBAEK_PHASE_ONE_PATTERN_COUNT)
            {
                errors.Add($"{OWNER}.{nameof(_patterns)}: 월백 1페이즈 패턴 수 {wolbaekPhaseOneCount} != {WOLBAEK_PHASE_ONE_PATTERN_COUNT}");
            }
        }

        // requireTuned가 거짓이면 0을 미지정으로 넘기고, 참이면 0도 오류로 본다.
        // 방어, 시작 대기, 제어 면역처럼 0이 정상 설계값인 필드는 두 경우 모두 0 이상만 본다
        private static void ValidateBoss(List<string> errors, BossDefinition boss, bool requireTuned)
        {
            string field = $"{nameof(_bosses)}[{boss.DefinitionId}]";

            CheckPositive(errors, field + ".ArenaDiameter", boss.ArenaDiameter, requireTuned);
            CheckPositive(errors, field + ".HealthRef", boss.HealthRef, requireTuned);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".Defense", boss.Defense);
            CheckPositiveAtMost(errors, field + ".PhaseTransitionHealthRate", boss.PhaseTransitionHealthRate, MAX_RATE, requireTuned);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".OpeningDelaySeconds", boss.OpeningDelaySeconds);
            CheckPositive(errors, field + ".NearDistanceMax", boss.NearDistanceMax, requireTuned);
            CheckAtLeast(errors, field + ".MidDistanceMax", boss.MidDistanceMax, boss.NearDistanceMax, float.MaxValue, requireTuned);
            CheckPositiveAtMost(errors, field + ".FrontAngleMax", boss.FrontAngleMax, MAX_ANGLE, requireTuned);
            CheckAtLeast(errors, field + ".SideAngleMax", boss.SideAngleMax, boss.FrontAngleMax, MAX_ANGLE, requireTuned);
            CheckPositive(errors, field + ".NearbyRadius", boss.NearbyRadius, requireTuned);
            CheckPositive(errors, field + ".EmptyWeightRetrySeconds", boss.EmptyWeightRetrySeconds, requireTuned);
            CheckPositive(errors, field + ".DirectControlMaxSeconds", boss.DirectControlMaxSeconds, requireTuned);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".DirectControlImmuneSeconds", boss.DirectControlImmuneSeconds);
        }

        // 추적, 회전, 발동, 회복 시간은 0이 정상 설계값이라 두 경우 모두 0 이상만 본다
        private void ValidatePattern(List<string> errors, PatternDefinition pattern, bool requireTuned)
        {
            string field = $"{nameof(_patterns)}[{pattern.DefinitionId}]";

            if (!ContainsBoss(pattern.BossId))
            {
                errors.Add($"{OWNER}.{field}.BossId: 보스 '{pattern.BossId}'가 이 카탈로그에 없다");
            }

            DataValidation.RequireRange(errors, OWNER, field + ".Phase", pattern.Phase, FIRST_PHASE, int.MaxValue);
            CheckPositive(errors, field + ".BaseWeight", pattern.BaseWeight, requireTuned);
            ValidateMultipliers(errors, field + ".DistanceMultipliers", pattern.DistanceMultipliers, DISTANCE_BAND_COUNT, requireTuned);
            ValidateMultipliers(errors, field + ".DirectionMultipliers", pattern.DirectionMultipliers, DIRECTION_BAND_COUNT, requireTuned);
            ValidateMultipliers(errors, field + ".NearbyMultipliers", pattern.NearbyMultipliers, NEARBY_BAND_COUNT, requireTuned);
            ValidateMultipliers(errors, field + ".RecentUseMultipliers", pattern.RecentUseMultipliers, RECENT_USE_SLOTS, requireTuned);

            CheckPositive(errors, field + ".TelegraphSeconds", pattern.TelegraphSeconds, requireTuned);
            DataValidation.RequireRange(errors, OWNER, field + ".TrackingSeconds", pattern.TrackingSeconds, 0f, UpperBound(pattern.TelegraphSeconds, requireTuned));
            DataValidation.RequireNonNegative(errors, OWNER, field + ".TurnRate", pattern.TurnRate);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".ActiveSeconds", pattern.ActiveSeconds);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".RecoverySeconds", pattern.RecoverySeconds);
            CheckPositive(errors, field + ".DamageRef", pattern.DamageRef, requireTuned);

            if (pattern.Shape == null)
            {
                errors.Add($"{OWNER}.{field}.Shape: 공격 형태가 비어 있다");
            }
            else
            {
                ValidateShape(errors, field, pattern, requireTuned);
            }

            ValidateControlWindow(errors, field, pattern, requireTuned);
        }

        private static void ValidateShape(List<string> errors, string field, PatternDefinition pattern, bool requireTuned)
        {
            EnemyCatalogSO.AttackShapeSpec shape = pattern.Shape;
            string shapeField = field + ".Shape";

            if (requireTuned)
            {
                shape.Validate(errors, OWNER, shapeField);
            }
            else
            {
                // AttackShapeSpec 검사는 0을 오류로 보므로 미지정을 허용하는 부트 검사에서는 넣은 값만 따로 본다
                if (!Enum.IsDefined(typeof(EEnemyAttackShape), shape.Shape))
                {
                    errors.Add($"{OWNER}.{shapeField}.Shape = {shape.Shape}: 정의되지 않은 형태다");
                }

                CheckPositive(errors, shapeField + ".Range", shape.Range, false);
                CheckPositiveAtMost(errors, shapeField + ".ArcAngle", shape.ArcAngle, MAX_ARC_ANGLE, false);
                CheckPositive(errors, shapeField + ".Width", shape.Width, false);
                CheckPositive(errors, shapeField + ".ProjectileSpeed", shape.ProjectileSpeed, false);
            }

            if (shape.Shape != EEnemyAttackShape.TargetArea)
            {
                return;
            }

            CheckPositive(errors, field + ".AreaDurationSeconds", pattern.AreaDurationSeconds, requireTuned);
            CheckPositiveAtMost(errors, field + ".AreaTickInterval", pattern.AreaTickInterval, UpperBound(pattern.AreaDurationSeconds, requireTuned), requireTuned);
            CheckPositive(errors, field + ".MaxConcurrentAreas", pattern.MaxConcurrentAreas, requireTuned);
        }

        private static void ValidateControlWindow(List<string> errors, string field, PatternDefinition pattern, bool requireTuned)
        {
            float phaseSeconds;
            switch (pattern.ControlPhase)
            {
                case EBossActionState.Telegraph:
                    phaseSeconds = pattern.TelegraphSeconds;
                    break;
                case EBossActionState.Execute:
                    phaseSeconds = pattern.ActiveSeconds;
                    break;
                case EBossActionState.Recover:
                    phaseSeconds = pattern.RecoverySeconds;
                    break;
                default:
                    if (pattern.ControlWindowSeconds != UNSET)
                    {
                        errors.Add($"{OWNER}.{field}.ControlPhase = {pattern.ControlPhase}: 예고, 발동, 회복 단계만 제어 구간을 가질 수 있다");
                    }
                    return;
            }

            CheckPositiveAtMost(errors, field + ".ControlWindowSeconds", pattern.ControlWindowSeconds, UpperBound(phaseSeconds, requireTuned), requireTuned);
        }

        // 배율 하나하나는 0이 정상 설계값이다. 표 전체가 0이면 그 패턴이 뽑히지 않으므로 미지정으로 본다
        private static void ValidateMultipliers(List<string> errors, string field, IReadOnlyList<float> multipliers, int expected, bool requireTuned)
        {
            DataValidation.RequireCount(errors, OWNER, field, multipliers, expected);
            if (multipliers == null)
            {
                return;
            }

            bool hasPositive = false;
            for (int i = 0; i < multipliers.Count; ++i)
            {
                DataValidation.RequireNonNegative(errors, OWNER, $"{field}[{i}]", multipliers[i]);
                hasPositive |= multipliers[i] > 0f;
            }

            if (requireTuned && !hasPositive)
            {
                errors.Add($"{OWNER}.{field}: 배율이 모두 0이라 미지정이다");
            }
        }

        // 다른 값을 상한으로 쓸 때 그 값이 미지정이면 부트 검사에서는 상한을 두지 않는다
        private static float UpperBound(float bound, bool requireTuned)
        {
            return (!requireTuned && (bound == UNSET)) ? float.MaxValue : bound;
        }

        private static void CheckPositive(List<string> errors, string field, float value, bool requireTuned)
        {
            if (!requireTuned && (value == UNSET))
            {
                return;
            }

            DataValidation.RequirePositive(errors, OWNER, field, value);
        }

        private static void CheckPositive(List<string> errors, string field, int value, bool requireTuned)
        {
            if (!requireTuned && (value == 0))
            {
                return;
            }

            DataValidation.RequirePositive(errors, OWNER, field, value);
        }

        private static void CheckPositiveAtMost(List<string> errors, string field, float value, float max, bool requireTuned)
        {
            if (!requireTuned && (value == UNSET))
            {
                return;
            }

            DataValidation.RequirePositiveAtMost(errors, OWNER, field, value, max);
        }

        // 하한으로 쓰는 다른 값이 미지정이면 0보다 큰지만 본다
        private static void CheckAtLeast(List<string> errors, string field, float value, float min, float max, bool requireTuned)
        {
            if (!requireTuned && (value == UNSET))
            {
                return;
            }

            if (min == UNSET)
            {
                DataValidation.RequirePositiveAtMost(errors, OWNER, field, value, max);
                return;
            }

            DataValidation.RequireRange(errors, OWNER, field, value, min, max);
        }
    }
}
