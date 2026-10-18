// System
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Data;

using BossDefinition    = Shinshi.Data.BossPatternCatalogSO.BossDefinition;
using EPatternGroup     = Shinshi.Data.BossPatternCatalogSO.EPatternGroup;
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 개발 시험 장면 전용 카탈로그다. 운영 BossPatternCatalog 에셋은 무철을 비활성으로 두고 있어(수치와 패턴 없음) 시험 장면이 그것을 읽으면
    // 전투가 열리지 않는다. 그래서 무철 시험 구동기가 이 객체를 두뇌에 넣는다. 운영 에셋과 GameDB는 읽지도 쓰지도 않는다.
    // 아래 수치는 모두 플레이 테스트 전 임시 시작값이며 밸런스 확정값이 아니다. 방어 가능 여부(Guardable)도 임시 가정이다.
    // P01~P05는 정의의 거리 배율이 맞는 거리 구간에서만 고르고, 구간 밖이라 가중치가 0이 되는 일이 없게 최근 사용 배율은 0보다 크게 둔다.
    // 2페이즈를 켜면(includePhaseTwo) 체력 55%에서 2페이즈로 넘어가고 mucheol_ph2_p01~p06을 같은 방식의 임시 시작값으로 가진다.
    // 2페이즈 선택 가중치, 배율, 흐름 배율, 강화 구간(체력 25% 이하)은 시작안이며 확정된 값이 아니다. 끄면 1페이즈 시험과 같다.
    // 하드 P07(includeHardP07)은 2페이즈 시험 안의 또 다른 시험 스위치다. 꺼 두면 2페이즈 후보와 실행 데이터에 P07이 전혀 없어
    // 기존 2페이즈 시험값이 그대로이고, 켜면 하드 2페이즈 전용 전장 공격 mucheol_ph2_p07이 2페이즈 후보로 섞인다. 수치는 모두 시험 전 임시안이다
    /// <summary> 무철 1페이즈 여섯 패턴(선택하면 2페이즈 여섯 패턴, 하드 P07까지)의 시험용 임시 수치를 가진 카탈로그, 시험 장면 전용 </summary>
    public sealed class MucheolDevCatalog : IBossPatternCatalog
    {
        /****************************************
        *              Constants
        ****************************************/

        // 보스 정의, 거리 구간과 방향 구간 기준은 월백 시험값과 같은 모양이다. 전환 비율 0은 2페이즈가 없다는 뜻이다
        private const float HEALTH_REF         = 700f;
        private const float DEFENSE            = 200f;
        private const float PHASE_RATE_NONE    = 0f;
        private const float PHASE_TWO_RATE     = 0.55f;
        private const float ENHANCED_RATE      = 0.25f;
        private const float APPEARANCE_NONE    = 0f;
        private const float OPENING_DELAY      = 0.5f;
        private const float NEAR_DISTANCE_MAX  = 5f;
        private const float MID_DISTANCE_MAX   = 12f;
        private const float FRONT_ANGLE_MAX    = 60f;
        private const float SIDE_ANGLE_MAX     = 120f;
        private const float NEARBY_RADIUS      = 5f;
        private const float EMPTY_RETRY        = 0.25f;
        private const float CONTROL_MAX        = 1f;
        private const float CONTROL_IMMUNE     = 2f;

        private const int   FIRST_PHASE        = 1;
        private const int   SECOND_PHASE       = 2;

        // 하드 P07 시작안. 반경은 지름 44m 전장에서 가장자리에 선 보스가 반대편까지 덮는 값이고, 각도는 등 쪽 45도 안전지대의 나머지다
        private const float P07_RADIUS         = 44f;
        private const float P07_ARC_ANGLE      = 315f;
        private const float P07_WEIGHT         = 3f;
        private const float P07_ENHANCED       = 3f;
        private const float P07_TELEGRAPH      = 8f;
        private const float P07_RECOVERY       = 3.6f;
        private const float P07_DAMAGE_REF     = 0.30f;
        private const float P07_PUSH_DISTANCE  = 1f;

        // 거리 구간 배율은 근, 중, 원 순서다
        private static readonly float[] Neutral3     = { 1f, 1f, 1f };
        private static readonly float[] NearOnly     = { 1f, 0f, 0f };
        private static readonly float[] MidOnly      = { 0f, 1f, 0f };
        private static readonly float[] MidAndFar    = { 0f, 1f, 1f };
        private static readonly float[] RecentSoft   = { 0.5f, 0.8f, 1f, 1f };

        // 2페이즈 선택 배율 시작안, 거리는 근 중 원, 방향은 전 측 후, 주변은 1명 2명 3명, 최근 사용은 직전부터 네 칸이다
        private static readonly float[] P2Recent       = { 0f, 0.3f, 0.7f, 1f };
        private static readonly float[] P7Recent       = { 0f, 0f, 0f, 1f };
        private static readonly float[] P2RecentDash   = { 0.25f, 0.5f, 0.8f, 1f };
        private static readonly float[] P2NearHigh     = { 1.8f, 0f, 0f };
        private static readonly float[] P2NearMid      = { 1.6f, 0f, 0f };
        private static readonly float[] P2NearLow      = { 1.7f, 0f, 0f };
        private static readonly float[] P2DashDistance = { 0.3f, 1.8f, 1.1f };
        private static readonly float[] P2ChargeDist   = { 0.2f, 1.4f, 1.8f };
        private static readonly float[] P2DirSwing     = { 1.3f, 1.2f, 0.6f };
        private static readonly float[] P2DirSlam      = { 1.6f, 0.8f, 0.3f };
        private static readonly float[] P2DirCombo     = { 1.4f, 1f, 0.4f };
        private static readonly float[] P2DirCharge    = { 1.5f, 1f, 0.6f };
        private static readonly float[] P2NearbySwing  = { 1f, 1.1f, 1.2f };
        private static readonly float[] P2NearbySlam   = { 1f, 1.2f, 1.3f };
        private static readonly float[] P2NearbyDash   = { 1.2f, 1f, 0.8f };
        private static readonly float[] P2NearbyStomp  = { 0.7f, 1.4f, 2f };
        private static readonly float[] P2NearbyCombo  = { 1f, 1f, 1.1f };
        private static readonly float[] P2NearbyCharge = { 1f, 1.1f, 1f };

        // 흐름 배율 시작안, 체력 구간(강화 아님, 강화) x 직전 그룹(Large, Pressure, Basic) x 다음 그룹 순서다
        private static readonly float[] FlowTable =
        {
            0.25f, 1.25f, 1.6f,  1.15f, 0.6f, 1.2f,  1.3f, 1.1f, 1f,
            0.45f, 1.35f, 1.25f, 1.15f, 0.6f, 1.2f,  1.3f, 1.1f, 1f
        };

        /****************************************
        *                Fields
        ****************************************/

        private readonly BossDefinition          _boss;
        private readonly List<PatternDefinition> _patterns         = new List<PatternDefinition>();
        private readonly List<PatternDefinition> _phaseTwoPatterns = new List<PatternDefinition>();
        private readonly bool                    _includePhaseTwo;
        private readonly bool                    _includeHardP07;

        /****************************************
        *              Properties
        ****************************************/

        public IReadOnlyList<PatternDefinition> Patterns => _patterns;

        /// <summary> 하드 P07이 후보에 들어 있는가 </summary>
        public bool IncludesHardP07 => _includeHardP07;

        /****************************************
        *                Methods
        ****************************************/

        public MucheolDevCatalog(float arenaDiameter)
            : this(arenaDiameter, false)
        {
        }

        // arenaDiameter는 전장 지름이다. 돌진(P06)이 18m라 경계(지름의 절반에서 몸체 반경을 뺀 값)가 18m보다 멀어야 중심에서 벽 없이 끝까지 간다
        // includePhaseTwo가 거짓이면 전환 비율 0(2페이즈 없음)으로 1페이즈 시험과 같고, 참이면 체력 55%에서 2페이즈로 넘어간다
        public MucheolDevCatalog(float arenaDiameter, bool includePhaseTwo)
            : this(arenaDiameter, includePhaseTwo, false)
        {
        }

        // includeHardP07은 2페이즈가 켜져 있을 때만 뜻이 있다. 2페이즈가 없으면 P07 후보도 없다
        public MucheolDevCatalog(float arenaDiameter, bool includePhaseTwo, bool includeHardP07)
        {
            _includePhaseTwo = includePhaseTwo;
            _includeHardP07  = includePhaseTwo && includeHardP07;

            _boss = includePhaseTwo
                ? new BossDefinition(MucheolPatternFactory.BOSS_ID, arenaDiameter, HEALTH_REF, DEFENSE, PHASE_TWO_RATE,
                    OPENING_DELAY, NEAR_DISTANCE_MAX, MID_DISTANCE_MAX, FRONT_ANGLE_MAX, SIDE_ANGLE_MAX, NEARBY_RADIUS, EMPTY_RETRY,
                    CONTROL_MAX, CONTROL_IMMUNE, APPEARANCE_NONE, ENHANCED_RATE, FlowTable)
                : new BossDefinition(MucheolPatternFactory.BOSS_ID, arenaDiameter, HEALTH_REF, DEFENSE, PHASE_RATE_NONE,
                    OPENING_DELAY, NEAR_DISTANCE_MAX, MID_DISTANCE_MAX, FRONT_ANGLE_MAX, SIDE_ANGLE_MAX, NEARBY_RADIUS, EMPTY_RETRY,
                    CONTROL_MAX, CONTROL_IMMUNE);

            // P01 팔 휘두르기, 부채꼴 5m 120도
            Add(MucheolPatternFactory.P01_ID, 30f, NearOnly, 1.4f, 0.35f, 180f, 0f, 1.2f,
                EnemyCatalogSO.AttackShapeSpec.Fan(5f, 120f), 0.20f, true);

            // P02 양손 내려찍기, 부채꼴 5m 70도
            Add(MucheolPatternFactory.P02_ID, 25f, NearOnly, 2.4f, 0.6f, 120f, 0f, 2.4f,
                EnemyCatalogSO.AttackShapeSpec.Fan(5f, 70f), 0.32f, true);

            // P03 어깨 돌진, 8m 폭 2.5m
            Add(MucheolPatternFactory.P03_ID, 15f, MidOnly, 1.8f, 0.4f, 150f, 0.5f, 1.6f,
                EnemyCatalogSO.AttackShapeSpec.Dash(8f, 2.5f), 0.22f, true);

            // P04 발구르기 충격파, 원형 5m
            Add(MucheolPatternFactory.P04_ID, 10f, NearOnly, 2.0f, 0f, 0f, 0f, 1.6f,
                EnemyCatalogSO.AttackShapeSpec.Circle(5f), 0.26f, true);

            // P05 2연타, 타별 형태와 피해는 콤보 데이터가 정한다. 이 정의의 형태와 피해는 쓰이지 않는다
            Add(MucheolPatternFactory.P05_ID, 10f, NearOnly, 1.6f, 0.4f, 150f, 0.5f, 2.4f,
                EnemyCatalogSO.AttackShapeSpec.Fan(5f, 120f), 0.14f, true);

            // P06 뿔 들이받기, 18m 폭 3m 방어 불가
            Add(MucheolPatternFactory.P06_ID, 10f, MidAndFar, 3.2f, 0.6f, 90f, 0.75f, 3.0f,
                EnemyCatalogSO.AttackShapeSpec.Dash(18f, 3f), 0.40f, false);

            if (includePhaseTwo)
            {
                AddPhaseTwo();
            }

            if (_includeHardP07)
            {
                AddHardP07();
            }
        }

        public bool TryGetBoss(string bossId, out BossDefinition boss)
        {
            boss = (bossId == MucheolPatternFactory.BOSS_ID) ? _boss : null;
            return boss != null;
        }

        public void CollectPatterns(string bossId, int phase, List<PatternDefinition> results)
        {
            results.Clear();
            if (bossId != MucheolPatternFactory.BOSS_ID)
            {
                return;
            }

            if (phase == FIRST_PHASE)
            {
                results.AddRange(_patterns);
            }
            else if ((phase == SECOND_PHASE) && _includePhaseTwo)
            {
                results.AddRange(_phaseTwoPatterns);
            }
        }

        // 시험용이라 항상 수치가 지정된 것으로 본다
        public bool IsTuned(string bossId)
        {
            return bossId == MucheolPatternFactory.BOSS_ID;
        }

        /// <summary> 정의 ID로 패턴 정의를 찾는다, 없으면 null이다 </summary>
        public PatternDefinition Find(string definitionId)
        {
            for (int i = 0; i < _patterns.Count; ++i)
            {
                if (_patterns[i].DefinitionId == definitionId)
                {
                    return _patterns[i];
                }
            }

            for (int i = 0; i < _phaseTwoPatterns.Count; ++i)
            {
                if (_phaseTwoPatterns[i].DefinitionId == definitionId)
                {
                    return _phaseTwoPatterns[i];
                }
            }

            return null;
        }

        /// <summary> 2페이즈 여섯 패턴의 시험용 실행 데이터, 정의의 실행 시간과 맞아야 해서 이 카탈로그와 함께 둔다. 모든 값이 시작안이다 </summary>
        public static MucheolPhase2Spec CreatePhaseTwoSpec()
        {
            // P01, P05의 좌우 구역은 120도 부채꼴을 60도씩 둘로 나눈 것으로 본다
            var p01 = new MucheolSequenceSpec(
                MucheolSequenceStep.Fan(5f, 60f, -30f, 0.16f, 0f),
                MucheolSequenceStep.Fan(5f, 60f, 30f, 0.16f, 0.45f));

            var p02 = new MucheolSequenceSpec(
                MucheolSequenceStep.Fan(5f, 70f, 0f, 0.30f, 0f),
                MucheolSequenceStep.Line(6f, 3f, 0f, 0.16f, 0.5f));

            var p04 = new MucheolSequenceSpec(
                MucheolSequenceStep.Circle(5f, 0.22f, 0f),
                MucheolSequenceStep.Circle(6f, 0.22f, 0.7f));

            var p05 = new MucheolSequenceSpec(
                MucheolSequenceStep.Fan(5f, 60f, -30f, 0.14f, 0f),
                MucheolSequenceStep.Fan(5f, 60f, 30f, 0.14f, 0.45f),
                MucheolSequenceStep.Fan(5f, 70f, 0f, 0.26f, 1f));

            // P07의 실행 데이터는 후보 여부와 무관하게 항상 준다. 후보 여부는 정의가 있느냐로만 갈리고, 정의가 없는 P07은 선택되지 않는다
            return new MucheolPhase2Spec(p01, p02, new MucheolMultiDashSpec(2, 2, 0.5f, 0.4f), p04, p05,
                new MucheolMultiDashSpec(2, 3, 0.75f, 0.6f), new MucheolBattlefieldSpec(P07_PUSH_DISTANCE));
        }

        // 2페이즈 여섯 패턴. 폭주 돌진(P06)의 실행 시간은 두 번(2.1초)이 아니라 세 번(3.45초)에 맞춘다. 공용 공격 FSM이 실행 시간을
        // 정의 하나로 고정해 횟수별로 줄 수 없어서이며, 두 번 달리는 시전은 남는 구간 동안 서 있는다. FSM 변경이 승인되면 이 값은 2.1초가 된다
        private void AddPhaseTwo()
        {
            // P01 연속 휘두르기, 좌우 구역
            AddTwo(MucheolPhase2PatternFactory.P01_ID, EPatternGroup.Basic, 22f, 18f, P2NearHigh, P2DirSwing, P2NearbySwing, P2Recent,
                1.8f, 0.4f, 150f, 0.45f, 1.4f, EnemyCatalogSO.AttackShapeSpec.Fan(5f, 120f), 0.16f, true);

            // P02 충격 내려찍기, 부채꼴 뒤 전방 충격파
            AddTwo(MucheolPhase2PatternFactory.P02_ID, EPatternGroup.Large, 12f, 14f, P2NearMid, P2DirSlam, P2NearbySlam, P2Recent,
                2.4f, 0.6f, 120f, 0.5f, 2.4f, EnemyCatalogSO.AttackShapeSpec.Fan(5f, 70f), 0.30f, true);

            // P03 연속 어깨 돌진, 8m 두 번
            AddTwo(MucheolPhase2PatternFactory.P03_ID, EPatternGroup.Pressure, 16f, 18f, P2DashDistance, Neutral3, P2NearbyDash, P2RecentDash,
                1.8f, 0.4f, 150f, 1.4f, 1.8f, EnemyCatalogSO.AttackShapeSpec.Dash(8f, 2.5f), 0.22f, true);

            // P04 이중 지면 충격파, 원형 두 번
            AddTwo(MucheolPhase2PatternFactory.P04_ID, EPatternGroup.Pressure, 14f, 16f, P2NearHigh, Neutral3, P2NearbyStomp, P2Recent,
                2.0f, 0f, 0f, 0.7f, 1.8f, EnemyCatalogSO.AttackShapeSpec.Circle(5f), 0.22f, true);

            // P05 폭주 연타, 좌우 구역 뒤 내려찍기
            AddTwo(MucheolPhase2PatternFactory.P05_ID, EPatternGroup.Large, 12f, 16f, P2NearLow, P2DirCombo, P2NearbyCombo, P2Recent,
                1.8f, 0.4f, 150f, 1.0f, 3.0f, EnemyCatalogSO.AttackShapeSpec.Fan(5f, 70f), 0.26f, true);

            // P06 폭주 돌진, 18m 두 번(강화 구간 세 번), 방어 불가
            AddTwo(MucheolPhase2PatternFactory.P06_ID, EPatternGroup.Large, 10f, 14f, P2ChargeDist, P2DirCharge, P2NearbyCharge, P2Recent,
                4.0f, 0.8f, 90f, 3.45f, 3.6f, EnemyCatalogSO.AttackShapeSpec.Dash(18f, 3f), 0.32f, false);
        }

        // P07 전장 공격, 방어 불가 단발. 실행 시간이 없어 예고가 끝나는 순간 타격하고 회복으로 넘어간다.
        // 추적 시간과 회전 속도가 0인 것은 방향을 예고 시작 순간에 고정한다는 뜻이다. 거리, 방향, 주변 인원 배율은 전 구간 1이고
        // 최근 사용 배율이 최근 세 번 안에 쓴 패턴을 다시 고르지 않게 한다
        private void AddHardP07()
        {
            AddTwo(MucheolPhase2PatternFactory.P07_ID, EPatternGroup.Large, P07_WEIGHT, P07_ENHANCED, Neutral3, Neutral3, Neutral3, P7Recent,
                P07_TELEGRAPH, 0f, 0f, 0f, P07_RECOVERY, EnemyCatalogSO.AttackShapeSpec.Fan(P07_RADIUS, P07_ARC_ANGLE), P07_DAMAGE_REF, false);
        }

        private void AddTwo(string id, EPatternGroup group, float weight, float enhancedWeight, float[] distance, float[] direction,
            float[] nearby, float[] recent, float telegraph, float tracking, float turnRate, float active, float recovery,
            EnemyCatalogSO.AttackShapeSpec shape, float damageRef, bool guardable)
        {
            _phaseTwoPatterns.Add(new PatternDefinition(id, MucheolPatternFactory.BOSS_ID, SECOND_PHASE, weight, distance, direction,
                nearby, recent, telegraph, tracking, turnRate, active, recovery, shape, damageRef, guardable,
                group: group, enhancedBaseWeight: enhancedWeight));
        }

        private void Add(string id, float weight, float[] distance, float telegraph, float tracking, float turnRate, float active,
            float recovery, EnemyCatalogSO.AttackShapeSpec shape, float damageRef, bool guardable)
        {
            _patterns.Add(new PatternDefinition(id, MucheolPatternFactory.BOSS_ID, FIRST_PHASE, weight, distance, Neutral3, Neutral3,
                RecentSoft, telegraph, tracking, turnRate, active, recovery, shape, damageRef, guardable));
        }
    }
}
