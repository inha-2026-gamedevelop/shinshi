// System
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Data;

using BossDefinition    = Shinshi.Data.BossPatternCatalogSO.BossDefinition;
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 개발 시험 장면 전용 카탈로그다. 운영 BossPatternCatalog 에셋은 무철을 비활성으로 두고 있어(수치와 패턴 없음) 시험 장면이 그것을 읽으면
    // 전투가 열리지 않는다. 그래서 무철 시험 구동기가 이 객체를 두뇌에 넣는다. 운영 에셋과 GameDB는 읽지도 쓰지도 않는다.
    // 아래 수치는 모두 플레이 테스트 전 임시 시작값이며 밸런스 확정값이 아니다. 방어 가능 여부(Guardable)도 임시 가정이다.
    // P01~P05는 정의의 거리 배율이 맞는 거리 구간에서만 고르고, 구간 밖이라 가중치가 0이 되는 일이 없게 최근 사용 배율은 0보다 크게 둔다
    /// <summary> 무철 1페이즈 여섯 패턴의 시험용 임시 수치를 가진 카탈로그, 시험 장면 전용 </summary>
    public sealed class MucheolDevCatalog : IBossPatternCatalog
    {
        /****************************************
        *              Constants
        ****************************************/

        // 보스 정의, 거리 구간과 방향 구간 기준은 월백 시험값과 같은 모양이다. 전환 비율 0은 2페이즈가 없다는 뜻이다
        private const float HEALTH_REF         = 700f;
        private const float DEFENSE            = 200f;
        private const float PHASE_RATE_NONE    = 0f;
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

        // 거리 구간 배율은 근, 중, 원 순서다
        private static readonly float[] Neutral3     = { 1f, 1f, 1f };
        private static readonly float[] NearOnly     = { 1f, 0f, 0f };
        private static readonly float[] MidOnly      = { 0f, 1f, 0f };
        private static readonly float[] MidAndFar    = { 0f, 1f, 1f };
        private static readonly float[] RecentSoft   = { 0.5f, 0.8f, 1f, 1f };

        /****************************************
        *                Fields
        ****************************************/

        private readonly BossDefinition          _boss;
        private readonly List<PatternDefinition> _patterns = new List<PatternDefinition>();

        /****************************************
        *              Properties
        ****************************************/

        public IReadOnlyList<PatternDefinition> Patterns => _patterns;

        /****************************************
        *                Methods
        ****************************************/

        // arenaDiameter는 전장 지름이다. 돌진(P06)이 18m라 경계(지름의 절반에서 몸체 반경을 뺀 값)가 18m보다 멀어야 중심에서 벽 없이 끝까지 간다
        public MucheolDevCatalog(float arenaDiameter)
        {
            _boss = new BossDefinition(MucheolPatternFactory.BOSS_ID, arenaDiameter, HEALTH_REF, DEFENSE, PHASE_RATE_NONE,
                OPENING_DELAY, NEAR_DISTANCE_MAX, MID_DISTANCE_MAX, FRONT_ANGLE_MAX, SIDE_ANGLE_MAX, NEARBY_RADIUS, EMPTY_RETRY,
                CONTROL_MAX, CONTROL_IMMUNE);

            // P01 팔 휘두르기, 부채꼴 5m 120도
            Add(MucheolPatternFactory.P01_ID, 30f, NearOnly, 0.7f, 0.35f, 180f, 0f, 0.6f,
                EnemyCatalogSO.AttackShapeSpec.Fan(5f, 120f), 0.20f, true);

            // P02 양손 내려찍기, 부채꼴 5m 70도
            Add(MucheolPatternFactory.P02_ID, 25f, NearOnly, 1.2f, 0.6f, 120f, 0f, 1.2f,
                EnemyCatalogSO.AttackShapeSpec.Fan(5f, 70f), 0.32f, true);

            // P03 어깨 돌진, 8m 폭 2.5m
            Add(MucheolPatternFactory.P03_ID, 15f, MidOnly, 0.9f, 0.4f, 150f, 0.5f, 0.8f,
                EnemyCatalogSO.AttackShapeSpec.Dash(8f, 2.5f), 0.22f, true);

            // P04 발구르기 충격파, 원형 5m
            Add(MucheolPatternFactory.P04_ID, 10f, NearOnly, 1.0f, 0f, 0f, 0f, 0.8f,
                EnemyCatalogSO.AttackShapeSpec.Circle(5f), 0.26f, true);

            // P05 2연타, 타별 형태와 피해는 콤보 데이터가 정한다. 이 정의의 형태와 피해는 쓰이지 않는다
            Add(MucheolPatternFactory.P05_ID, 10f, NearOnly, 0.8f, 0.4f, 150f, 0.5f, 1.2f,
                EnemyCatalogSO.AttackShapeSpec.Fan(5f, 120f), 0.14f, true);

            // P06 뿔 들이받기, 18m 폭 3m 방어 불가
            Add(MucheolPatternFactory.P06_ID, 10f, MidAndFar, 1.6f, 0.6f, 90f, 0.75f, 1.5f,
                EnemyCatalogSO.AttackShapeSpec.Dash(18f, 3f), 0.40f, false);
        }

        public bool TryGetBoss(string bossId, out BossDefinition boss)
        {
            boss = (bossId == MucheolPatternFactory.BOSS_ID) ? _boss : null;
            return boss != null;
        }

        public void CollectPatterns(string bossId, int phase, List<PatternDefinition> results)
        {
            results.Clear();
            if ((bossId != MucheolPatternFactory.BOSS_ID) || (phase != FIRST_PHASE))
            {
                return;
            }

            results.AddRange(_patterns);
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

            return null;
        }

        private void Add(string id, float weight, float[] distance, float telegraph, float tracking, float turnRate, float active,
            float recovery, EnemyCatalogSO.AttackShapeSpec shape, float damageRef, bool guardable)
        {
            _patterns.Add(new PatternDefinition(id, MucheolPatternFactory.BOSS_ID, FIRST_PHASE, weight, distance, Neutral3, Neutral3,
                RecentSoft, telegraph, tracking, turnRate, active, recovery, shape, damageRef, guardable));
        }
    }
}
