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

            // 이 패턴이 속한 난이도. 필드가 없는 기존 에셋과 기존 정의는 0인 Easy로 읽혀 값이 바뀌지 않는다
            [SerializeField] private EBossDifficulty                _difficulty;

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

            // 단계형 별 모양 빔만 쓴다. 0은 미지정이고 회전형이다. 단계형은 회전 속도와 피해 간격을 쓰지 않는다
            // 첫 단계 타격은 예고 시간(TelegraphSeconds)이 끝나는 순간이고 실행 시간(ActiveSeconds)은 첫 단계부터 마지막 단계까지다
            [SerializeField] private int                            _starStageCount;            // 한 시전의 단계 수
            [SerializeField] private float                          _starStageDegrees;          // 도, 단계마다 별을 돌리는 각도
            [SerializeField] private float                          _starStageIntervalSeconds;  // 초, 단계와 단계 사이(다음 단계 예고 길이이기도 하다)

            // 워프 후 충격파만 쓴다. 0은 미지정이고 워프 패턴이 아니다. 충격파 반경은 형태의 Range, 피해는 DamageRef, 예고는 TelegraphSeconds, 회복은 RecoverySeconds다
            [SerializeField] private float                          _warpMinTargetDistance;     // m, 선택 때 대상과의 최소 수평 거리
            [SerializeField] private float                          _warpLandingDistance;       // m, 대상 둘레 착지 후보 반경, 후방 후보는 대상 뒤 이 거리다
            [SerializeField] private int                            _warpCandidateCount;        // 후방, 대각, 측면, 전방 순서로 한 시전이 검사하는 착지 후보의 최대 수

            // 격자 순차 빔만 쓴다. 0은 미지정이고 격자 빔이 아니다. 줄 폭은 형태의 Width, 첫 타격은 예고 시간(TelegraphSeconds)이 끝나는 순간이다
            // 실행 시간(ActiveSeconds)은 첫 타격부터 마지막 타격까지이고 마지막 타격이 실행 끝 경계에 놓인다
            [SerializeField] private int                            _gridLineCount;             // 한 방향(가로, 세로)의 줄 수, 가로가 먼저이고 세로가 뒤다
            [SerializeField] private float                          _gridLineSpacing;           // m, 이웃한 줄의 중심선 사이 거리
            [SerializeField] private float                          _gridStrikeIntervalSeconds; // 초, 같은 방향의 이웃한 줄 타격 사이
            [SerializeField] private float                          _gridAxisGapSeconds;        // 초, 가로 마지막 타격에서 세로 첫 타격까지

            // 잔류 부채꼴 연쇄만 쓴다. 0은 미지정이고 잔류 연쇄가 아니다. 구역 수와 간격은 연쇄 값(SweepZoneCount, SweepIntervalSeconds)을 그대로 쓴다
            // 형태의 ArcAngle은 폭발 구역 전체 각도이고 360도에서 뺀 나머지가 피난 방향이다. 형태의 Range는 최소 사거리이고 첫 폭발은 예고 시간(TelegraphSeconds)이 끝나는 순간이다
            [SerializeField] private float                          _chainLingerSeconds;        // 초, 마지막 폭발 뒤 이미 폭발한 구역이 위험한 시간
            [SerializeField] private float                          _chainSafeRevealSeconds;    // 초, 시전 시작부터 피난 방향만 보여 주는 시간, 이후 예고 시간이 끝날 때까지 바닥을 예고한다
            [SerializeField] private float                          _chainHitCooldownSeconds;   // 초, 같은 대상이 다시 맞기까지의 최소 간격
            [SerializeField] private int                            _chainMaxHitsPerTarget;     // 한 시전에서 대상 한 명이 맞는 최대 횟수, 막힌 유효 타격도 센다

            // 전방위 투사체 난사만 쓴다. 0은 미지정이고 첫 명중에 사라지는 기존 규칙이다. 발 수(ProjectileCount)가 전방위를 나누는 구역 수이고
            // 첫 발은 예고 시간(TelegraphSeconds)이 끝나는 순간이며 각 발의 예고는 발사 TelegraphSeconds 전에 시작한다. 실행 시간(ActiveSeconds)은 첫 발부터 마지막 발까지다
            [SerializeField] private int                            _projectileMaxHitsPerTarget; // 한 시전에서 대상 한 명이 맞는 최대 횟수, 막힌 유효 타격도 센다

            // 동시 원형 폭발만 쓴다. 0은 미지정이고 동시 폭발이 아니다. 보스 중심 원의 반경은 형태의 Range, 예고 시간(TelegraphSeconds)이 끝나는 순간이 모든 원의 판정 시각이다
            // 사분면 네 곳(전장 중심 기준 부호 네 조합)에 같은 반경의 원을 세 개씩 놓는다. 한 사분면의 좌표는 (안쪽, 안쪽), (바깥쪽, 안쪽), (안쪽, 바깥쪽)이다
            [SerializeField] private float                          _burstSpotRadius;           // m, 사분면 원 하나의 반경
            [SerializeField] private float                          _burstNearOffset;           // m, 전장 중심에서 사분면 원 중심까지 안쪽 좌표 크기
            [SerializeField] private float                          _burstFarOffset;            // m, 전장 중심에서 사분면 원 중심까지 바깥쪽 좌표 크기

            public string                         DefinitionId          => _definitionId;
            public string                         DisplayKey            => _displayKey;
            public string                         BossId                => _bossId;
            public int                            Phase                 => _phase;
            public EBossDifficulty                Difficulty            => _difficulty;
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

            /// <summary> 데이터에 입력된 단계 수 그대로, 0이면 단계형이 아니라 회전형이다 </summary>
            public int                            RawStarStageCount     => _starStageCount;

            /// <summary> 도, 단계마다 별을 돌리는 각도 </summary>
            public float                          StarStageDegrees      => _starStageDegrees;

            /// <summary> 초, 단계와 단계 사이 </summary>
            public float                          StarStageIntervalSeconds => _starStageIntervalSeconds;

            /// <summary> m, 워프 선택 때 대상과의 최소 수평 거리, 0이면 거리 조건이 없다 </summary>
            public float                          WarpMinTargetDistance => _warpMinTargetDistance;

            /// <summary> m, 착지 후보를 놓는 대상 둘레 반경, 후방 후보가 대상 뒤 이 거리에 놓인다 </summary>
            public float                          WarpLandingDistance   => _warpLandingDistance;

            /// <summary> 데이터에 입력된 착지 후보 수 그대로, 0이면 워프 패턴이 아니다 </summary>
            public int                            WarpCandidateCount    => _warpCandidateCount;

            /// <summary> 데이터에 입력된 한 방향의 줄 수 그대로, 0이면 격자 빔이 아니다 </summary>
            public int                            RawGridLineCount      => _gridLineCount;

            /// <summary> m, 이웃한 줄의 중심선 사이 거리 </summary>
            public float                          GridLineSpacing       => _gridLineSpacing;

            /// <summary> 초, 같은 방향의 이웃한 줄 타격 사이 </summary>
            public float                          GridStrikeIntervalSeconds => _gridStrikeIntervalSeconds;

            /// <summary> 초, 가로 마지막 타격에서 세로 첫 타격까지 </summary>
            public float                          GridAxisGapSeconds    => _gridAxisGapSeconds;

            /// <summary> 초, 마지막 폭발 뒤 이미 폭발한 구역이 위험한 시간, 0이면 잔류 연쇄가 아니다 </summary>
            public float                          ChainLingerSeconds    => _chainLingerSeconds;

            /// <summary> 초, 시전 시작부터 피난 방향만 보여 주는 시간 </summary>
            public float                          ChainSafeRevealSeconds => _chainSafeRevealSeconds;

            /// <summary> 초, 같은 대상이 다시 맞기까지의 최소 간격 </summary>
            public float                          ChainHitCooldownSeconds => _chainHitCooldownSeconds;

            /// <summary> 한 시전에서 대상 한 명이 맞는 최대 횟수 </summary>
            public int                            ChainMaxHitsPerTarget => _chainMaxHitsPerTarget;

            /// <summary> 한 시전에서 대상 한 명이 투사체에 맞는 최대 횟수, 0이면 대상 상한 없이 첫 명중에 사라지는 기존 규칙이다 </summary>
            public int                            ProjectileMaxHitsPerTarget => _projectileMaxHitsPerTarget;

            /// <summary> m, 동시 원형 폭발의 사분면 원 하나의 반경, 0이면 동시 폭발이 아니다 </summary>
            public float                          BurstSpotRadius       => _burstSpotRadius;

            /// <summary> m, 전장 중심에서 사분면 원 중심까지 안쪽 좌표의 크기 </summary>
            public float                          BurstNearOffset       => _burstNearOffset;

            /// <summary> m, 전장 중심에서 사분면 원 중심까지 바깥쪽 좌표의 크기 </summary>
            public float                          BurstFarOffset        => _burstFarOffset;

            /****************************************
            *                Methods
            ****************************************/

            /// <summary> 범주만 정하고 수치를 모두 미지정으로 둔 패턴, 배율 표는 열 수만 맞춘다 </summary>
            public PatternDefinition(string definitionId, string bossId, int phase, EEnemyAttackShape shape, bool guardable,
                EBossActionState controlPhase = NO_CONTROL_PHASE, bool controlCancelsPattern = false,
                bool areaSkipsMissedRounds = false, EBossDifficulty difficulty = EBossDifficulty.Easy)
                : this(definitionId, bossId, phase, UNSET,
                    new float[DISTANCE_BAND_COUNT], new float[DIRECTION_BAND_COUNT], new float[NEARBY_BAND_COUNT], new float[RECENT_USE_SLOTS],
                    UNSET, UNSET, UNSET, UNSET, UNSET,
                    new EnemyCatalogSO.AttackShapeSpec(shape, UNSET, UNSET, UNSET, UNSET), UNSET, guardable,
                    controlPhase, UNSET, controlCancelsPattern, areaSkipsMissedRounds: areaSkipsMissedRounds,
                    difficulty: difficulty)
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
                int starVertexCount = 0, float starRingRadius = 0f, int starConnectStep = 0, float starRotationDegrees = 0f,
                EBossDifficulty difficulty = EBossDifficulty.Easy,
                float warpMinTargetDistance = 0f, float warpLandingDistance = 0f, int warpCandidateCount = 0,
                int starStageCount = 0, float starStageDegrees = 0f, float starStageIntervalSeconds = 0f,
                int gridLineCount = 0, float gridLineSpacing = 0f, float gridStrikeIntervalSeconds = 0f, float gridAxisGapSeconds = 0f,
                float chainLingerSeconds = 0f, float chainSafeRevealSeconds = 0f, float chainHitCooldownSeconds = 0f,
                int chainMaxHitsPerTarget = 0, int projectileMaxHitsPerTarget = 0,
                float burstSpotRadius = 0f, float burstNearOffset = 0f, float burstFarOffset = 0f)
            {
                _burstSpotRadius            = burstSpotRadius;
                _burstNearOffset            = burstNearOffset;
                _burstFarOffset             = burstFarOffset;
                _projectileMaxHitsPerTarget = projectileMaxHitsPerTarget;
                _chainLingerSeconds        = chainLingerSeconds;
                _chainSafeRevealSeconds    = chainSafeRevealSeconds;
                _chainHitCooldownSeconds   = chainHitCooldownSeconds;
                _chainMaxHitsPerTarget     = chainMaxHitsPerTarget;
                _gridLineCount             = gridLineCount;
                _gridLineSpacing           = gridLineSpacing;
                _gridStrikeIntervalSeconds = gridStrikeIntervalSeconds;
                _gridAxisGapSeconds        = gridAxisGapSeconds;
                _starStageCount            = starStageCount;
                _starStageDegrees          = starStageDegrees;
                _starStageIntervalSeconds  = starStageIntervalSeconds;
                _warpMinTargetDistance     = warpMinTargetDistance;
                _warpLandingDistance       = warpLandingDistance;
                _warpCandidateCount        = warpCandidateCount;
                _difficulty                = difficulty;
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
        public const string WOLBAEK_P2_P07_ID = "wolbaek_ph2_p07";

        // 월백 하드 1페이즈 패턴 ID, 이지 ID와 겹치지 않는다. P01~P06 여섯 개가 모두 있다
        public const string WOLBAEK_HARD_P01_ID = "wolbaek_hard_p01";
        public const string WOLBAEK_HARD_P02_ID = "wolbaek_hard_p02";
        public const string WOLBAEK_HARD_P03_ID = "wolbaek_hard_p03";
        public const string WOLBAEK_HARD_P04_ID = "wolbaek_hard_p04";
        public const string WOLBAEK_HARD_P05_ID = "wolbaek_hard_p05";
        public const string WOLBAEK_HARD_P06_ID = "wolbaek_hard_p06";

        // 월백 하드 2페이즈 패턴 ID, 이지 2페이즈 ID와 겹치지 않는다. P01~P07 일곱 개가 모두 있어야 하드 2페이즈가 준비된 것으로 본다
        public const string WOLBAEK_HARD_P2_P01_ID = "wolbaek_hard_ph2_p01";
        public const string WOLBAEK_HARD_P2_P02_ID = "wolbaek_hard_ph2_p02";
        public const string WOLBAEK_HARD_P2_P03_ID = "wolbaek_hard_ph2_p03";
        public const string WOLBAEK_HARD_P2_P04_ID = "wolbaek_hard_ph2_p04";
        public const string WOLBAEK_HARD_P2_P05_ID = "wolbaek_hard_ph2_p05";
        public const string WOLBAEK_HARD_P2_P06_ID = "wolbaek_hard_ph2_p06";
        public const string WOLBAEK_HARD_P2_P07_ID = "wolbaek_hard_ph2_p07";

        // 무철 패턴 ID, 이지 1페이즈(mucheol_p01~06)와 2페이즈(mucheol_ph2_p01~06)를 하드가 같은 정의로 다시 쓴다. 하드 전용은 P07 하나다
        public const string MUCHEOL_P01_ID = "mucheol_p01";
        public const string MUCHEOL_P02_ID = "mucheol_p02";
        public const string MUCHEOL_P03_ID = "mucheol_p03";
        public const string MUCHEOL_P04_ID = "mucheol_p04";
        public const string MUCHEOL_P05_ID = "mucheol_p05";
        public const string MUCHEOL_P06_ID = "mucheol_p06";
        public const string MUCHEOL_P2_P01_ID = "mucheol_ph2_p01";
        public const string MUCHEOL_P2_P02_ID = "mucheol_ph2_p02";
        public const string MUCHEOL_P2_P03_ID = "mucheol_ph2_p03";
        public const string MUCHEOL_P2_P04_ID = "mucheol_ph2_p04";
        public const string MUCHEOL_P2_P05_ID = "mucheol_ph2_p05";
        public const string MUCHEOL_P2_P06_ID = "mucheol_ph2_p06";
        public const string MUCHEOL_HARD_P2_P07_ID = "mucheol_ph2_p07";

        // 무철 하드 전용 정의 ID. P01~P06 각각이 같은 번호의 이지 정의를 하드에서 대신한다(CollectPatterns가 이지 쪽을 뺀다).
        // 실행 객체와 연출은 ToMucheolBaseId로 이지 ID로 되돌려 같은 것을 쓴다. P07은 하드 전용이라 따로 ID를 두지 않는다
        public const string MUCHEOL_HARD_P01_ID    = "mucheol_hard_p01";
        public const string MUCHEOL_HARD_P02_ID    = "mucheol_hard_p02";
        public const string MUCHEOL_HARD_P03_ID    = "mucheol_hard_p03";
        public const string MUCHEOL_HARD_P04_ID    = "mucheol_hard_p04";
        public const string MUCHEOL_HARD_P05_ID    = "mucheol_hard_p05";
        public const string MUCHEOL_HARD_P06_ID    = "mucheol_hard_p06";
        public const string MUCHEOL_HARD_P2_P01_ID = "mucheol_hard_ph2_p01";
        public const string MUCHEOL_HARD_P2_P02_ID = "mucheol_hard_ph2_p02";
        public const string MUCHEOL_HARD_P2_P03_ID = "mucheol_hard_ph2_p03";
        public const string MUCHEOL_HARD_P2_P04_ID = "mucheol_hard_ph2_p04";
        public const string MUCHEOL_HARD_P2_P05_ID = "mucheol_hard_ph2_p05";
        public const string MUCHEOL_HARD_P2_P06_ID = "mucheol_hard_ph2_p06";

        private const string MUCHEOL_HARD_PREFIX = "mucheol_hard_";
        private const string MUCHEOL_BASE_PREFIX = "mucheol_";

        private const float NEUTRAL_MULTIPLIER = 1f;

        // 한 시전의 발 수 상한, 서버 지속물 관리자의 공용 비행 슬롯 수와 같다. 대상당 피격 상한이 있는 난사는 이전 시전의 잔류 투사체와 겹쳐도
        // 모든 발이 나가도록 관리자의 예비 슬롯까지 쓴다
        private const int   MAX_PROJECTILE_COUNT = 8;

        // 투사체가 한 시전에서 대상 한 명을 맞힐 수 있는 횟수의 상한, 하드 P05 난사의 확정 횟수
        private const int   MAX_PROJECTILE_HITS  = 8;
        private const int   BARRAGE_MAX_HITS     = 2;

        // 하드 P05 난사의 구역 수이자 발 수, 전방위를 45도씩 나눈 여덟 곳이다
        private const int   BARRAGE_SHOTS        = 8;

        // 하드 P04 추적 장판의 계약값, 서버 패턴이 기대하는 시간과 구조다. 추적 4초 뒤 중심을 고정하고 예고 8초에 실행으로 들어가
        // 실행 2.5초가 끝나는 10.5초에 장판이 켜져 60초 동안 1초마다 피해를 준다. 회복은 2초다
        private const float HARD_AREA_TRACKING_SECONDS  = 4f;
        private const float HARD_AREA_TELEGRAPH_SECONDS = 8f;
        private const float HARD_AREA_FIXED_SECONDS     = 2.5f;
        private const float HARD_AREA_RECOVERY_SECONDS  = 2f;
        private const float HARD_AREA_RADIUS           = 6f;
        private const float HARD_AREA_DURATION_SECONDS = 60f;
        private const float HARD_AREA_TICK_SECONDS     = 1f;
        private const float HARD_AREA_DAMAGE_REF       = 0.08f;
        private const int   HARD_AREA_TARGET_COUNT     = 2;

        // 한 시전이 장판을 설치할 수 있는 대상 수의 상한, 서버 패턴의 기술적 상한과 같다
        private const int   MAX_AREA_TARGETS     = 4;

        // 한 시전의 연쇄 구역 수의 상한, 서버 패턴의 기술적 상한과 같다
        private const int   MAX_SWEEP_ZONES      = 8;
        private const float SEQUENCE_EPSILON     = 0.0001f; // 초, 실행 구간 비교의 부동소수 오차

        // 한 시전의 평행 빔 줄 수의 상한, 서버 빔 관리자의 슬롯 수와 같다
        private const int   MAX_BEAMS            = 4;
        private const float BEAM_LATERAL_FACTOR  = 2f;

        // 워프 착지 후보 수의 상한, 대상 둘레를 45도씩 나눈 여덟 곳이다
        private const int   MAX_WARP_CANDIDATES  = 8;

        // 별형 꼭짓점 수의 범위, 서버 별형 패턴의 기술적 상한과 같다
        private const int   MIN_STAR_VERTICES    = 5;
        private const int   MAX_STAR_VERTICES    = 8;      // 두 줄 사이 중심선 거리는 좌우 거리의 두 배

        // 단계형 별 빔의 단계 수 상한, 서버 단계형 패턴의 기술적 상한과 같다
        private const int   MAX_STAR_STAGES      = 4;

        // 격자 순차 빔의 한 방향 줄 수 상한, 서버 격자 패턴의 기술적 상한과 같다
        private const int   MAX_GRID_LINES       = 16;
        private const int   GRID_AXIS_COUNT      = 2;
        private const float GRID_FIT_EPSILON     = 0.001f; // m, 줄 간격과 전장 지름을 비교할 때 부동소수 오차 흡수

        // 잔류 부채꼴 연쇄의 대상당 최대 피격 횟수 상한, 서버 패턴의 기술적 상한과 같다
        private const int   MAX_CHAIN_HITS       = 8;

        // 동시 원형 폭발의 가장 먼 원이 전장 안에 드는지 볼 때 쓰는 값
        private const float RADIUS_PER_DIAMETER  = 0.5f;
        private const float BURST_FIT_EPSILON    = 0.001f; // m, 가장 먼 원과 전장 반지름을 비교할 때 부동소수 오차 흡수

        // 흐름 배율 표, 체력 구간 2 x 직전 그룹 3 x 다음 그룹 3
        private const int FLOW_BAND_COUNT  = 2;
        private const int FLOW_GROUP_COUNT = 3;
        private const int FLOW_TABLE_SIZE  = FLOW_BAND_COUNT * FLOW_GROUP_COUNT * FLOW_GROUP_COUNT;

        private const int    FIRST_PHASE                     = 1;
        private const int    SECOND_PHASE                    = 2;
        private const string WOLBAEK_ID                     = "boss_wolbaek";
        private const string MUCHEOL_ID                      = "boss_mucheol";
        private const int    WOLBAEK_PHASE_ONE_PATTERN_COUNT = 6;

        // 월백 하드 구성, 1페이즈 P01~P06 여섯 개와 2페이즈 P01~P07 일곱 개
        private const int    WOLBAEK_HARD_PHASE_ONE_PATTERN_COUNT = 6;
        private const int    WOLBAEK_HARD_PHASE_TWO_PATTERN_COUNT = 7;
        private const int    NO_EXPECTED_COUNT                    = 0;

        // 무철 구성, 이지와 하드 모두 1페이즈 P01~P06 여섯 개이고 2페이즈는 이지 여섯 개, 하드는 이지 여섯 개에 하드 전용 P07을 더한 일곱 개다
        private const int    MUCHEOL_PHASE_ONE_PATTERN_COUNT      = 6;
        private const int    MUCHEOL_EASY_PHASE_TWO_PATTERN_COUNT = 6;
        private const int    MUCHEOL_HARD_PHASE_TWO_PATTERN_COUNT = 7;

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
            // 산문지기 무철, 월백 뼈대를 그대로 쓰는 새 보스다. 수치는 MucheolDevCatalog의 임시 시작값이며 확정 밸런스가 아니다
            CreateMucheolBossDefault()
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
            patterns.AddRange(CreateMucheolDefaults());
            return patterns.ToArray();
        }

        // 2페이즈 Easy 선택 데이터(기본값, 거리, 방향, 주변, 최근, 그룹)를 기획안에서 옮긴다. 공격 시간, 범위, 피해량은 공격을 구현하고
        // 기획이 수치를 정한 패턴만 넣고(지금은 P06) 나머지는 0으로 비워 둔다. 비운 패턴은 IsPatternReady가 거짓이라 뽑히지 않는다
        // TODO(기웅): 아래 형태 범주와 방어 가능 여부는 자리값이다. P01과 P02는 EEnemyAttackShape에 빔 형태가 없어 Line으로 두었고,
        // 형태 추가는 00.Common/Types 소유자와 협의해야 한다. 각 공격을 구현할 때 확정한다
        /// <summary> 월백 2페이즈 이지 일곱 패턴의 선택 데이터 기본값, 기존 에셋에 더할 때도 같은 값을 쓴다 </summary>
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
                // P03은 선택한 쪽 반원 180도를 45도씩 4구역으로 나눠 예고 종료 순간부터 0.25초 간격으로 차례로 폭발한다. 반경 18m, 예고 1.2초, 실행 0.75초(첫 폭발부터 마지막 폭발까지),
                // 회복 0.8초, 피해 0.18Href, 반격 가능이고 직접 제어는 없다. 체력 25% 이하에서도 수치와 순서는 같다
                MakePhaseTwo(WOLBAEK_P2_P03_ID, EEnemyAttackShape.Fan, true, EPatternGroup.Large, 12f, 14f,
                    new[] { 1.3f, 1.2f, 0.7f }, flatDirection, new[] { 1.0f, 1.15f, 1.3f }, recent,
                    new PhaseTwoExecution(1.2f, 0.75f, 0.8f, EnemyCatalogSO.AttackShapeSpec.Fan(18f, 180f), 0.18f,
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
                    new PhaseTwoExecution(1.2f, 0f, 0.8f, EnemyCatalogSO.AttackShapeSpec.Circle(5f), 0.28f)),
                // P07은 하드 설계안(2026-10-07)의 후방 우선 워프 수치를 이지 시험값으로 옮겼다. 확정 밸런스가 아니며 이지와 하드 정의를 따로 조정한다.
                // 대상과 5m 이상일 때만 선택하고(근거리 배율 0), 시전 시작에 대상 뒤 3m를 먼저 두고 대각, 측면, 전방 순서로 최대 8곳만 검사해 착지한다.
                // 착지 전 표식은 없고 착지한 보스 위치를 중심으로 반경 5m를 1.5초 예고한 뒤 한 번 타격한다. 방어 전 0.24Href, 반격 가능, 회복 1초, 직접 제어 없음이다.
                // 기본값 6.5와 7은 같은 시점의 P01~P06 평균 기본값(13, 13.83)의 절반이다. 거리와 방향과 주변 배율은 중립이고 최근 배율은 P01~P06과 같다.
                // 그룹은 보스 중심 폭발인 P06과 같은 Pressure로 뒀다
                MakePhaseTwo(WOLBAEK_P2_P07_ID, EEnemyAttackShape.Circle, true, EPatternGroup.Pressure, 6.5f, 7f,
                    new[] { 0f, 1f, 1f }, flatDirection, new[] { 1f, 1f, 1f }, recent,
                    new PhaseTwoExecution(1.5f, 0f, 1f, EnemyCatalogSO.AttackShapeSpec.Circle(5f), 0.24f,
                        warpMinTargetDistance: 5f, warpLandingDistance: 3f, warpCandidateCount: MAX_WARP_CANDIDATES))
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

        // 무철 운영 데이터는 MucheolDevCatalog의 임시 시작값을 그대로 옮긴 것이다. 확정 밸런스가 아니며 플레이 테스트 뒤에 에셋에서 조정한다.
        // 전장 지름 44m는 시험 장치(MucheolBossTestRig)의 값을 시작값으로 쓴 것이다. 방어 가능 여부도 임시 가정이다.
        // 하드는 이지 1, 2페이즈 P01~P06 정의를 같은 값으로 다시 쓰고(CollectPatterns, ValidateTuning이 이어 준다) 하드 전용 P07만 따로 둔다
        /// <summary> 무철 보스 정의의 임시 시작값, 이지와 하드가 같은 정의를 쓴다. 55%에서 2페이즈로 넘어가고 25% 이하가 강화 구간이다 </summary>
        public static BossDefinition CreateMucheolBossDefault()
        {
            return new BossDefinition(MUCHEOL_ID, 44f, 700f, 200f, 0.55f, 0.5f, 5f, 12f, 60f, 120f, 5f, 0.25f, 1f, 2f,
                0f, 0.25f, CreateMucheolFlowDefaults());
        }

        /// <summary> 무철 흐름 배율 18칸의 임시 시작값, 체력 구간(초과, 이하) x 직전 그룹(Large, Pressure, Basic) x 다음 그룹 순서 </summary>
        public static float[] CreateMucheolFlowDefaults()
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

        /// <summary> 무철 이지 1페이즈 여섯, 이지 2페이즈 여섯, 하드 1페이즈 여섯, 하드 2페이즈 여섯과 하드 전용 P07의 임시 시작값 </summary>
        public static PatternDefinition[] CreateMucheolDefaults()
        {
            var patterns = new List<PatternDefinition>();
            patterns.AddRange(CreateMucheolPhaseOneDefaults());
            patterns.AddRange(CreateMucheolPhaseTwoDefaults());
            patterns.AddRange(CreateMucheolHardPhaseOneDefaults());
            patterns.AddRange(CreateMucheolHardPhaseTwoReplacementDefaults());
            patterns.Add(CreateMucheolHardPhaseTwoDefault());
            return patterns.ToArray();
        }

        // 1페이즈는 거리 구간이 맞는 곳에서만 고르고, 방향과 주변 배율은 중립이다. 최근 사용 배율은 구간 밖으로 가중치가 0이 되지 않게 0보다 크게 둔다.
        // 이 패턴들은 그룹이 None이고 강화 기본값이 0이다
        private static PatternDefinition[] CreateMucheolPhaseOneDefaults()
        {
            float[] neutral    = { 1f, 1f, 1f };
            float[] nearOnly   = { 1f, 0f, 0f };
            float[] midOnly    = { 0f, 1f, 0f };
            float[] midAndFar  = { 0f, 1f, 1f };
            float[] recentSoft = { 0.5f, 0.8f, 1f, 1f };

            return new[]
            {
                // P01 팔 휘두르기, 부채꼴 5m 120도
                MakeMucheol(MUCHEOL_P01_ID, FIRST_PHASE, EBossDifficulty.Easy, EPatternGroup.None, 30f, 0f,
                    nearOnly, neutral, neutral, recentSoft, 0.7f, 0.35f, 180f, 0f, 0.6f, EnemyCatalogSO.AttackShapeSpec.Fan(5f, 120f), 0.20f, true),
                // P02 양손 내려찍기, 부채꼴 5m 70도
                MakeMucheol(MUCHEOL_P02_ID, FIRST_PHASE, EBossDifficulty.Easy, EPatternGroup.None, 25f, 0f,
                    nearOnly, neutral, neutral, recentSoft, 1.2f, 0.6f, 120f, 0f, 1.2f, EnemyCatalogSO.AttackShapeSpec.Fan(5f, 70f), 0.32f, true),
                // P03 어깨 돌진, 8m 폭 2.5m
                MakeMucheol(MUCHEOL_P03_ID, FIRST_PHASE, EBossDifficulty.Easy, EPatternGroup.None, 15f, 0f,
                    midOnly, neutral, neutral, recentSoft, 0.9f, 0.4f, 150f, 0.5f, 0.8f, EnemyCatalogSO.AttackShapeSpec.Dash(8f, 2.5f), 0.22f, true),
                // P04 발구르기 충격파, 원형 5m
                MakeMucheol(MUCHEOL_P04_ID, FIRST_PHASE, EBossDifficulty.Easy, EPatternGroup.None, 10f, 0f,
                    nearOnly, neutral, neutral, recentSoft, 1.0f, 0f, 0f, 0f, 0.8f, EnemyCatalogSO.AttackShapeSpec.Circle(5f), 0.26f, true),
                // P05 2연타, 타별 형태와 피해는 서버 콤보 데이터가 정한다. 이 정의의 형태와 피해는 선택 문맥과 최소 검증용이다
                MakeMucheol(MUCHEOL_P05_ID, FIRST_PHASE, EBossDifficulty.Easy, EPatternGroup.None, 10f, 0f,
                    nearOnly, neutral, neutral, recentSoft, 0.8f, 0.4f, 150f, 0.5f, 1.2f, EnemyCatalogSO.AttackShapeSpec.Fan(5f, 120f), 0.14f, true),
                // P06 뿔 들이받기, 18m 폭 3m 방어 불가
                MakeMucheol(MUCHEOL_P06_ID, FIRST_PHASE, EBossDifficulty.Easy, EPatternGroup.None, 10f, 0f,
                    midAndFar, neutral, neutral, recentSoft, 1.6f, 0.6f, 90f, 0.75f, 1.5f, EnemyCatalogSO.AttackShapeSpec.Dash(18f, 3f), 0.40f, false)
            };
        }

        // 2페이즈 선택 가중치, 배율, 흐름 배율은 모두 시작안이다. 거리는 근 중 원, 방향은 전 측 후, 주변은 1명 2명 3명, 최근 사용은 직전부터 네 칸이다.
        // TODO(기웅): P06 폭주 돌진의 실행 시간 3.45초는 두 번이 아니라 세 번(강화 구간) 기준이다. 공용 공격 FSM이 실행 시간을 정의 하나로 고정해
        // 횟수별로 줄 수 없어서이며, 두 번 달리는 시전은 남는 구간 동안 서 있는다. 동적 실행 시간 연결은 팀장 승인 대기이고 승인되면 이 값은 2.1초가 된다
        private static PatternDefinition[] CreateMucheolPhaseTwoDefaults()
        {
            float[] neutral = { 1f, 1f, 1f };
            float[] recent  = { 0f, 0.3f, 0.7f, 1f };

            return new[]
            {
                // P01 연속 휘두르기, 좌우 구역
                MakeMucheol(MUCHEOL_P2_P01_ID, SECOND_PHASE, EBossDifficulty.Easy, EPatternGroup.Basic, 22f, 18f,
                    new[] { 1.8f, 0f, 0f }, new[] { 1.3f, 1.2f, 0.6f }, new[] { 1f, 1.1f, 1.2f }, recent,
                    0.9f, 0.4f, 150f, 0.45f, 0.7f, EnemyCatalogSO.AttackShapeSpec.Fan(5f, 120f), 0.16f, true),
                // P02 충격 내려찍기, 부채꼴 뒤 전방 충격파
                MakeMucheol(MUCHEOL_P2_P02_ID, SECOND_PHASE, EBossDifficulty.Easy, EPatternGroup.Large, 12f, 14f,
                    new[] { 1.6f, 0f, 0f }, new[] { 1.6f, 0.8f, 0.3f }, new[] { 1f, 1.2f, 1.3f }, recent,
                    1.2f, 0.6f, 120f, 0.5f, 1.2f, EnemyCatalogSO.AttackShapeSpec.Fan(5f, 70f), 0.30f, true),
                // P03 연속 어깨 돌진, 8m 두 번
                MakeMucheol(MUCHEOL_P2_P03_ID, SECOND_PHASE, EBossDifficulty.Easy, EPatternGroup.Pressure, 16f, 18f,
                    new[] { 0.3f, 1.8f, 1.1f }, neutral, new[] { 1.2f, 1f, 0.8f }, new[] { 0.25f, 0.5f, 0.8f, 1f },
                    0.9f, 0.4f, 150f, 1.4f, 0.9f, EnemyCatalogSO.AttackShapeSpec.Dash(8f, 2.5f), 0.22f, true),
                // P04 이중 지면 충격파, 원형 두 번
                MakeMucheol(MUCHEOL_P2_P04_ID, SECOND_PHASE, EBossDifficulty.Easy, EPatternGroup.Pressure, 14f, 16f,
                    new[] { 1.8f, 0f, 0f }, neutral, new[] { 0.7f, 1.4f, 2f }, recent,
                    1.0f, 0f, 0f, 0.7f, 0.9f, EnemyCatalogSO.AttackShapeSpec.Circle(5f), 0.22f, true),
                // P05 폭주 연타, 좌우 구역 뒤 내려찍기
                MakeMucheol(MUCHEOL_P2_P05_ID, SECOND_PHASE, EBossDifficulty.Easy, EPatternGroup.Large, 12f, 16f,
                    new[] { 1.7f, 0f, 0f }, new[] { 1.4f, 1f, 0.4f }, new[] { 1f, 1f, 1.1f }, recent,
                    0.9f, 0.4f, 150f, 1.0f, 1.5f, EnemyCatalogSO.AttackShapeSpec.Fan(5f, 70f), 0.26f, true),
                // P06 폭주 돌진, 18m 두 번(강화 구간 세 번), 방어 불가
                MakeMucheol(MUCHEOL_P2_P06_ID, SECOND_PHASE, EBossDifficulty.Easy, EPatternGroup.Large, 10f, 14f,
                    new[] { 0.2f, 1.4f, 1.8f }, new[] { 1.5f, 1f, 0.6f }, new[] { 1f, 1.1f, 1f }, recent,
                    2.0f, 0.8f, 90f, 3.45f, 1.8f, EnemyCatalogSO.AttackShapeSpec.Dash(18f, 3f), 0.32f, false)
            };
        }

        // 하드 1페이즈 여섯 정의. 같은 번호의 이지 정의를 대신하며 거리, 방향, 주변, 최근 사용 배열은 이지와 같다.
        // 이지보다 후딜을 줄이고(약 0.75배) 범위와 피해를 키우며 큰 패턴의 가중치를 조금 올린다. 모두 시험 전 임시값이다.
        // 부채꼴 사거리 5m를 7m(1.4배)로, 돌진 폭은 3.0m와 3.5m로, 원형 반경은 6.5m로 키운다
        private static PatternDefinition[] CreateMucheolHardPhaseOneDefaults()
        {
            float[] neutral    = { 1f, 1f, 1f };
            float[] nearOnly   = { 1f, 0f, 0f };
            float[] midOnly    = { 0f, 1f, 0f };
            float[] midAndFar  = { 0f, 1f, 1f };
            float[] recentSoft = { 0.5f, 0.8f, 1f, 1f };

            return new[]
            {
                MakeMucheol(MUCHEOL_HARD_P01_ID, FIRST_PHASE, EBossDifficulty.Hard, EPatternGroup.None, 24f, 0f,
                    nearOnly, neutral, neutral, recentSoft, 1.4f, 0.35f, 180f, 0f, 0.9f, EnemyCatalogSO.AttackShapeSpec.Fan(7f, 120f), 0.23f, true),
                MakeMucheol(MUCHEOL_HARD_P02_ID, FIRST_PHASE, EBossDifficulty.Hard, EPatternGroup.None, 27f, 0f,
                    nearOnly, neutral, neutral, recentSoft, 2.4f, 0.6f, 120f, 0f, 1.8f, EnemyCatalogSO.AttackShapeSpec.Fan(7f, 70f), 0.37f, true),
                MakeMucheol(MUCHEOL_HARD_P03_ID, FIRST_PHASE, EBossDifficulty.Hard, EPatternGroup.None, 15f, 0f,
                    midOnly, neutral, neutral, recentSoft, 1.8f, 0.4f, 180f, 0.5f, 1.2f, EnemyCatalogSO.AttackShapeSpec.Dash(8f, 3f), 0.25f, true),
                MakeMucheol(MUCHEOL_HARD_P04_ID, FIRST_PHASE, EBossDifficulty.Hard, EPatternGroup.None, 10f, 0f,
                    nearOnly, neutral, neutral, recentSoft, 2.0f, 0f, 0f, 0f, 1.2f, EnemyCatalogSO.AttackShapeSpec.Circle(6.5f), 0.30f, true),
                MakeMucheol(MUCHEOL_HARD_P05_ID, FIRST_PHASE, EBossDifficulty.Hard, EPatternGroup.None, 12f, 0f,
                    nearOnly, neutral, neutral, recentSoft, 1.6f, 0.4f, 150f, 0.5f, 1.8f, EnemyCatalogSO.AttackShapeSpec.Fan(7f, 120f), 0.16f, true),
                MakeMucheol(MUCHEOL_HARD_P06_ID, FIRST_PHASE, EBossDifficulty.Hard, EPatternGroup.None, 12f, 0f,
                    midAndFar, neutral, neutral, recentSoft, 2.8f, 0.6f, 90f, 0.75f, 2.4f, EnemyCatalogSO.AttackShapeSpec.Dash(18f, 3.5f), 0.46f, false)
            };
        }

        // 하드 2페이즈 P01~P06 여섯 정의, 같은 번호의 이지 정의를 대신한다. 선택 배열은 이지와 같고 가중치와 실행 값만 다르다.
        // P03과 P06의 돌진 횟수와 간격은 실행 데이터(MucheolMultiDashSpec)가 정하므로 실행 시간은 이지와 같게 둔다
        private static PatternDefinition[] CreateMucheolHardPhaseTwoReplacementDefaults()
        {
            float[] neutral = { 1f, 1f, 1f };
            float[] recent  = { 0f, 0.3f, 0.7f, 1f };

            return new[]
            {
                MakeMucheol(MUCHEOL_HARD_P2_P01_ID, SECOND_PHASE, EBossDifficulty.Hard, EPatternGroup.Basic, 18f, 18f,
                    new[] { 1.8f, 0f, 0f }, new[] { 1.3f, 1.2f, 0.6f }, new[] { 1f, 1.1f, 1.2f }, recent,
                    1.8f, 0.4f, 150f, 0.35f, 1.05f, EnemyCatalogSO.AttackShapeSpec.Fan(7f, 120f), 0.18f, true),
                MakeMucheol(MUCHEOL_HARD_P2_P02_ID, SECOND_PHASE, EBossDifficulty.Hard, EPatternGroup.Large, 12f, 16f,
                    new[] { 1.6f, 0f, 0f }, new[] { 1.6f, 0.8f, 0.3f }, new[] { 1f, 1.2f, 1.3f }, recent,
                    2.4f, 0.6f, 120f, 0.5f, 1.8f, EnemyCatalogSO.AttackShapeSpec.Fan(7f, 70f), 0.35f, true),
                MakeMucheol(MUCHEOL_HARD_P2_P03_ID, SECOND_PHASE, EBossDifficulty.Hard, EPatternGroup.Pressure, 16f, 18f,
                    new[] { 0.3f, 1.8f, 1.1f }, neutral, new[] { 1.2f, 1f, 0.8f }, new[] { 0.25f, 0.5f, 0.8f, 1f },
                    1.8f, 0.4f, 180f, 1.4f, 1.35f, EnemyCatalogSO.AttackShapeSpec.Dash(8f, 3f), 0.25f, true),
                MakeMucheol(MUCHEOL_HARD_P2_P04_ID, SECOND_PHASE, EBossDifficulty.Hard, EPatternGroup.Pressure, 14f, 16f,
                    new[] { 1.8f, 0f, 0f }, neutral, new[] { 0.7f, 1.4f, 2f }, recent,
                    2.0f, 0f, 0f, 0.7f, 1.35f, EnemyCatalogSO.AttackShapeSpec.Circle(6.5f), 0.25f, true),
                MakeMucheol(MUCHEOL_HARD_P2_P05_ID, SECOND_PHASE, EBossDifficulty.Hard, EPatternGroup.Large, 14f, 18f,
                    new[] { 1.7f, 0f, 0f }, new[] { 1.4f, 1f, 0.4f }, new[] { 1f, 1f, 1.1f }, recent,
                    1.8f, 0.4f, 150f, 0.85f, 2.25f, EnemyCatalogSO.AttackShapeSpec.Fan(7f, 70f), 0.30f, true),
                MakeMucheol(MUCHEOL_HARD_P2_P06_ID, SECOND_PHASE, EBossDifficulty.Hard, EPatternGroup.Large, 12f, 16f,
                    new[] { 0.2f, 1.4f, 1.8f }, new[] { 1.5f, 1f, 0.6f }, new[] { 1f, 1.1f, 1f }, recent,
                    3.5f, 0.8f, 90f, 3.45f, 3.0f, EnemyCatalogSO.AttackShapeSpec.Dash(18f, 3.5f), 0.37f, false)
            };
        }

        // 하드 전용 P07 전장 공격, 방어 불가 단발이다. 실행 시간이 없어 예고가 끝나는 순간 타격하고 회복으로 넘어간다.
        // 추적 시간과 회전 속도가 0인 것은 방향을 예고 시작 순간에 고정한다는 뜻이다. 반경 44m는 지름 44m 전장에서 가장자리에 선 보스가 반대편까지 덮는 값이고
        // 각도 315도는 등 쪽 45도 안전지대의 나머지다. 거리, 방향, 주변 인원 배율은 전 구간 1이고 최근 사용 배율이 최근 세 번 안에 쓴 패턴을 다시 고르지 않게 한다.
        // 밀림 거리 1m는 정의가 아니라 서버 실행 데이터(MucheolBattlefieldSpec)가 가진다
        private static PatternDefinition CreateMucheolHardPhaseTwoDefault()
        {
            float[] neutral = { 1f, 1f, 1f };

            return MakeMucheol(MUCHEOL_HARD_P2_P07_ID, SECOND_PHASE, EBossDifficulty.Hard, EPatternGroup.Large, 3f, 3f,
                neutral, neutral, neutral, new[] { 0f, 0f, 0f, 1f },
                4f, 0f, 0f, 0f, 1.8f, EnemyCatalogSO.AttackShapeSpec.Fan(44f, 315f), 0.30f, false);
        }

        private static PatternDefinition MakeMucheol(string id, int phase, EBossDifficulty difficulty, EPatternGroup group,
            float baseWeight, float enhancedBaseWeight, float[] distance, float[] direction, float[] nearby, float[] recent,
            float telegraphSeconds, float trackingSeconds, float turnRate, float activeSeconds, float recoverySeconds,
            EnemyCatalogSO.AttackShapeSpec shape, float damageRef, bool guardable)
        {
            return new PatternDefinition(id, MUCHEOL_ID, phase, baseWeight,
                (float[])distance.Clone(), (float[])direction.Clone(), (float[])nearby.Clone(), (float[])recent.Clone(),
                telegraphSeconds, trackingSeconds, turnRate, activeSeconds, recoverySeconds,
                shape, damageRef, guardable,
                group: group, enhancedBaseWeight: enhancedBaseWeight, difficulty: difficulty);
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
            public readonly float                          WarpMinTargetDistance;
            public readonly float                          WarpLandingDistance;
            public readonly int                            WarpCandidateCount;

            public PhaseTwoExecution(float telegraphSeconds, float activeSeconds, float recoverySeconds,
                EnemyCatalogSO.AttackShapeSpec shape, float damageRef, float trackingSeconds = 0f, float turnRate = 0f,
                EBossActionState controlPhase = NO_CONTROL_PHASE, float controlWindowSeconds = 0f, int projectileCount = 0,
                float projectileIntervalSeconds = 0f, float areaDurationSeconds = 0f, float areaTickInterval = 0f,
                int maxConcurrentAreas = 0, int areaTargetCount = 0, bool areaSkipsMissedRounds = false,
                int sweepZoneCount = 0, float sweepIntervalSeconds = 0f,
                int beamCount = 0, float beamLateralOffset = 0f, float beamTickInterval = 0f,
                int starVertexCount = 0, float starRingRadius = 0f, int starConnectStep = 0, float starRotationDegrees = 0f,
                float warpMinTargetDistance = 0f, float warpLandingDistance = 0f, int warpCandidateCount = 0)
            {
                WarpMinTargetDistance = warpMinTargetDistance;
                WarpLandingDistance   = warpLandingDistance;
                WarpCandidateCount    = warpCandidateCount;
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
                    starConnectStep: value.StarConnectStep, starRotationDegrees: value.StarRotationDegrees,
                    warpMinTargetDistance: value.WarpMinTargetDistance, warpLandingDistance: value.WarpLandingDistance,
                    warpCandidateCount: value.WarpCandidateCount);
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

        // 난이도를 받지 않는 호출은 모두 Easy로 본다. 하드 패턴이 같은 보스와 페이즈에 들어와도 기존 호출의 결과가 달라지지 않는다
        /// <summary> 보스와 페이즈에 속한 이지 패턴을 결과 목록에 채운다, 서버 선택기가 전투 시작 때 한 번 만든다 </summary>
        public void CollectPatterns(string bossId, int phase, List<PatternDefinition> results)
        {
            CollectPatterns(bossId, phase, EBossDifficulty.Easy, results);
        }

        /// <summary> 보스, 페이즈, 난이도에 속한 패턴을 결과 목록에 채운다, 목록은 먼저 비운다. 무철 하드는 이지 정의를 같이 담는다 </summary>
        public void CollectPatterns(string bossId, int phase, EBossDifficulty difficulty, List<PatternDefinition> results)
        {
            results.Clear();
            if (_patterns == null)
            {
                return;
            }

            for (int i = 0; i < _patterns.Length; ++i)
            {
                PatternDefinition pattern = _patterns[i];
                if ((pattern != null) && (pattern.BossId == bossId) && (pattern.Phase == phase) && BelongsToDifficulty(pattern, difficulty))
                {
                    results.Add(pattern);
                }
            }
        }

        /// <summary> 무철 하드 전용 ID(mucheol_hard_...)를 같은 번호의 이지 ID(mucheol_...)로 되돌린다, 다른 ID는 그대로다. 실행 객체와 연출 선택에 쓴다 </summary>
        public static string ToMucheolBaseId(string patternId)
        {
            if ((patternId != null) && patternId.StartsWith(MUCHEOL_HARD_PREFIX, StringComparison.Ordinal))
            {
                return MUCHEOL_BASE_PREFIX + patternId.Substring(MUCHEOL_HARD_PREFIX.Length);
            }

            return patternId;
        }

        /// <summary> 무철 이지 ID(mucheol_...)에 대응하는 하드 전용 ID(mucheol_hard_...)를 돌려준다, 무철 ID가 아니거나 이미 하드 ID면 null이다. 하드 정의가 없는 번호(P07)에도 값을 돌려주므로 정의 유무는 부른 쪽이 본다 </summary>
        public static string ToMucheolHardId(string basePatternId)
        {
            if ((basePatternId == null) || (!basePatternId.StartsWith(MUCHEOL_BASE_PREFIX, StringComparison.Ordinal))
                || basePatternId.StartsWith(MUCHEOL_HARD_PREFIX, StringComparison.Ordinal))
            {
                return null;
            }

            return MUCHEOL_HARD_PREFIX + basePatternId.Substring(MUCHEOL_BASE_PREFIX.Length);
        }

        // 난이도가 정확히 같은 패턴이 속한다. 무철 하드만 예외로 이지 정의를 같이 담되, 같은 번호의 하드 정의가 있으면 그 이지 정의는 뺀다.
        // 월백과 다른 보스는 기존대로 난이도가 정확히 같아야 한다. 이지는 하드 정의를 절대 받지 않는다
        private bool BelongsToDifficulty(PatternDefinition pattern, EBossDifficulty difficulty)
        {
            if (pattern.Difficulty == difficulty)
            {
                return true;
            }

            return (pattern.BossId == MUCHEOL_ID) && (difficulty == EBossDifficulty.Hard) && (pattern.Difficulty == EBossDifficulty.Easy)
                && (!IsReplacedByMucheolHard(pattern));
        }

        // 같은 번호의 무철 하드 정의가 따로 있으면 참이다. 하드 ID를 이지 ID로 되돌려 비교한다
        private bool IsReplacedByMucheolHard(PatternDefinition easyPattern)
        {
            for (int i = 0; i < _patterns.Length; ++i)
            {
                PatternDefinition other = _patterns[i];
                if ((other != null) && (other.BossId == MUCHEOL_ID) && (other.Difficulty == EBossDifficulty.Hard)
                    && (other.DefinitionId != easyPattern.DefinitionId) && (ToMucheolBaseId(other.DefinitionId) == easyPattern.DefinitionId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary> 보스와 그 1페이즈 이지 패턴의 필수 수치가 모두 지정됐고 범위가 맞는가. 서버 세션은 이 값이 거짓인 보스의 전투를 열지 않는다. 2페이즈 패턴은 IsPhaseTuned가 따로 본다 </summary>
        public bool IsTuned(string bossId)
        {
            return IsTuned(bossId, EBossDifficulty.Easy);
        }

        /// <summary> 보스와 그 난이도의 1페이즈 패턴 수치가 모두 지정됐고 범위가 맞는가 </summary>
        public bool IsTuned(string bossId, EBossDifficulty difficulty)
        {
            return IsPhaseTuned(bossId, FIRST_PHASE, difficulty);
        }

        /// <summary> 보스와 그 페이즈의 모든 이지 패턴이 지정됐고 범위가 맞는가 </summary>
        public bool IsPhaseTuned(string bossId, int phase)
        {
            return IsPhaseTuned(bossId, phase, EBossDifficulty.Easy);
        }

        /// <summary> 보스와 그 페이즈, 난이도의 모든 패턴이 지정됐고 범위가 맞는가 </summary>
        public bool IsPhaseTuned(string bossId, int phase, EBossDifficulty difficulty)
        {
            var errors = new List<string>();
            ValidateTuning(bossId, phase, difficulty, errors);
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

        /// <summary> 보스와 그 1페이즈 이지 패턴의 미지정 수치와 범위 오류를 결과 목록에 더한다. 미지정 수치를 기본값으로 대신 채워 전투를 열지 않는다 </summary>
        public void ValidateTuning(string bossId, List<string> errors)
        {
            ValidateTuning(bossId, FIRST_PHASE, EBossDifficulty.Easy, errors);
        }

        /// <summary> 보스와 한 페이즈 이지 패턴의 미지정 수치와 범위 오류를 결과 목록에 더한다 </summary>
        public void ValidateTuning(string bossId, int phase, List<string> errors)
        {
            ValidateTuning(bossId, phase, EBossDifficulty.Easy, errors);
        }

        // 페이즈별로 나누는 이유: 2페이즈 선택 데이터는 먼저 들어오고 공격 수치는 공격을 구현할 때 들어오므로, 한꺼번에 검사하면
        // 2페이즈 데이터를 넣는 순간 1페이즈 전투 시작까지 막힌다. 1페이즈 검사 기준은 그대로이고 2페이즈 검사를 우회하지 않는다
        // 난이도별로 나누는 이유도 같다. 하드 패턴이 미지정인 동안에도 이지 전투가 열려야 하고, 이지 패턴이 하드 검사를 채워 주면 안 된다.
        // 무철 하드만 예외다. 이지 P01~P06을 같은 값으로 다시 쓰므로 검사도 이지 정의를 함께 본다(BelongsToDifficulty)
        /// <summary> 보스와 한 페이즈, 난이도 패턴의 미지정 수치와 범위 오류를 결과 목록에 더한다 </summary>
        public void ValidateTuning(string bossId, int phase, EBossDifficulty difficulty, List<string> errors)
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
                    if ((pattern == null) || (pattern.BossId != bossId) || (pattern.Phase != phase) || !BelongsToDifficulty(pattern, difficulty))
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
                errors.Add($"{OWNER}.{nameof(_patterns)}: 보스 '{bossId}'의 {difficulty} {phase}페이즈 패턴이 없다");
            }

            // 하드 구성은 일부만 있어도 수치 검사는 통과하므로 개수를 따로 본다. 개수가 맞지 않으면 전투를 열지 않는다
            int expectedHardCount = GetWolbaekHardPatternCount(bossId, phase, difficulty);
            if ((expectedHardCount != NO_EXPECTED_COUNT) && (patternCount != expectedHardCount))
            {
                errors.Add($"{OWNER}.{nameof(_patterns)}: 월백 하드 {phase}페이즈 패턴 수 {patternCount} != {expectedHardCount}");
            }

            // 무철은 이지도 개수를 본다. 일부만 들어온 데이터가 수치 검사만으로 준비된 것으로 통과하지 않게 한다
            int expectedMucheolCount = GetMucheolPatternCount(bossId, phase, difficulty);
            if ((expectedMucheolCount != NO_EXPECTED_COUNT) && (patternCount != expectedMucheolCount))
            {
                errors.Add($"{OWNER}.{nameof(_patterns)}: 무철 {difficulty} {phase}페이즈 패턴 수 {patternCount} != {expectedMucheolCount}");
            }
        }

        // 무철 이지는 1, 2페이즈 여섯 개씩, 하드는 1페이즈 여섯 개와 2페이즈 일곱 개다. 다른 보스는 NO_EXPECTED_COUNT다
        private static int GetMucheolPatternCount(string bossId, int phase, EBossDifficulty difficulty)
        {
            if (bossId != MUCHEOL_ID)
            {
                return NO_EXPECTED_COUNT;
            }

            if (phase == FIRST_PHASE)
            {
                return MUCHEOL_PHASE_ONE_PATTERN_COUNT;
            }

            if (phase != SECOND_PHASE)
            {
                return NO_EXPECTED_COUNT;
            }

            return (difficulty == EBossDifficulty.Hard) ? MUCHEOL_HARD_PHASE_TWO_PATTERN_COUNT : MUCHEOL_EASY_PHASE_TWO_PATTERN_COUNT;
        }

        // 기대 개수가 정해진 조합만 값을 돌려주고 나머지는 NO_EXPECTED_COUNT다. 이지와 다른 보스는 기존 검사 그대로다
        private static int GetWolbaekHardPatternCount(string bossId, int phase, EBossDifficulty difficulty)
        {
            if ((bossId != WOLBAEK_ID) || (difficulty != EBossDifficulty.Hard))
            {
                return NO_EXPECTED_COUNT;
            }

            if (phase == FIRST_PHASE)
            {
                return WOLBAEK_HARD_PHASE_ONE_PATTERN_COUNT;
            }

            return (phase == SECOND_PHASE) ? WOLBAEK_HARD_PHASE_TWO_PATTERN_COUNT : NO_EXPECTED_COUNT;
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

                // 6개는 이지 기획 기준이다. 하드 패턴을 세면 하드를 추가하는 순간 이지 구성이 깨진 것으로 보인다
                if ((pattern.BossId == WOLBAEK_ID) && (pattern.Phase == FIRST_PHASE) && (pattern.Difficulty == EBossDifficulty.Easy))
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
            ValidateHardPhaseTwoArea(errors, field, pattern);
            ValidateHardPhaseTwoBarrage(errors, field, pattern);
            ValidateHardPhaseTwoBurst(errors, field, pattern);
            ValidateHardPhaseTwoWarp(errors, field, pattern);
            ValidateBurst(errors, field, pattern);
            ValidateSweep(errors, field, pattern);
            ValidateBeam(errors, field, pattern);
            ValidateStar(errors, field, pattern);
            ValidateWarp(errors, field, pattern);
            ValidateGrid(errors, field, pattern);
            ValidateChain(errors, field, pattern);
        }

        // 잔류 부채꼴 연쇄는 구역 연쇄(SweepZoneCount)의 확장이라 구역 수와 간격은 그 값을 쓴다. 구역 전체 각도는 360도 미만이어야 피난 방향이 남는다
        // 마지막 폭발이 실행 구간의 끝 경계에 놓여야 하므로 실행 시간은 첫 폭발부터 마지막 폭발까지와 같다
        // 잔류 시간은 회복 시간 안에 끝나야 한다. 패턴은 회복이 끝나면 정리되므로 더 길면 잔류 위험이 설계보다 일찍 사라진다
        // 예고 시간 안에 피난 방향 공개가 끝나야 바닥 예고가 남는다
        private void ValidateChain(List<string> errors, string field, PatternDefinition pattern)
        {
            DataValidation.RequireNonNegative(errors, OWNER, field + ".ChainLingerSeconds", pattern.ChainLingerSeconds);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".ChainSafeRevealSeconds", pattern.ChainSafeRevealSeconds);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".ChainHitCooldownSeconds", pattern.ChainHitCooldownSeconds);
            DataValidation.RequireRange(errors, OWNER, field + ".ChainMaxHitsPerTarget", pattern.ChainMaxHitsPerTarget, 0, MAX_CHAIN_HITS);

            bool hasAny = (pattern.ChainLingerSeconds > UNSET) || (pattern.ChainSafeRevealSeconds > UNSET)
                || (pattern.ChainHitCooldownSeconds > UNSET) || (pattern.ChainMaxHitsPerTarget > 0);
            if (!hasAny)
            {
                return;
            }

            if (pattern.RawSweepZoneCount <= 0)
            {
                errors.Add($"{OWNER}.{field}.SweepZoneCount: 잔류 연쇄 값이 있으면 구역 수가 필요하다");
                return;
            }

            if ((pattern.RawBeamCount > 0) || (pattern.StarVertexCount > 0) || (pattern.RawGridLineCount > 0) || (pattern.WarpCandidateCount > 0))
            {
                errors.Add($"{OWNER}.{field}.ChainLingerSeconds: 잔류 연쇄는 빔, 별형, 격자, 워프 값과 섞을 수 없다");
            }

            if ((pattern.Shape != null) && ((pattern.Shape.ArcAngle <= UNSET) || (pattern.Shape.ArcAngle >= MAX_ARC_ANGLE)))
            {
                errors.Add($"{OWNER}.{field}.Shape.ArcAngle = {pattern.Shape.ArcAngle}: 0 초과 {MAX_ARC_ANGLE} 미만이어야 피난 방향이 남는다");
            }

            if (pattern.ChainLingerSeconds <= UNSET)
            {
                errors.Add($"{OWNER}.{field}.ChainLingerSeconds: 잔류 시간이 필요하다");
            }
            else if (pattern.ChainLingerSeconds > (pattern.RecoverySeconds + SEQUENCE_EPSILON))
            {
                errors.Add($"{OWNER}.{field}.ChainLingerSeconds = {pattern.ChainLingerSeconds}: 회복 시간 {pattern.RecoverySeconds}초 이하여야 한다");
            }

            if ((pattern.ChainSafeRevealSeconds <= UNSET) || (pattern.ChainSafeRevealSeconds >= pattern.TelegraphSeconds))
            {
                errors.Add($"{OWNER}.{field}.ChainSafeRevealSeconds = {pattern.ChainSafeRevealSeconds}: 0 초과 예고 시간 {pattern.TelegraphSeconds}초 미만이어야 한다");
            }

            if (pattern.ChainHitCooldownSeconds <= UNSET)
            {
                errors.Add($"{OWNER}.{field}.ChainHitCooldownSeconds: 재피격 간격이 필요하다");
            }

            if (pattern.ChainMaxHitsPerTarget < 1)
            {
                errors.Add($"{OWNER}.{field}.ChainMaxHitsPerTarget: 최대 피격 횟수가 필요하다");
            }

            if ((pattern.RawSweepZoneCount > 1) && (pattern.SweepIntervalSeconds > UNSET))
            {
                float sequence = (pattern.RawSweepZoneCount - 1) * pattern.SweepIntervalSeconds;
                if (Mathf.Abs(pattern.ActiveSeconds - sequence) > SEQUENCE_EPSILON)
                {
                    errors.Add($"{OWNER}.{field}.ActiveSeconds = {pattern.ActiveSeconds}: 첫 폭발부터 마지막 폭발까지 {sequence}초와 같아야 한다");
                }
            }
        }

        // 격자 순차 빔은 선 형태여야 하고, 줄 폭이 줄 간격 이상이어야 줄 사이에 틈이 없으며 줄 전체가 전장 지름을 덮어야 한다
        // 실행 시간은 첫 타격부터 마지막 타격까지와 정확히 같아야 한다. 더 길면 마지막 타격 뒤 회복이 설계 시간보다 길어지고, 짧으면 마지막 줄이 실행 밖으로 밀린다
        // 가로와 세로 두 방향이 같은 간격으로 이어지므로 첫 타격부터 마지막 타격까지는 2 x (줄 수 - 1) x 타격 간격 + 방향 전환 간격이다
        private void ValidateGrid(List<string> errors, string field, PatternDefinition pattern)
        {
            DataValidation.RequireRange(errors, OWNER, field + ".GridLineCount", pattern.RawGridLineCount, 0, MAX_GRID_LINES);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".GridLineSpacing", pattern.GridLineSpacing);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".GridStrikeIntervalSeconds", pattern.GridStrikeIntervalSeconds);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".GridAxisGapSeconds", pattern.GridAxisGapSeconds);

            if (pattern.RawGridLineCount <= 0)
            {
                if ((pattern.GridLineSpacing > UNSET) || (pattern.GridStrikeIntervalSeconds > UNSET) || (pattern.GridAxisGapSeconds > UNSET))
                {
                    errors.Add($"{OWNER}.{field}.GridLineCount: 격자 값이 있으면 줄 수가 필요하다");
                }

                return;
            }

            if ((pattern.Shape != null) && (pattern.Shape.Shape != EEnemyAttackShape.Line))
            {
                errors.Add($"{OWNER}.{field}.Shape: 격자 빔은 선 형태여야 한다");
            }

            if ((pattern.RawBeamCount > 0) || (pattern.StarVertexCount > 0) || (pattern.RawSweepZoneCount > 0) || (pattern.BeamTickInterval > UNSET))
            {
                errors.Add($"{OWNER}.{field}.GridLineCount: 격자 빔은 평행 빔, 별형, 연쇄 값과 섞을 수 없다");
            }

            if (pattern.GridLineSpacing <= UNSET)
            {
                errors.Add($"{OWNER}.{field}.GridLineSpacing: 줄 간격이 필요하다");
            }
            else if ((pattern.Shape != null) && (pattern.Shape.Width < (pattern.GridLineSpacing - GRID_FIT_EPSILON)))
            {
                errors.Add($"{OWNER}.{field}.Shape.Width = {pattern.Shape.Width}: 줄 간격 {pattern.GridLineSpacing}m 이상이어야 줄 사이에 틈이 없다");
            }

            if ((pattern.RawGridLineCount > 1) && (pattern.GridStrikeIntervalSeconds <= UNSET))
            {
                errors.Add($"{OWNER}.{field}.GridStrikeIntervalSeconds: 여러 줄이면 타격 간격이 필요하다");
            }

            float sequence = (GRID_AXIS_COUNT * (pattern.RawGridLineCount - 1) * pattern.GridStrikeIntervalSeconds) + pattern.GridAxisGapSeconds;
            if (Mathf.Abs(pattern.ActiveSeconds - sequence) > SEQUENCE_EPSILON)
            {
                errors.Add($"{OWNER}.{field}.ActiveSeconds = {pattern.ActiveSeconds}: 첫 타격부터 마지막 타격까지 {sequence}초와 같아야 한다");
            }

            if (TryGetBoss(pattern.BossId, out BossDefinition boss) && (boss.ArenaDiameter > UNSET) && (pattern.GridLineSpacing > UNSET)
                && ((pattern.RawGridLineCount * pattern.GridLineSpacing) + GRID_FIT_EPSILON < boss.ArenaDiameter))
            {
                errors.Add($"{OWNER}.{field}.GridLineCount = {pattern.RawGridLineCount}: 줄 전체 {pattern.RawGridLineCount * pattern.GridLineSpacing}m가 전장 지름 {boss.ArenaDiameter}m를 덮어야 한다");
            }
        }

        // 워프 후 충격파는 원형 형태여야 하고, 후보 수와 후보 반경이 있어야 한다. 후보 반경은 전장 반지름보다 작아야 후보가 전장 안에 들 수 있다
        private void ValidateWarp(List<string> errors, string field, PatternDefinition pattern)
        {
            DataValidation.RequireRange(errors, OWNER, field + ".WarpCandidateCount", pattern.WarpCandidateCount, 0, MAX_WARP_CANDIDATES);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".WarpMinTargetDistance", pattern.WarpMinTargetDistance);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".WarpLandingDistance", pattern.WarpLandingDistance);

            if (pattern.WarpCandidateCount <= 0)
            {
                if ((pattern.WarpMinTargetDistance > UNSET) || (pattern.WarpLandingDistance > UNSET))
                {
                    errors.Add($"{OWNER}.{field}.WarpCandidateCount: 워프 거리 값이 있으면 후보 수가 필요하다");
                }

                return;
            }

            if ((pattern.Shape != null) && (pattern.Shape.Shape != EEnemyAttackShape.Circle))
            {
                errors.Add($"{OWNER}.{field}.Shape: 워프 충격파는 원형 형태여야 한다");
            }

            if (pattern.WarpLandingDistance <= UNSET)
            {
                errors.Add($"{OWNER}.{field}.WarpLandingDistance: 착지 후보 반경이 필요하다");
            }
            else if (TryGetBoss(pattern.BossId, out BossDefinition boss) && (boss.ArenaDiameter > UNSET)
                && (pattern.WarpLandingDistance >= (boss.ArenaDiameter * 0.5f)))
            {
                errors.Add($"{OWNER}.{field}.WarpLandingDistance = {pattern.WarpLandingDistance}: 전장 반지름 {boss.ArenaDiameter * 0.5f}m 미만이어야 한다");
            }
        }

        // 별형 회전 빔은 선 형태여야 하고, 꼭짓점 수와 연결 칸 수와 배치 반경과 회전 속도와 피해 간격이 있어야 한다
        // 연결 칸 수가 꼭짓점 수와 서로소가 아니면 닫힌 별이 되지 않고, 1이면 별이 아니라 다각형이다. 배치 반경은 전장 반지름 이하여야 빔이 전장 밖으로 나가지 않는다
        private void ValidateStar(List<string> errors, string field, PatternDefinition pattern)
        {
            DataValidation.RequireRange(errors, OWNER, field + ".StarVertexCount", pattern.StarVertexCount, 0, MAX_STAR_VERTICES);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".StarRingRadius", pattern.StarRingRadius);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".StarRotationDegrees", pattern.StarRotationDegrees);
            DataValidation.RequireRange(errors, OWNER, field + ".StarStageCount", pattern.RawStarStageCount, 0, MAX_STAR_STAGES);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".StarStageDegrees", pattern.StarStageDegrees);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".StarStageIntervalSeconds", pattern.StarStageIntervalSeconds);

            if (pattern.StarVertexCount <= 0)
            {
                if ((pattern.RawStarStageCount > 0) || (pattern.StarStageDegrees > UNSET) || (pattern.StarStageIntervalSeconds > UNSET))
                {
                    errors.Add($"{OWNER}.{field}.StarVertexCount: 단계 값이 있으면 꼭짓점 수가 필요하다");
                }

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

            if (pattern.RawStarStageCount > 0)
            {
                ValidateStarStages(errors, field, pattern);
                return;
            }

            if ((pattern.StarStageDegrees > UNSET) || (pattern.StarStageIntervalSeconds > UNSET))
            {
                errors.Add($"{OWNER}.{field}.StarStageCount: 단계 각도나 간격이 있으면 단계 수가 필요하다");
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

        // 단계형은 회전 속도와 피해 간격을 쓰지 않으므로 값이 남아 있으면 회전형과 섞인 것으로 보고 막는다
        // 실행 시간이 첫 단계부터 마지막 단계까지를 덮어야 마지막 단계 타격이 실행 구간 밖으로 밀리지 않는다
        private static void ValidateStarStages(List<string> errors, string field, PatternDefinition pattern)
        {
            if ((pattern.StarStageDegrees <= UNSET) || (pattern.StarStageDegrees > MAX_ANGLE))
            {
                errors.Add($"{OWNER}.{field}.StarStageDegrees = {pattern.StarStageDegrees}: 0 초과 {MAX_ANGLE} 이하여야 한다");
            }

            if ((pattern.StarRotationDegrees > UNSET) || (pattern.BeamTickInterval > UNSET))
            {
                errors.Add($"{OWNER}.{field}.StarRotationDegrees: 단계형은 회전 속도와 피해 간격을 쓰지 않는다");
            }

            if (pattern.RawStarStageCount == 1)
            {
                return;
            }

            if (pattern.StarStageIntervalSeconds <= UNSET)
            {
                errors.Add($"{OWNER}.{field}.StarStageIntervalSeconds: 여러 단계면 간격이 필요하다");
                return;
            }

            float sequence = (pattern.RawStarStageCount - 1) * pattern.StarStageIntervalSeconds;
            if (pattern.ActiveSeconds < (sequence - SEQUENCE_EPSILON))
            {
                errors.Add($"{OWNER}.{field}.ActiveSeconds = {pattern.ActiveSeconds}: 첫 단계부터 마지막 단계까지 {sequence}초를 덮어야 한다");
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

        private static void ValidateHardPhaseTwoArea(List<string> errors, string field, PatternDefinition pattern)
        {
            if (pattern.DefinitionId != WOLBAEK_HARD_P2_P04_ID)
            {
                return;
            }

            if ((pattern.Shape == null) || (pattern.Shape.Shape != EEnemyAttackShape.TargetArea)
                || (pattern.Difficulty != EBossDifficulty.Hard) || (pattern.Phase != SECOND_PHASE)
                || (pattern.TelegraphSeconds != HARD_AREA_TELEGRAPH_SECONDS) || (pattern.TrackingSeconds != HARD_AREA_TRACKING_SECONDS)
                || (pattern.ActiveSeconds != HARD_AREA_FIXED_SECONDS) || (pattern.RecoverySeconds != HARD_AREA_RECOVERY_SECONDS)
                || (pattern.Shape.Range != HARD_AREA_RADIUS) || (pattern.AreaDurationSeconds != HARD_AREA_DURATION_SECONDS)
                || (pattern.AreaTickInterval != HARD_AREA_TICK_SECONDS) || (pattern.DamageRef != HARD_AREA_DAMAGE_REF)
                || pattern.Guardable || (pattern.RawAreaTargetCount != HARD_AREA_TARGET_COUNT)
                || (pattern.MaxConcurrentAreas != HARD_AREA_TARGET_COUNT)
                || (!pattern.AreaSkipsMissedRounds))
            {
                errors.Add($"{OWNER}.{field}: 하드 P04의 추적 4초, 예고 8초, 실행 2.5초, 회복 2초, 장판 2개와 60회 피해 계약이 맞지 않는다");
            }
        }

        // 난사의 구조만 고정한다. 구역 수가 되는 발 수, 대상당 피격 횟수, 조준 추적 없음, 방어 가능은 서버 패턴이 기대는 계약이라 어긋나면 막는다
        // 시간, 사거리, 폭, 속도, 피해량은 밸런스 값이라 에셋에서 조정한다. 다만 각 발의 예고가 발사 예고 시간만큼 앞서므로 예고 시간은 양수여야 한다
        private static void ValidateHardPhaseTwoBarrage(List<string> errors, string field, PatternDefinition pattern)
        {
            if (pattern.DefinitionId != WOLBAEK_HARD_P2_P05_ID)
            {
                return;
            }

            if ((pattern.Shape == null) || (pattern.Shape.Shape != EEnemyAttackShape.Projectile)
                || (pattern.Difficulty != EBossDifficulty.Hard) || (pattern.Phase != SECOND_PHASE)
                || (pattern.RawProjectileCount != BARRAGE_SHOTS) || (pattern.ProjectileMaxHitsPerTarget != BARRAGE_MAX_HITS)
                || (pattern.TrackingSeconds > UNSET) || (pattern.TurnRate > UNSET) || (!pattern.Guardable)
                || (pattern.TelegraphSeconds <= UNSET) || (pattern.ProjectileIntervalSeconds <= UNSET))
            {
                errors.Add($"{OWNER}.{field}: 하드 P05의 8구역 8발, 대상당 2회 피격, 추적 없는 예고와 방어 가능 계약이 맞지 않는다");
            }
        }

        // 동시 폭발의 구조만 고정한다. 원형 형태, 즉발(실행 시간 0), 방어 가능, 사분면 원의 반경과 좌표가 있어야 서버 패턴이 13개 원을 만들 수 있다
        // 시간, 반경, 좌표 크기, 피해량은 밸런스 값이라 에셋에서 조정한다
        private static void ValidateHardPhaseTwoBurst(List<string> errors, string field, PatternDefinition pattern)
        {
            if (pattern.DefinitionId != WOLBAEK_HARD_P2_P06_ID)
            {
                return;
            }

            if ((pattern.Shape == null) || (pattern.Shape.Shape != EEnemyAttackShape.Circle)
                || (pattern.Difficulty != EBossDifficulty.Hard) || (pattern.Phase != SECOND_PHASE)
                || (!pattern.Guardable) || (pattern.ActiveSeconds > UNSET) || (pattern.TelegraphSeconds <= UNSET)
                || (pattern.BurstSpotRadius <= UNSET) || (pattern.BurstNearOffset <= UNSET)
                || (pattern.BurstFarOffset <= pattern.BurstNearOffset))
            {
                errors.Add($"{OWNER}.{field}: 하드 P06의 즉발 판정, 방어 가능, 사분면 고정 원 계약이 맞지 않는다");
            }
        }

        // 워프 충격파의 구조만 고정한다. 원형 형태, 즉발(실행 시간 0), 방어 가능, 후보 수와 착지 거리와 선택 최소 거리가 있어야 서버 패턴이 착지 후보를 검사할 수 있다
        // 시간, 반경, 피해량은 밸런스 값이라 에셋에서 조정한다
        private static void ValidateHardPhaseTwoWarp(List<string> errors, string field, PatternDefinition pattern)
        {
            if (pattern.DefinitionId != WOLBAEK_HARD_P2_P07_ID)
            {
                return;
            }

            if ((pattern.Shape == null) || (pattern.Shape.Shape != EEnemyAttackShape.Circle)
                || (pattern.Difficulty != EBossDifficulty.Hard) || (pattern.Phase != SECOND_PHASE)
                || (!pattern.Guardable) || (pattern.ActiveSeconds > UNSET) || (pattern.TelegraphSeconds <= UNSET)
                || (pattern.WarpCandidateCount <= 0) || (pattern.WarpLandingDistance <= UNSET)
                || (pattern.WarpMinTargetDistance <= UNSET))
            {
                errors.Add($"{OWNER}.{field}: 하드 P07의 즉발 원형 충격파, 방어 가능, 후방 우선 착지 후보 계약이 맞지 않는다");
            }
        }

        // 동시 원형 폭발은 원형 형태여야 하고, 사분면 원의 반경과 안쪽, 바깥쪽 좌표가 함께 있어야 한다. 바깥쪽이 안쪽보다 커야 한 사분면의 세 원이 서로 다른 곳에 놓인다
        // 가장 먼 원(바깥쪽 좌표와 안쪽 좌표의 대각 거리에 반경을 더한 값)이 전장 반지름 안이어야 예고가 전장 밖으로 나가지 않는다
        private void ValidateBurst(List<string> errors, string field, PatternDefinition pattern)
        {
            DataValidation.RequireNonNegative(errors, OWNER, field + ".BurstSpotRadius", pattern.BurstSpotRadius);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".BurstNearOffset", pattern.BurstNearOffset);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".BurstFarOffset", pattern.BurstFarOffset);

            bool hasAny = (pattern.BurstSpotRadius > UNSET) || (pattern.BurstNearOffset > UNSET) || (pattern.BurstFarOffset > UNSET);
            if (!hasAny)
            {
                return;
            }

            if ((pattern.Shape != null) && (pattern.Shape.Shape != EEnemyAttackShape.Circle))
            {
                errors.Add($"{OWNER}.{field}.Shape: 동시 원형 폭발은 원형 형태여야 한다");
            }

            if ((pattern.BurstSpotRadius <= UNSET) || (pattern.BurstNearOffset <= UNSET) || (pattern.BurstFarOffset <= UNSET))
            {
                errors.Add($"{OWNER}.{field}.BurstSpotRadius: 사분면 원의 반경과 안쪽, 바깥쪽 좌표가 모두 필요하다");
                return;
            }

            if (pattern.BurstFarOffset <= pattern.BurstNearOffset)
            {
                errors.Add($"{OWNER}.{field}.BurstFarOffset = {pattern.BurstFarOffset}: 안쪽 좌표 {pattern.BurstNearOffset}m보다 커야 한다");
            }

            if ((pattern.RawBeamCount > 0) || (pattern.StarVertexCount > 0) || (pattern.RawSweepZoneCount > 0) || (pattern.RawGridLineCount > 0)
                || (pattern.WarpCandidateCount > 0) || (pattern.ChainLingerSeconds > UNSET) || (pattern.RawProjectileCount > 0)
                || (pattern.RawAreaTargetCount > 0))
            {
                errors.Add($"{OWNER}.{field}.BurstSpotRadius: 동시 원형 폭발은 빔, 별형, 연쇄, 격자, 워프, 투사체, 장판 값과 섞을 수 없다");
            }

            if (TryGetBoss(pattern.BossId, out BossDefinition boss) && (boss.ArenaDiameter > UNSET))
            {
                float reach = Mathf.Sqrt((pattern.BurstFarOffset * pattern.BurstFarOffset) + (pattern.BurstNearOffset * pattern.BurstNearOffset))
                    + pattern.BurstSpotRadius;
                if (reach > ((boss.ArenaDiameter * RADIUS_PER_DIAMETER) + BURST_FIT_EPSILON))
                {
                    errors.Add($"{OWNER}.{field}.BurstFarOffset = {pattern.BurstFarOffset}: 가장 먼 원이 {reach}m까지 닿아 전장 반지름 {boss.ArenaDiameter * RADIUS_PER_DIAMETER}m를 넘는다");
                }
            }
        }

        // 한 발이면(0 또는 1) 간격은 쓰지 않는다. 여러 발이면 간격이 있어야 하고, 실행 구간이 첫 발부터 마지막 발까지를 덮어야 마지막 발이 실행 밖으로 밀리지 않는다
        private static void ValidateProjectileSequence(List<string> errors, string field, PatternDefinition pattern)
        {
            DataValidation.RequireRange(errors, OWNER, field + ".ProjectileCount", pattern.RawProjectileCount, 0, MAX_PROJECTILE_COUNT);
            DataValidation.RequireNonNegative(errors, OWNER, field + ".ProjectileIntervalSeconds", pattern.ProjectileIntervalSeconds);
            DataValidation.RequireRange(errors, OWNER, field + ".ProjectileMaxHitsPerTarget", pattern.ProjectileMaxHitsPerTarget, 0, MAX_PROJECTILE_HITS);

            if ((pattern.ProjectileMaxHitsPerTarget > 0) && (pattern.Shape != null) && (pattern.Shape.Shape != EEnemyAttackShape.Projectile))
            {
                errors.Add($"{OWNER}.{field}.ProjectileMaxHitsPerTarget: 대상당 피격 상한은 투사체 형태에만 쓴다");
            }

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
