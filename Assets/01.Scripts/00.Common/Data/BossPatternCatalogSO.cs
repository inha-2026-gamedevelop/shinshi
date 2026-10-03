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
        /// <summary> 2페이즈 선택 흐름 배율이 쓰는 패턴 역할 분류, 1페이즈 패턴은 None이다 </summary>
        public enum EPatternGroup
        {
            None     = 0,
            Large    = 1,
            Pressure = 2,
            Basic    = 3
        }

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
            [SerializeField] private float  _appearanceSeconds;          // 초, 등장 컷씬 길이, 전투 시작 전 기다리는 시간. 0이면 대기 없이 바로 시작한다
            [SerializeField] private float  _openingDelaySeconds;        // 초, 전투 시작 뒤 첫 패턴 선택까지
            [SerializeField] private float  _nearDistanceMax;            // m, 이 거리 미만이 근거리
            [SerializeField] private float  _midDistanceMax;             // m, 이 거리 미만이 중거리, 이상은 원거리
            [SerializeField] private float  _frontAngleMax;              // 도, 정면 대비 절대각이 이 값 이하면 전방
            [SerializeField] private float  _sideAngleMax;               // 도, 이 값 이하면 측면, 초과는 후방
            [SerializeField] private float  _nearbyRadius;               // m, 주변 인원을 세는 반경
            [SerializeField] private float  _emptyWeightRetrySeconds;    // 초, 가중치 합이 0일 때 다시 조회하기까지
            [SerializeField] private float  _directControlMaxSeconds;    // 초, 한 패턴에 한 번 받는 직접 제어의 최대 길이
            [SerializeField] private float  _directControlImmuneSeconds; // 초, 직접 제어가 성공한 뒤 추가 직접 제어를 받지 않는 시간

            // 아래 둘은 2페이즈 선택에만 쓴다. 1페이즈 시작 검사(IsTuned)는 보지 않고 2페이즈 준비 검사가 본다
            [SerializeField] private float   _enhancedHealthRate;        // 이 체력 비율 이하(포함)면 강화 기본값과 강화 흐름 배율을 쓴다
            [SerializeField] private float[] _flowMultipliers;           // 흐름 배율 18칸, 체력 구간(초과, 이하) x 직전 그룹(Large, Pressure, Basic) x 다음 그룹 순서

            public string DefinitionId              => _definitionId;
            public string DisplayKey                 => _displayKey;
            public float  ArenaDiameter              => _arenaDiameter;
            public float  HealthRef                  => _healthRef;
            public float  Defense                    => _defense;
            public float  PhaseTransitionHealthRate  => _phaseTransitionHealthRate;
            public float  AppearanceSeconds          => _appearanceSeconds;
            public float  OpeningDelaySeconds        => _openingDelaySeconds;
            public float  NearDistanceMax            => _nearDistanceMax;
            public float  MidDistanceMax             => _midDistanceMax;
            public float  FrontAngleMax              => _frontAngleMax;
            public float  SideAngleMax               => _sideAngleMax;
            public float  NearbyRadius               => _nearbyRadius;
            public float  EmptyWeightRetrySeconds    => _emptyWeightRetrySeconds;
            public float  DirectControlMaxSeconds    => _directControlMaxSeconds;
            public float  DirectControlImmuneSeconds => _directControlImmuneSeconds;
            public float  EnhancedHealthRate         => _enhancedHealthRate;

            public IReadOnlyList<float> FlowMultipliers => _flowMultipliers;

            /****************************************
            *                Methods
            ****************************************/

            /// <summary> 체력 비율이 강화 구간(기준 이하, 경계 포함)인가, 기준이 미지정이면 거짓이다 </summary>
            public bool IsEnhanced(float healthRate)
            {
                return (_enhancedHealthRate > UNSET) && (healthRate <= _enhancedHealthRate);
            }

            /// <summary> 직전 그룹에서 다음 그룹으로 이어질 때의 흐름 배율, 직전 그룹이 없으면(첫 선택) 1이고 표가 비었으면 0이다 </summary>
            public float GetFlowMultiplier(bool isEnhanced, EPatternGroup previous, EPatternGroup next)
            {
                if (previous == EPatternGroup.None)
                {
                    return NEUTRAL_MULTIPLIER;
                }

                if ((next == EPatternGroup.None) || (_flowMultipliers == null) || (_flowMultipliers.Length != FLOW_TABLE_SIZE))
                {
                    return UNSET;
                }

                int band = isEnhanced ? 1 : 0;
                return _flowMultipliers[(((band * FLOW_GROUP_COUNT) + ((int)previous - 1)) * FLOW_GROUP_COUNT) + ((int)next - 1)];
            }

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
                : this(definitionId, arenaDiameter, healthRef, defense, phaseTransitionHealthRate, openingDelaySeconds,
                    nearDistanceMax, midDistanceMax, frontAngleMax, sideAngleMax, nearbyRadius, emptyWeightRetrySeconds,
                    directControlMaxSeconds, directControlImmuneSeconds, UNSET)
            {
            }

            public BossDefinition(string definitionId, float arenaDiameter, float healthRef, float defense,
                float phaseTransitionHealthRate, float openingDelaySeconds, float nearDistanceMax, float midDistanceMax,
                float frontAngleMax, float sideAngleMax, float nearbyRadius, float emptyWeightRetrySeconds,
                float directControlMaxSeconds, float directControlImmuneSeconds, float appearanceSeconds)
                : this(definitionId, arenaDiameter, healthRef, defense, phaseTransitionHealthRate, openingDelaySeconds,
                    nearDistanceMax, midDistanceMax, frontAngleMax, sideAngleMax, nearbyRadius, emptyWeightRetrySeconds,
                    directControlMaxSeconds, directControlImmuneSeconds, appearanceSeconds, UNSET, new float[FLOW_TABLE_SIZE])
            {
            }

            public BossDefinition(string definitionId, float arenaDiameter, float healthRef, float defense,
                float phaseTransitionHealthRate, float openingDelaySeconds, float nearDistanceMax, float midDistanceMax,
                float frontAngleMax, float sideAngleMax, float nearbyRadius, float emptyWeightRetrySeconds,
                float directControlMaxSeconds, float directControlImmuneSeconds, float appearanceSeconds,
                float enhancedHealthRate, float[] flowMultipliers)
            {
                _enhancedHealthRate         = enhancedHealthRate;
                _flowMultipliers            = flowMultipliers;
                _definitionId               = definitionId;
                _displayKey                 = definitionId + DISPLAY_KEY_SUFFIX;
                _appearanceSeconds          = appearanceSeconds;
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

            // 2페이즈 선택에만 쓴다. 1페이즈 패턴은 그룹이 None이고 강화 기본값이 0이다
            [SerializeField] private EPatternGroup                  _group;                 // 흐름 배율이 직전, 다음 패턴을 가르는 역할 분류
            [SerializeField] private float                          _enhancedBaseWeight;    // 체력이 강화 구간(25% 이하)일 때의 기본값

            // 투사체 형태만 쓴다. 0은 미지정이고 한 발로 본다, 그래서 단발 패턴(1페이즈 P04)은 값을 넣지 않는다
            [SerializeField] private int                            _projectileCount;           // 한 시전에 순서대로 쏘는 발 수
            [SerializeField] private float                          _projectileIntervalSeconds; // 초, 발과 발 사이, 첫 발은 예고가 끝나는 순간이다

            // 장판 형태만 쓴다. 0은 미지정이고 선택된 타깃 한 명의 단일 장판으로 본다, 그래서 1페이즈 P05는 값을 넣지 않는다
            [SerializeField] private int                            _areaTargetCount;           // 서로 다른 대상에게 동시에 설치하는 장판 수, 2 이상이면 여러 대상 시전이다
            [SerializeField] private bool                           _areaSkipsMissedRounds;     // 지연된 틱에서 장판 하나당 가장 최근 회차만 판정하고 이전 회차는 건너뛴다

            // 부채꼴 연쇄 폭발만 쓴다. 0은 미지정이고 연쇄가 아니다. 전체 각도는 형태의 ArcAngle이고 구역 하나의 각도는 전체 각도를 구역 수로 나눈 값이다
            [SerializeField] private int                            _sweepZoneCount;            // 선택한 쪽 반원을 나누는 구역 수
            [SerializeField] private float                          _sweepIntervalSeconds;      // 초, 구역과 구역 사이, 첫 구역은 예고가 끝나는 순간이다

            // 고정 평행 빔만 쓴다. 0은 미지정이고 빔 패턴이 아니다. 빔 폭은 형태의 Width, 유지 시간은 ActiveSeconds, 피해 간격은 아래 값이다
            [SerializeField] private int                            _beamCount;                 // 한 시전이 설치하는 평행 빔 줄 수
            [SerializeField] private float                          _beamLateralOffset;         // m, 전장 중심에서 각 빔 중심선까지 좌우 거리
            [SerializeField] private float                          _beamTickInterval;          // 초, 피해 간격

            // 별형 회전 빔만 쓴다. 0은 미지정이고 별형이 아니다. 빔 폭은 형태의 Width, 유지 시간은 ActiveSeconds, 피해 간격은 BeamTickInterval이다
            [SerializeField] private int                            _starVertexCount;           // 전장 중심 둘레에 놓는 여우불(꼭짓점) 수
            [SerializeField] private float                          _starRingRadius;            // m, 전장 중심에서 꼭짓점까지 거리
            [SerializeField] private int                            _starConnectStep;           // 꼭짓점 k를 몇 칸 뒤 꼭짓점과 잇는가(오각별은 2)
            [SerializeField] private float                          _starRotationDegrees;       // 도/초, 활성 순간부터 한 방향으로 도는 속도

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
            public EPatternGroup                  Group                 => _group;
            public float                          EnhancedBaseWeight    => _enhancedBaseWeight;

            /// <summary> 한 시전에 쏘는 발 수, 미지정(0)이면 한 발이다 </summary>
            public int                            ProjectileCount       => Mathf.Max(1, _projectileCount);

            /// <summary> 데이터에 입력된 발 수 그대로, 0이면 미지정이다 </summary>
            public int                            RawProjectileCount    => _projectileCount;

            public float                          ProjectileIntervalSeconds => _projectileIntervalSeconds;

            /// <summary> 한 시전이 설치하는 장판 대상 수, 미지정(0)이면 한 개다 </summary>
            public int                            AreaTargetCount       => Mathf.Max(1, _areaTargetCount);

            /// <summary> 데이터에 입력된 대상 수 그대로, 0이면 미지정이다 </summary>
            public int                            RawAreaTargetCount    => _areaTargetCount;

            public bool                           AreaSkipsMissedRounds => _areaSkipsMissedRounds;

            /// <summary> 데이터에 입력된 연쇄 구역 수 그대로, 0이면 연쇄 패턴이 아니다 </summary>
            public int                            RawSweepZoneCount     => _sweepZoneCount;

            /// <summary> 연쇄 구역 수, 연쇄가 아니면 한 구역으로 본다 </summary>
            public int                            SweepZoneCount        => Mathf.Max(1, _sweepZoneCount);

            public float                          SweepIntervalSeconds  => _sweepIntervalSeconds;

            /// <summary> 데이터에 입력된 빔 줄 수 그대로, 0이면 빔 패턴이 아니다 </summary>
            public int                            RawBeamCount          => _beamCount;

            /// <summary> 빔 줄 수, 빔 패턴이 아니면 한 줄로 본다 </summary>
            public int                            BeamCount             => Mathf.Max(1, _beamCount);

            /// <summary> m, 전장 중심에서 빔 중심선까지 좌우 거리, 두 줄이면 중심선 사이 거리는 이 값의 두 배다 </summary>
            public float                          BeamLateralOffset     => _beamLateralOffset;

            /// <summary> 초, 빔 피해 간격, 활성 순간이 첫 타격이다. 빔 유지 시간은 ActiveSeconds다 </summary>
            public float                          BeamTickInterval      => _beamTickInterval;

            /// <summary> 데이터에 입력된 별형 꼭짓점 수 그대로, 0이면 별형 회전 빔이 아니다 </summary>
            public int                            StarVertexCount       => _starVertexCount;

            /// <summary> m, 전장 중심에서 꼭짓점까지 거리 </summary>
            public float                          StarRingRadius        => _starRingRadius;

            /// <summary> 꼭짓점 k와 잇는 꼭짓점이 몇 칸 뒤인가 </summary>
            public int                            StarConnectStep       => _starConnectStep;

            /// <summary> 도/초, 활성 순간부터 도는 속도 </summary>
            public float                          StarRotationDegrees   => _starRotationDegrees;

            /****************************************
            *                Methods
            ****************************************/

            /// <summary> 범주만 정하고 수치를 모두 미지정으로 둔 패턴, 배율 표는 열 수만 맞춘다 </summary>
            public PatternDefinition(string definitionId, string bossId, int phase, EEnemyAttackShape shape, bool guardable,
                EBossActionState controlPhase = NO_CONTROL_PHASE, bool controlCancelsPattern = false,
                bool areaSkipsMissedRounds = false)
                : this(definitionId, bossId, phase, UNSET,
                    new float[DISTANCE_BAND_COUNT], new float[DIRECTION_BAND_COUNT], new float[NEARBY_BAND_COUNT], new float[RECENT_USE_SLOTS],
                    UNSET, UNSET, UNSET, UNSET, UNSET,
                    new EnemyCatalogSO.AttackShapeSpec(shape, UNSET, UNSET, UNSET, UNSET), UNSET, guardable,
                    controlPhase, UNSET, controlCancelsPattern, areaSkipsMissedRounds: areaSkipsMissedRounds)
            {
            }

            public PatternDefinition(string definitionId, string bossId, int phase, float baseWeight,
                float[] distanceMultipliers, float[] directionMultipliers, float[] nearbyMultipliers, float[] recentUseMultipliers,
                float telegraphSeconds, float trackingSeconds, float turnRate, float activeSeconds, float recoverySeconds,
                EnemyCatalogSO.AttackShapeSpec shape, float damageRef, bool guardable,
                EBossActionState controlPhase = NO_CONTROL_PHASE, float controlWindowSeconds = 0f, bool controlCancelsPattern = false,
                float areaDurationSeconds = 0f, float areaTickInterval = 0f, int maxConcurrentAreas = 0,
                EPatternGroup group = EPatternGroup.None, float enhancedBaseWeight = 0f,
                int projectileCount = 0, float projectileIntervalSeconds = 0f, int areaTargetCount = 0,
                bool areaSkipsMissedRounds = false, int sweepZoneCount = 0, float sweepIntervalSeconds = 0f,
                int beamCount = 0, float beamLateralOffset = 0f, float beamTickInterval = 0f,
                int starVertexCount = 0, float starRingRadius = 0f, int starConnectStep = 0, float starRotationDegrees = 0f)
            {
                _starVertexCount           = starVertexCount;
                _starRingRadius            = starRingRadius;
                _starConnectStep           = starConnectStep;
                _starRotationDegrees       = starRotationDegrees;
                _beamCount                 = beamCount;
                _beamLateralOffset         = beamLateralOffset;
                _beamTickInterval          = beamTickInterval;
                _sweepZoneCount            = sweepZoneCount;
                _sweepIntervalSeconds      = sweepIntervalSeconds;
                _areaTargetCount           = areaTargetCount;
                _areaSkipsMissedRounds     = areaSkipsMissedRounds;
                _group                 = group;
                _enhancedBaseWeight    = enhancedBaseWeight;
                _projectileCount       = projectileCount;
                _projectileIntervalSeconds = projectileIntervalSeconds;
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

        // 월백 2페이즈 패턴 ID, 1페이즈 ID(wolbaek_p01~06)와 겹치지 않는다
        public const string WOLBAEK_P2_P01_ID = "wolbaek_ph2_p01";
        public const string WOLBAEK_P2_P02_ID = "wolbaek_ph2_p02";
        public const string WOLBAEK_P2_P03_ID = "wolbaek_ph2_p03";
        public const string WOLBAEK_P2_P04_ID = "wolbaek_ph2_p04";
        public const string WOLBAEK_P2_P05_ID = "wolbaek_ph2_p05";
        public const string WOLBAEK_P2_P06_ID = "wolbaek_ph2_p06";

        private const float NEUTRAL_MULTIPLIER = 1f;

        // 한 시전의 발 수 상한, 서버 지속물 관리자의 동시 비행 슬롯 수와 같다
        private const int   MAX_PROJECTILE_COUNT = 8;

        // 한 시전이 장판을 설치할 수 있는 대상 수의 상한, 서버 패턴의 기술적 상한과 같다
        private const int   MAX_AREA_TARGETS     = 4;

        // 한 시전의 연쇄 구역 수의 상한, 서버 패턴의 기술적 상한과 같다
        private const int   MAX_SWEEP_ZONES      = 8;
        private const float SEQUENCE_EPSILON     = 0.0001f; // 초, 실행 구간 비교의 부동소수 오차

        // 한 시전의 평행 빔 줄 수의 상한, 서버 빔 관리자의 슬롯 수와 같다
        private const int   MAX_BEAMS            = 4;
        private const float BEAM_LATERAL_FACTOR  = 2f;

        // 별형 꼭짓점 수의 범위, 서버 별형 패턴의 기술적 상한과 같다
        private const int   MIN_STAR_VERTICES    = 5;
        private const int   MAX_STAR_VERTICES    = 8;      // 두 줄 사이 중심선 거리는 좌우 거리의 두 배

        // 흐름 배율 표, 체력 구간 2 x 직전 그룹 3 x 다음 그룹 3
        private const int FLOW_BAND_COUNT  = 2;
        private const int FLOW_GROUP_COUNT = 3;
        private const int FLOW_TABLE_SIZE  = FLOW_BAND_COUNT * FLOW_GROUP_COUNT * FLOW_GROUP_COUNT;

        private const int    FIRST_PHASE                     = 1;
        private const int    SECOND_PHASE                    = 2;
        private const string WOLBAEK_ID                     = "boss_wolbaek";
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
        // TODO(기웅): wolbaek_p02, p03, p06의 회복 0.5초는 임시값이다, 확정 밸런스가 아니며 에셋에서 조정한다
        [Header("패턴")]
        [SerializeField] private PatternDefinition[] _patterns = CreateDefaultPatterns();

        [NonSerialized] private readonly DefinitionLookup<BossDefinition>    _bossLookup    = new DefinitionLookup<BossDefinition>();
        [NonSerialized] private readonly DefinitionLookup<PatternDefinition> _patternLookup = new DefinitionLookup<PatternDefinition>();

        public IReadOnlyList<BossDefinition>    Bosses   => _bosses;
        public IReadOnlyList<PatternDefinition> Patterns => _patterns;

        /****************************************
        *           Default Pattern Data
        ****************************************/

        // 1페이즈 여섯 패턴은 범주만 둔다. 수치는 에셋에서 지정한다
        private static PatternDefinition[] CreateDefaultPatterns()
        {
            List<PatternDefinition> patterns = new List<PatternDefinition>
            {
                new PatternDefinition("wolbaek_p01", WOLBAEK_ID, FIRST_PHASE, EEnemyAttackShape.Dash, guardable: true),
                new PatternDefinition("wolbaek_p02", WOLBAEK_ID, FIRST_PHASE, EEnemyAttackShape.Fan, guardable: true),
                new PatternDefinition("wolbaek_p03", WOLBAEK_ID, FIRST_PHASE, EEnemyAttackShape.Circle, guardable: true),
                new PatternDefinition("wolbaek_p04", WOLBAEK_ID, FIRST_PHASE, EEnemyAttackShape.Projectile, guardable: true,
                    controlPhase: EBossActionState.Recover),
                // 1페이즈 P05도 지연된 틱에서 장판당 한 틱에 최근 회차만 판정한다(확정 규칙), 다음 회차는 원래 활성 시각 기준이다
                new PatternDefinition("wolbaek_p05", WOLBAEK_ID, FIRST_PHASE, EEnemyAttackShape.TargetArea, guardable: false,
                    areaSkipsMissedRounds: true),
                new PatternDefinition("wolbaek_p06", WOLBAEK_ID, FIRST_PHASE, EEnemyAttackShape.Line, guardable: false,
                    controlPhase: EBossActionState.Telegraph, controlCancelsPattern: true)
            };

            patterns.AddRange(CreateWolbaekPhaseTwoDefaults());
            return patterns.ToArray();
        }

        // 2페이즈 Easy 선택 데이터(기본값, 거리, 방향, 주변, 최근, 그룹)를 기획안에서 옮긴다. 공격 시간, 범위, 피해량은 공격을 구현하고
        // 기획이 수치를 정한 패턴만 넣고(지금은 P06) 나머지는 0으로 비워 둔다. 비운 패턴은 IsPatternReady가 거짓이라 뽑히지 않는다
        // TODO(기웅): 아래 형태 범주와 방어 가능 여부는 자리값이다. P01과 P02는 EEnemyAttackShape에 빔 형태가 없어 Line으로 두었고,
        // 형태 추가는 00.Common/Types 소유자와 협의해야 한다. 각 공격을 구현할 때 확정한다
        /// <summary> 월백 2페이즈 여섯 패턴의 선택 데이터 기본값, 기존 에셋에 더할 때도 같은 값을 쓴다 </summary>
        public static PatternDefinition[] CreateWolbaekPhaseTwoDefaults()
        {
            float[] recent = { 0f, 0.3f, 0.7f, 1f };
            float[] flatDirection = { 1f, 1f, 1f };

            return new[]
            {
                // P01은 전장 중심 반경 14m에 여우불 5개를 정오각형으로 놓고 두 칸 뒤 꼭짓점과 이어 오각별 빔 5개를 만든다(0-2, 1-3, 2-4, 3-0, 4-1).
                // 빔 폭 1.5m, 예고 2초, 활성 순간부터 6초 동안 서버가 고른 방향(좌우 각 50%)으로 초당 20도 회전, 0.25초 간격 24회, 회당 0.03Href(0.5초에 0.06Href와 같은 총량),
                // 방어 불가, 회복 1초, 직접 제어 없음이다. 체력 25% 이하에서도 수치는 같다. 수치는 시작안이며 테스트로 조정한다
                // 형태의 길이(27m)는 별 한 줄(약 26.6m)의 표시용 값이고 판정은 꼭짓점 좌표로 한다
                MakePhaseTwo(WOLBAEK_P2_P01_ID, EEnemyAttackShape.Line, false, EPatternGroup.Large, 10f, 12f,
                    new[] { 1.0f, 1.1f, 1.1f }, flatDirection, new[] { 1.0f, 1.1f, 1.2f }, recent,
                    new PhaseTwoExecution(2f, 6f, 1f, EnemyCatalogSO.AttackShapeSpec.Line(27f, 1.5f), 0.03f,
                        beamTickInterval: 0.25f, starVertexCount: 5, starRingRadius: 14f, starConnectStep: 2, starRotationDegrees: 20f)),
                // P02는 전장 중심에서 좌우 5m에 폭 2m 평행 빔 두 줄을 예고 시작 순간의 방향으로 고정한다(가운데 안전 간격 8m). 예고 1.5초, 유지 4초(실행 구간),
                // 활성 순간부터 0.5초 간격 8회, 회당 0.08Href, 방어 불가, 회복 0.8초이고 직접 제어는 없다. 체력 25% 이하에서도 수치는 같다
                // 형태의 길이(36m)는 전장 안쪽으로 잘리므로 판정에 쓰지 않는 표시용 상한이다
                MakePhaseTwo(WOLBAEK_P2_P02_ID, EEnemyAttackShape.Line, false, EPatternGroup.Large, 10f, 12f,
                    new[] { 1.0f, 1.1f, 1.1f }, flatDirection, new[] { 1.0f, 1.1f, 1.2f }, recent,
                    new PhaseTwoExecution(1.5f, 4f, 0.8f, EnemyCatalogSO.AttackShapeSpec.Line(36f, 2f), 0.08f,
                        beamCount: 2, beamLateralOffset: 5f, beamTickInterval: 0.5f)),
                // P03은 선택한 쪽 반원 180도를 45도씩 4구역으로 나눠 예고 종료 순간부터 0.25초 간격으로 차례로 폭발한다. 반경 6m, 예고 1.2초, 실행 0.75초(첫 폭발부터 마지막 폭발까지),
                // 회복 0.8초, 피해 0.18Href, 반격 가능이고 직접 제어는 없다. 체력 25% 이하에서도 수치와 순서는 같다
                MakePhaseTwo(WOLBAEK_P2_P03_ID, EEnemyAttackShape.Fan, true, EPatternGroup.Large, 12f, 14f,
                    new[] { 1.3f, 1.2f, 0.7f }, flatDirection, new[] { 1.0f, 1.15f, 1.3f }, recent,
                    new PhaseTwoExecution(1.2f, 0.75f, 0.8f, EnemyCatalogSO.AttackShapeSpec.Fan(6f, 180f), 0.18f,
                        sweepZoneCount: 4, sweepIntervalSeconds: 0.25f)),
                // P04는 서로 다른 생존 플레이어 최대 2명에게 장판을 하나씩 동시에 설치한다. 예고 1초, 반경 3m, 지속 6초, 간격 1초(활성 순간부터 6회), 회당 0.08Href, 방어 불가, 회복 0.5초이고
                // 직접 제어는 없다. 보스 전체 장판(기존 월백 장판 포함) 상한은 3개이고 체력 25% 이하에서도 대상 수와 수치는 같다. 지연 시 장판당 한 틱에 한 회만 판정한다
                MakePhaseTwo(WOLBAEK_P2_P04_ID, EEnemyAttackShape.TargetArea, false, EPatternGroup.Pressure, 16f, 18f,
                    new[] { 0.9f, 1.2f, 1.4f }, flatDirection, new[] { 1.0f, 1.25f, 1.5f }, recent,
                    new PhaseTwoExecution(1f, 0f, 0.5f, EnemyCatalogSO.AttackShapeSpec.TargetArea(3f), 0.08f,
                        areaDurationSeconds: 6f, areaTickInterval: 1f, maxConcurrentAreas: 3, areaTargetCount: 2,
                        areaSkipsMissedRounds: true)),
                // P05는 예고 0.9초(처음 0.45초 최대 120도/초 추적 뒤 고정), 같은 방향으로 3발을 0.3초 간격으로 쏜다. 실행 구간 0.6초는 첫 발부터 마지막 발까지이고
                // 회복 0.5초만 직접 제어를 받는다. 발마다 1페이즈 P04와 같이 폭 1m, 속도 10m/초, 사거리 35m, 방어 전 피해 0.18Href, 반격 가능이다
                MakePhaseTwo(WOLBAEK_P2_P05_ID, EEnemyAttackShape.Projectile, true, EPatternGroup.Basic, 22f, 18f,
                    new[] { 0.5f, 1.4f, 1.7f }, new[] { 1.2f, 1.0f, 0.7f }, new[] { 1.2f, 0.9f, 0.6f }, recent,
                    new PhaseTwoExecution(0.9f, 0.6f, 0.5f, EnemyCatalogSO.AttackShapeSpec.Projectile(35f, 1f, 10f), 0.18f,
                        0.45f, 120f, EBossActionState.Recover, 0.5f, 3, 0.3f)),
                // P06은 기획이 정한 실행 수치까지 넣었다. 예고 1.2초, 반경 5m, 방어 전 피해 0.28Href, 반격 가능, 회복 0.8초이고 체력 25% 이하에서도 같다
                MakePhaseTwo(WOLBAEK_P2_P06_ID, EEnemyAttackShape.Circle, true, EPatternGroup.Pressure, 8f, 9f,
                    new[] { 1.7f, 0.4f, 0f }, flatDirection, new[] { 0.5f, 1.6f, 2.4f }, recent,
                    new PhaseTwoExecution(1.2f, 0f, 0.8f, EnemyCatalogSO.AttackShapeSpec.Circle(5f), 0.28f))
            };
        }

        /// <summary> 2페이즈 강화 구간 기준 체력 비율, 기획안의 25%이며 이하(경계 포함)에서 강화한다 </summary>
        public const float WOLBAEK_ENHANCED_HEALTH_RATE = 0.25f;

        /// <summary> 2페이즈 흐름 배율 18칸의 기획안 값, 체력 구간(초과, 이하) x 직전 그룹(Large, Pressure, Basic) x 다음 그룹 순서 </summary>
        public static float[] CreateWolbaekFlowDefaults()
        {
            return new[]
            {
                // 25% 초과, 직전 Large
                0.25f, 1.25f, 1.6f,
                // 직전 Pressure
                1.15f, 0.6f, 1.2f,
                // 직전 Basic
                1.3f, 1.1f, 1.0f,
                // 25% 이하, 직전 Large
                0.45f, 1.35f, 1.25f,
                // 직전 Pressure
                1.15f, 0.6f, 1.2f,
                // 직전 Basic
                1.3f, 1.1f, 1.0f
            };
        }

        // 공격이 구현된 패턴만 실행 수치를 넣는다. 나머지는 비워 둬서 IsPatternReady가 거짓으로 남는다
        private readonly struct PhaseTwoExecution
        {
            public readonly float                          TelegraphSeconds;
            public readonly float                          ActiveSeconds;
            public readonly float                          RecoverySeconds;
            public readonly EnemyCatalogSO.AttackShapeSpec Shape;
            public readonly float                          DamageRef;
            public readonly float                          TrackingSeconds;
            public readonly float                          TurnRate;
            public readonly EBossActionState               ControlPhase;
            public readonly float                          ControlWindowSeconds;
            public readonly int                            ProjectileCount;
            public readonly float                          ProjectileIntervalSeconds;
            public readonly float                          AreaDurationSeconds;
            public readonly float                          AreaTickInterval;
            public readonly int                            MaxConcurrentAreas;
            public readonly int                            AreaTargetCount;
            public readonly bool                           AreaSkipsMissedRounds;
            public readonly int                            SweepZoneCount;
            public readonly float                          SweepIntervalSeconds;
            public readonly int                            BeamCount;
            public readonly float                          BeamLateralOffset;
            public readonly float                          BeamTickInterval;
            public readonly int                            StarVertexCount;
            public readonly float                          StarRingRadius;
            public readonly int                            StarConnectStep;
            public readonly float                          StarRotationDegrees;

            public PhaseTwoExecution(float telegraphSeconds, float activeSeconds, float recoverySeconds,
                EnemyCatalogSO.AttackShapeSpec shape, float damageRef, float trackingSeconds = 0f, float turnRate = 0f,
                EBossActionState controlPhase = NO_CONTROL_PHASE, float controlWindowSeconds = 0f, int projectileCount = 0,
                float projectileIntervalSeconds = 0f, float areaDurationSeconds = 0f, float areaTickInterval = 0f,
                int maxConcurrentAreas = 0, int areaTargetCount = 0, bool areaSkipsMissedRounds = false,
                int sweepZoneCount = 0, float sweepIntervalSeconds = 0f,
                int beamCount = 0, float beamLateralOffset = 0f, float beamTickInterval = 0f,
                int starVertexCount = 0, float starRingRadius = 0f, int starConnectStep = 0, float starRotationDegrees = 0f)
            {
                StarVertexCount       = starVertexCount;
                StarRingRadius        = starRingRadius;
                StarConnectStep       = starConnectStep;
                StarRotationDegrees   = starRotationDegrees;
                BeamCount             = beamCount;
                BeamLateralOffset     = beamLateralOffset;
                BeamTickInterval      = beamTickInterval;
                SweepZoneCount        = sweepZoneCount;
                SweepIntervalSeconds  = sweepIntervalSeconds;
                AreaDurationSeconds   = areaDurationSeconds;
                AreaTickInterval      = areaTickInterval;
                MaxConcurrentAreas    = maxConcurrentAreas;
                AreaTargetCount       = areaTargetCount;
                AreaSkipsMissedRounds = areaSkipsMissedRounds;
                TelegraphSeconds = telegraphSeconds;
                ActiveSeconds    = activeSeconds;
                RecoverySeconds  = recoverySeconds;
                Shape            = shape;
                DamageRef        = damageRef;
                TrackingSeconds  = trackingSeconds;
                TurnRate         = turnRate;
                ControlPhase     = controlPhase;
                ControlWindowSeconds = controlWindowSeconds;
                ProjectileCount  = projectileCount;
                ProjectileIntervalSeconds = projectileIntervalSeconds;
            }
        }

        private static PatternDefinition MakePhaseTwo(string id, EEnemyAttackShape shape, bool guardable, EPatternGroup group,
            float baseWeight, float enhancedBaseWeight, float[] distance, float[] direction, float[] nearby, float[] recent,
            PhaseTwoExecution? execution = null)
        {
            if (execution.HasValue)
            {
                PhaseTwoExecution value = execution.Value;
                return new PatternDefinition(id, WOLBAEK_ID, SECOND_PHASE, baseWeight,
                    distance, direction, nearby, (float[])recent.Clone(),
                    value.TelegraphSeconds, value.TrackingSeconds, value.TurnRate, value.ActiveSeconds, value.RecoverySeconds,
                    value.Shape, value.DamageRef, guardable,
                    value.ControlPhase, value.ControlWindowSeconds, false,
                    value.AreaDurationSeconds, value.AreaTickInterval, value.MaxConcurrentAreas,
                    group: group, enhancedBaseWeight: enhancedBaseWeight,
                    projectileCount: value.ProjectileCount, projectileIntervalSeconds: value.ProjectileIntervalSeconds,
                    areaTargetCount: value.AreaTargetCount, areaSkipsMissedRounds: value.AreaSkipsMissedRounds,
                    sweepZoneCount: value.SweepZoneCount, sweepIntervalSeconds: value.SweepIntervalSeconds,
                    beamCount: value.BeamCount, beamLateralOffset: value.BeamLateralOffset, beamTickInterval: value.BeamTickInterval,
                    starVertexCount: value.StarVertexCount, starRingRadius: value.StarRingRadius,
                    starConnectStep: value.StarConnectStep, starRotationDegrees: value.StarRotationDegrees);
            }

            return new PatternDefinition(id, WOLBAEK_ID, SECOND_PHASE, baseWeight,
                distance, direction, nearby, (float[])recent.Clone(),
                UNSET, UNSET, UNSET, UNSET, UNSET,
                new EnemyCatalogSO.AttackShapeSpec(shape, UNSET, UNSET, UNSET, UNSET), UNSET, guardable,
                group: group, enhancedBaseWeight: enhancedBaseWeight);
        }

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

        /// <summary> 보스와 그 1페이즈 패턴의 필수 수치가 모두 지정됐고 범위가 맞는가. 서버 세션은 이 값이 거짓인 보스의 전투를 열지 않는다. 2페이즈 패턴은 IsPhaseTuned가 따로 본다 </summary>
        public bool IsTuned(string bossId)
        {
            return IsPhaseTuned(bossId, FIRST_PHASE);
        }

        /// <summary> 보스와 그 페이즈의 모든 패턴이 지정됐고 범위가 맞는가 </summary>
        public bool IsPhaseTuned(string bossId, int phase)
        {
            var errors = new List<string>();
            ValidateTuning(bossId, phase, errors);
            return errors.Count == 0;
        }

        /// <summary> 한 패턴을 선택 후보로 쓸 수 있게 데이터가 준비됐는가, 보스 값과 패턴 수치를 모두 보고 2페이즈 이상이면 선택 데이터(그룹, 강화 기본값, 흐름 표)도 본다. 공격 구현 여부는 보지 않는다 </summary>
        public bool IsPatternReady(string bossId, PatternDefinition pattern)
        {
            if ((pattern == null) || (pattern.BossId != bossId) || !TryGetBoss(bossId, out BossDefinition boss))
            {
                return false;
            }

            var errors = new List<string>();
            ValidateBoss(errors, boss, true);
            ValidatePattern(errors, pattern, true);
            if (pattern.Phase > FIRST_PHASE)
            {
                ValidateSecondPhaseSelection(errors, boss, pattern);
            }

            return errors.Count == 0;
        }

        /// <summary> 보스와 그 1페이즈 패턴의 미지정 수치와 범위 오류를 결과 목록에 더한다. 미지정 수치를 기본값으로 대신 채워 전투를 열지 않는다 </summary>
        public void ValidateTuning(string bossId, List<string> errors)
        {
            ValidateTuning(bossId, FIRST_PHASE, errors);
        }

        // 페이즈별로 나누는 이유: 2페이즈 선택 데이터는 먼저 들어오고 공격 수치는 공격을 구현할 때 들어오므로, 한꺼번에 검사하면
        // 2페이즈 데이터를 넣는 순간 1페이즈 전투 시작까지 막힌다. 1페이즈 검사 기준은 그대로이고 2페이즈 검사를 우회하지 않는다
        /// <summary> 보스와 한 페이즈 패턴의 미지정 수치와 범위 오류를 결과 목록에 더한다 </summary>
        public void ValidateTuning(string bossId, int phase, List<string> errors)
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
                    if ((pattern == null) || (pattern.BossId != bossId) || (pattern.Phase != phase))
                    {
                        continue;
                    }

                    ++patternCount;
                    ValidatePattern(errors, pattern, true);
                    if (phase > FIRST_PHASE)
                    {
                        ValidateSecondPhaseSelection(errors, boss, pattern);
                    }
                }
            }

            if (patternCount == 0)
            {
                errors.Add($"{OWNER}.{nameof(_patterns)}: 보스 '{bossId}'의 {phase}페이즈 패턴이 없다");
            }
        }

        private static void ValidateSecondPhaseSelection(List<string> errors, BossDefinition boss, PatternDefinition pattern)
        {
            string field = $"{nameof(_patterns)}[{pattern.DefinitionId}]";

            if (pattern.Group == EPatternGroup.None)
            {
                errors.Add($"{OWNER}.{field}.Group: 2페이즈 패턴은 그룹이 필요하다");
            }

            CheckPositive(errors, field + ".EnhancedBaseWeight", pattern.EnhancedBaseWeight, true);
            CheckPositiveAtMost(errors, $"{nameof(_bosses)}[{boss.DefinitionId}].EnhancedHealthRate", boss.EnhancedHealthRate, MAX_RATE, true);
            ValidateFlowTable(errors, $"{nameof(_bosses)}[{boss.DefinitionId}].FlowMultipliers", boss.FlowMultipliers, true);
        }

        private static void ValidateFlowTable(List<string> errors, string field, IReadOnlyList<float> table, bool requireTuned)
        {
            if ((table == null) || (table.Count != FLOW_TABLE_SIZE))
            {
                if (requireTuned || (table != null))
                {
                    errors.Add($"{OWNER}.{field}: 흐름 배율은 {FLOW_TABLE_SIZE}칸이어야 한다");
                }

                return;
            }

            bool hasPositive = false;
            for (int i = 0; i < table.Count; ++i)
            {
                DataValidation.RequireNonNegative(errors, OWNER, $"{field}[{i}]", table[i]);
                hasPositive |= table[i] > 0f;
            }

            if (requireTuned && !hasPositive)
            {
                errors.Add($"{OWNER}.{field}: 흐름 배율이 모두 0이라 미지정이다");
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

                        // 2페이즈 선택 값은 1페이즈 시작 검사에 넣지 않으므로 부트 검사에서 넣은 값의 범위만 본다
                        string bossField = $"{nameof(_bosses)}[{_bosses[i].DefinitionId}]";
                        CheckPositiveAtMost(errors, bossField + ".EnhancedHealthRate", _bosses[i].EnhancedHealthRate, MAX_RATE, false);
                        ValidateFlowTable(errors, bossField + ".FlowMultipliers", _bosses[i].FlowMultipliers, false);
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

                if ((pattern.Phase > FIRST_PHASE) && (pattern.Group == EPatternGroup.None))
                {
                    errors.Add($"{OWNER}.{nameof(_patterns)}[{pattern.DefinitionId}].Group: 2페이즈 이상 패턴은 그룹이 필요하다");
                }

                DataValidation.RequireNonNegative(errors, OWNER, $"{nameof(_patterns)}[{pattern.DefinitionId}].EnhancedBaseWeight", pattern.EnhancedBaseWeight);
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
            DataValidation.RequireNonNegative(errors, OWNER, field + ".AppearanceSeconds", boss.AppearanceSeconds);
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
            ValidateProjectileSequence(errors, field, pattern);
            ValidateAreaTargets(errors, field, pattern);
            ValidateSweep(errors, field, pattern);
            ValidateBeam(errors, field, pattern);
            ValidateStar(errors, field, pattern);
        }

        // 별형 회전 빔은 선 형태여야 하고, 꼭짓점 수와 연결 칸 수와 배치 반경과 회전 속도와 피해 간격이 있어야 한다
        // 연결 칸 수가 꼭짓점 수와 서로소가 아니면 닫힌 별이 되지 않고, 1이면 별이 아니라 다각형이다. 배치 반경은 전장 반지름 이하여야 빔이 전장 밖으로 나가지 않는다
        private void ValidateStar(List<string> errors, string field, PatternDefinition pattern)
        {
            DataValidation.RequireRange(errors, OWNER, field + ".StarVertexCount", pattern.StarVertexCount, 0, MAX_STAR_VERTICES);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".StarRingRadius", pattern.StarRingRadius);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".StarRotationDegrees", pattern.StarRotationDegrees);

            if (pattern.StarVertexCount <= 0)
            {
                return;
            }

            if (pattern.StarVertexCount < MIN_STAR_VERTICES)
            {
                errors.Add($"{OWNER}.{field}.StarVertexCount = {pattern.StarVertexCount}: 별형은 {MIN_STAR_VERTICES}개 이상이어야 한다");
            }

            if ((pattern.Shape != null) && (pattern.Shape.Shape != EEnemyAttackShape.Line))
            {
                errors.Add($"{OWNER}.{field}.Shape: 별형 빔은 선 형태여야 한다");
            }

            if ((pattern.StarConnectStep < 2) || (pattern.StarConnectStep > (pattern.StarVertexCount / 2))
                || (GreatestCommonDivisor(pattern.StarVertexCount, pattern.StarConnectStep) != 1))
            {
                errors.Add($"{OWNER}.{field}.StarConnectStep = {pattern.StarConnectStep}: 꼭짓점 수와 서로소이고 2 이상 절반 이하여야 한다");
            }

            if (pattern.StarRingRadius <= UNSET)
            {
                errors.Add($"{OWNER}.{field}.StarRingRadius: 배치 반경이 필요하다");
            }
            else if (TryGetBoss(pattern.BossId, out BossDefinition boss) && (boss.ArenaDiameter > UNSET)
                && (pattern.StarRingRadius > (boss.ArenaDiameter * 0.5f)))
            {
                errors.Add($"{OWNER}.{field}.StarRingRadius = {pattern.StarRingRadius}: 전장 반지름 {boss.ArenaDiameter * 0.5f}m 이하여야 한다");
            }

            if (pattern.StarRotationDegrees <= UNSET)
            {
                errors.Add($"{OWNER}.{field}.StarRotationDegrees: 회전 속도가 필요하다");
            }

            if (pattern.ActiveSeconds <= UNSET)
            {
                errors.Add($"{OWNER}.{field}.ActiveSeconds: 회전 유지 시간이 필요하다");
            }

            if (pattern.BeamTickInterval <= UNSET)
            {
                errors.Add($"{OWNER}.{field}.BeamTickInterval: 피해 간격이 필요하다");
            }
            else if (pattern.BeamTickInterval > (pattern.ActiveSeconds + SEQUENCE_EPSILON))
            {
                errors.Add($"{OWNER}.{field}.BeamTickInterval = {pattern.BeamTickInterval}: 유지 시간 {pattern.ActiveSeconds}초 이하여야 한다");
            }
        }

        private static int GreatestCommonDivisor(int a, int b)
        {
            while (b != 0)
            {
                int remainder = a % b;
                a = b;
                b = remainder;
            }

            return a;
        }

        // 빔은 선 형태여야 하고, 줄마다 폭과 좌우 거리와 피해 간격이 있어야 하며 간격은 유지 시간(ActiveSeconds) 안이어야 한다. 줄이 겹치면 가운데 간격이 없어지므로 막는다
        private static void ValidateBeam(List<string> errors, string field, PatternDefinition pattern)
        {
            DataValidation.RequireRange(errors, OWNER, field + ".BeamCount", pattern.RawBeamCount, 0, MAX_BEAMS);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".BeamLateralOffset", pattern.BeamLateralOffset);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".BeamTickInterval", pattern.BeamTickInterval);

            if (pattern.RawBeamCount <= 0)
            {
                return;
            }

            if ((pattern.Shape != null) && (pattern.Shape.Shape != EEnemyAttackShape.Line))
            {
                errors.Add($"{OWNER}.{field}.Shape: 빔은 선 형태여야 한다");
            }

            if (pattern.ActiveSeconds <= UNSET)
            {
                errors.Add($"{OWNER}.{field}.ActiveSeconds: 빔 유지 시간이 필요하다");
            }

            if (pattern.BeamTickInterval <= UNSET)
            {
                errors.Add($"{OWNER}.{field}.BeamTickInterval: 피해 간격이 필요하다");
            }
            else if (pattern.BeamTickInterval > (pattern.ActiveSeconds + SEQUENCE_EPSILON))
            {
                errors.Add($"{OWNER}.{field}.BeamTickInterval = {pattern.BeamTickInterval}: 유지 시간 {pattern.ActiveSeconds}초 이하여야 한다");
            }

            if ((pattern.RawBeamCount > 1) && (pattern.Shape != null)
                && (pattern.Shape.Width > (pattern.BeamLateralOffset * BEAM_LATERAL_FACTOR)))
            {
                errors.Add($"{OWNER}.{field}.Shape.Width = {pattern.Shape.Width}: 줄 사이 거리 {pattern.BeamLateralOffset * BEAM_LATERAL_FACTOR}m를 넘으면 줄이 겹친다");
            }

            if (pattern.BeamLateralOffset <= UNSET)
            {
                errors.Add($"{OWNER}.{field}.BeamLateralOffset: 좌우 거리가 필요하다");
            }
        }

        // 연쇄 폭발은 부채꼴 형태여야 하고, 여러 구역이면 간격이 있어야 하며 실행 구간이 첫 구역부터 마지막 구역까지를 덮어야 마지막 폭발이 실행 밖으로 밀리지 않는다
        private static void ValidateSweep(List<string> errors, string field, PatternDefinition pattern)
        {
            DataValidation.RequireRange(errors, OWNER, field + ".SweepZoneCount", pattern.RawSweepZoneCount, 0, MAX_SWEEP_ZONES);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".SweepIntervalSeconds", pattern.SweepIntervalSeconds);

            if (pattern.RawSweepZoneCount <= 0)
            {
                return;
            }

            if ((pattern.Shape != null) && (pattern.Shape.Shape != EEnemyAttackShape.Fan))
            {
                errors.Add($"{OWNER}.{field}.Shape: 연쇄 폭발은 부채꼴 형태여야 한다");
            }

            if (pattern.RawSweepZoneCount == 1)
            {
                return;
            }

            if (pattern.SweepIntervalSeconds <= UNSET)
            {
                errors.Add($"{OWNER}.{field}.SweepIntervalSeconds: 여러 구역이면 간격이 필요하다");
                return;
            }

            float sequence = (pattern.RawSweepZoneCount - 1) * pattern.SweepIntervalSeconds;
            if (pattern.ActiveSeconds < (sequence - SEQUENCE_EPSILON))
            {
                errors.Add($"{OWNER}.{field}.ActiveSeconds = {pattern.ActiveSeconds}: 첫 구역부터 마지막 구역까지 {sequence}초를 덮어야 한다");
            }
        }

        // 여러 대상 장판은 동시 장판 상한이 한 시전의 설치 수보다 작지 않아야 한 번에 모두 설치할 수 있다
        private static void ValidateAreaTargets(List<string> errors, string field, PatternDefinition pattern)
        {
            DataValidation.RequireRange(errors, OWNER, field + ".AreaTargetCount", pattern.RawAreaTargetCount, 0, MAX_AREA_TARGETS);

            if ((pattern.RawAreaTargetCount > 1) && (pattern.MaxConcurrentAreas < pattern.RawAreaTargetCount))
            {
                errors.Add($"{OWNER}.{field}.MaxConcurrentAreas = {pattern.MaxConcurrentAreas}: 대상 수 {pattern.RawAreaTargetCount}개를 한 번에 설치하려면 그 이상이어야 한다");
            }
        }

        // 한 발이면(0 또는 1) 간격은 쓰지 않는다. 여러 발이면 간격이 있어야 하고, 실행 구간이 첫 발부터 마지막 발까지를 덮어야 마지막 발이 실행 밖으로 밀리지 않는다
        private static void ValidateProjectileSequence(List<string> errors, string field, PatternDefinition pattern)
        {
            DataValidation.RequireRange(errors, OWNER, field + ".ProjectileCount", pattern.RawProjectileCount, 0, MAX_PROJECTILE_COUNT);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".ProjectileIntervalSeconds", pattern.ProjectileIntervalSeconds);

            if (pattern.RawProjectileCount <= 1)
            {
                return;
            }

            if (pattern.ProjectileIntervalSeconds <= UNSET)
            {
                errors.Add($"{OWNER}.{field}.ProjectileIntervalSeconds: 여러 발이면 간격이 필요하다");
                return;
            }

            float sequence = (pattern.RawProjectileCount - 1) * pattern.ProjectileIntervalSeconds;
            if (pattern.ActiveSeconds < (sequence - SEQUENCE_EPSILON))
            {
                errors.Add($"{OWNER}.{field}.ActiveSeconds = {pattern.ActiveSeconds}: 첫 발부터 마지막 발까지 {sequence}초를 덮어야 한다");
            }
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
