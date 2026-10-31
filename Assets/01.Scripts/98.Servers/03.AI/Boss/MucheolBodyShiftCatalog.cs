// System
using System.Collections.Generic;

namespace Shinshi.AI
{
    // 무철 공격 클립은 발이 땅에 붙은 채 몸이 움직이는 동작이라 서버 위치가 고정이면 모델이 제자리에서 미끄러진다.
    // 서버가 보스를 같은 경로로 옮기도록 클라이언트 몸 이동 표(PF_MucheolBoss에서 기준 발이 움직인 만큼을 반대로 잰 값)를 옮겨 둔다.
    // 표현 상수이고 밸런스 값이 아니다. 클라이언트 표는 표현 어셈블리에 있어 서버 어셈블리가 볼 수 없어 같은 값을 따로 둔다.
    // 키는 클라이언트 모션 키(MucheolMotionCatalog)와 같은 클립 시각과 기준 시각이다. 모션 키가 바뀌면 여기도 같이 바꿔야 한다.
    // 돌진(P03, P06)은 넣지 않는다. 서버 돌진 이동이 이미 보스를 옮기고, 예고 구간 몸 이동(최대 약 0.34m)을 더하면
    // 돌진 시작점과 직선 예고가 시전 위치에서 어긋나기 때문이다
    /// <summary> 무철 타격형 패턴별 몸 이동 경로와 클립 시간 키 </summary>
    public static class MucheolBodyShiftCatalog
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float SAMPLE_STEP = 0.05f; // 초, 경로 표 한 칸의 클립 시간

        // 클립 시간(초), 클라이언트 모션 키와 같다
        private const float SWING_START         = 0f;
        private const float SWING_IMPACT        = 1.78f;
        private const float SWING_END           = 3.3057141f;
        private const float SLAM_START          = 0f;
        private const float SLAM_IMPACT         = 1.15f;
        private const float SLAM_END            = 3.25f;
        private const float STOMP_START         = 0.15f;
        private const float STOMP_IMPACT        = 1.65f;
        private const float STOMP_END           = 3.4f;
        private const float COMBO_START         = 0f;
        private const float COMBO_FIRST         = 0.84f;
        private const float COMBO_SECOND_P1     = 2.4f;
        private const float COMBO_SECOND_P2     = 1.2f;
        private const float COMBO_THIRD_P2      = 2.4f;
        private const float COMBO_END           = 3.3333335f;
        private const float DOUBLE_START        = 0.15f;
        private const float DOUBLE_FIRST        = 0.78f;
        private const float DOUBLE_SECOND_START = 1.2f;
        private const float DOUBLE_SECOND       = 1.65f;
        private const float DOUBLE_END          = 3.1f;

        // 타 확정 시각에서 더하는 꼬리 시간(초), 클라이언트 모션 키와 같다
        private const float NO_OFFSET     = 0f;
        private const float SWING_TAIL    = 0.6f;
        private const float SLAM_TAIL_P1  = 1.2f;
        private const float SLAM_TAIL_P2  = 1.7f;
        private const float SLAM_TAIL_P7  = 1.8f;
        private const float STOMP_TAIL_P1 = 0.8f;
        private const float STOMP_TAIL_P2 = 1.6f;
        private const float COMBO_TAIL_P1 = 1.2f;
        private const float COMBO_TAIL_P2 = 1.5f;
        private const float DOUBLE_TAIL   = 0.7f;

        private const int FIRST_STRIKE  = 0;
        private const int SECOND_STRIKE = 1;
        private const int THIRD_STRIKE  = 2;

        /****************************************
        *                Fields
        ****************************************/

        // 내려찍기(attack_ground02), 왼발을 뒤로 빼는 동작이라 끝까지 왼발 기준이고 끝에서 거의 닫힌다
        private static readonly float[] G_SlamPath =
        {
            -0.02f, 0.02f, -0.09f, 0.09f, -0.14f, 0.17f, -0.18f, 0.28f, -0.24f, 0.45f, -0.32f, 0.70f, -0.37f, 0.97f, -0.37f, 1.18f, -0.35f, 1.34f, -0.34f, 1.47f,
            -0.34f, 1.57f, -0.33f, 1.65f, -0.30f, 1.73f, -0.23f, 1.83f, -0.15f, 1.92f, -0.06f, 2.00f, 0.01f, 2.06f, 0.06f, 2.10f, 0.09f, 2.13f, 0.11f, 2.17f,
            0.10f, 2.15f, 0.02f, 1.95f, -0.05f, 1.75f, -0.08f, 1.60f, -0.09f, 1.46f, -0.08f, 1.37f, -0.06f, 1.32f, -0.05f, 1.30f, -0.05f, 1.31f, -0.05f, 1.34f,
            -0.04f, 1.38f, -0.05f, 1.42f, -0.08f, 1.45f, -0.11f, 1.45f, -0.13f, 1.42f, -0.14f, 1.38f, -0.16f, 1.36f, -0.20f, 1.34f, -0.25f, 1.33f, -0.28f, 1.33f,
            -0.28f, 1.34f, -0.25f, 1.36f, -0.22f, 1.38f, -0.20f, 1.39f, -0.19f, 1.40f, -0.18f, 1.38f, -0.18f, 1.28f, -0.18f, 1.12f, -0.19f, 0.95f, -0.22f, 0.76f,
            -0.26f, 0.57f, -0.28f, 0.37f, -0.29f, 0.19f, -0.28f, 0.05f, -0.27f, -0.07f, -0.25f, -0.16f, -0.22f, -0.20f, -0.17f, -0.21f, -0.12f, -0.18f, -0.06f, -0.13f,
            0.02f, -0.06f, 0.09f, 0.01f, 0.14f, 0.05f, 0.16f, 0.05f, 0.16f, 0.02f, 0.12f, -0.01f, 0.08f, -0.05f
        };

        // 발 구르기(attack_foot_left), 오른발이 클립 내내 지면에 붙어 끝까지 오른발 기준이다. 클립 0.15초 자세가 0점이다
        private static readonly float[] G_StompPath =
        {
            0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, -0.01f, -0.01f, -0.07f, -0.02f, -0.12f, -0.03f, -0.14f, -0.03f, -0.15f, -0.02f, -0.15f, 0.03f, -0.15f,
            0.12f, -0.14f, 0.26f, -0.09f, 0.44f, 0.00f, 0.62f, 0.09f, 0.74f, 0.14f, 0.82f, 0.17f, 0.86f, 0.19f, 0.89f, 0.20f, 0.91f, 0.21f, 0.92f, 0.21f,
            0.92f, 0.21f, 0.93f, 0.21f, 0.92f, 0.21f, 0.92f, 0.21f, 0.90f, 0.21f, 0.88f, 0.22f, 0.85f, 0.22f, 0.81f, 0.22f, 0.74f, 0.23f, 0.65f, 0.24f,
            0.54f, 0.27f, 0.40f, 0.35f, 0.25f, 0.55f, 0.15f, 0.82f, 0.11f, 1.05f, 0.11f, 1.14f, 0.09f, 1.11f, 0.07f, 1.04f, 0.05f, 0.99f, 0.05f, 0.95f,
            0.06f, 0.91f, 0.07f, 0.88f, 0.06f, 0.85f, 0.02f, 0.84f, -0.05f, 0.85f, -0.14f, 0.86f, -0.25f, 0.88f, -0.34f, 0.90f, -0.42f, 0.92f, -0.48f, 0.93f,
            -0.51f, 0.93f, -0.52f, 0.92f, -0.51f, 0.89f, -0.48f, 0.86f, -0.43f, 0.83f, -0.37f, 0.80f, -0.30f, 0.76f, -0.23f, 0.72f, -0.16f, 0.68f, -0.11f, 0.62f,
            -0.08f, 0.54f, -0.06f, 0.46f, -0.04f, 0.39f, -0.03f, 0.32f, -0.02f, 0.28f, 0.00f, 0.25f, 0.02f, 0.23f, 0.03f, 0.23f, 0.05f, 0.22f, 0.06f, 0.22f
        };

        // 2페이즈 연속 팔 휘두르기(attack04), 1타는 왼발이고 클립 1.4초부터 오른발 기준이다. 클립이 0.78초에서 1.2초로 건너뛰는 동안
        // 이동을 유지하도록 0.8~1.2초 값이 같고, 끝에서 닫히지 않는다
        private static readonly float[] G_DoublePath =
        {
            0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, -0.01f, 0.00f, -0.03f, 0.04f, -0.07f, 0.11f, -0.12f, 0.25f, -0.17f, 0.41f, -0.23f, 0.58f,
            -0.29f, 0.74f, -0.35f, 0.90f, -0.41f, 1.04f, -0.48f, 1.16f, -0.56f, 1.27f, -0.65f, 1.36f, -0.65f, 1.36f, -0.65f, 1.36f, -0.65f, 1.36f, -0.65f, 1.36f,
            -0.65f, 1.36f, -0.65f, 1.36f, -0.65f, 1.36f, -0.65f, 1.36f, -0.65f, 1.36f, -0.78f, 1.26f, -0.90f, 1.24f, -0.98f, 1.28f, -0.99f, 1.38f, -0.99f, 1.52f,
            -1.05f, 1.65f, -1.16f, 1.76f, -1.32f, 1.82f, -1.47f, 1.88f, -1.55f, 1.91f, -1.56f, 1.93f, -1.54f, 1.94f, -1.51f, 1.95f, -1.48f, 1.96f, -1.44f, 1.98f,
            -1.42f, 2.00f, -1.40f, 2.03f, -1.39f, 2.08f, -1.38f, 2.13f, -1.38f, 2.18f, -1.36f, 2.22f, -1.35f, 2.23f, -1.33f, 2.21f, -1.32f, 2.15f, -1.33f, 2.06f,
            -1.37f, 1.93f, -1.42f, 1.78f, -1.49f, 1.63f, -1.54f, 1.54f, -1.59f, 1.50f, -1.63f, 1.50f, -1.68f, 1.51f, -1.72f, 1.51f, -1.75f, 1.52f, -1.76f, 1.52f,
            -1.74f, 1.52f, -1.71f, 1.51f, -1.67f, 1.51f, -1.63f, 1.51f, -1.61f, 1.51f, -1.60f, 1.51f, -1.60f, 1.51f
        };

        // 팔 휘두르기(attack01), 끝까지 왼발 기준이고 끝에서 닫힌다
        private static readonly float[] G_SwingPath =
        {
            0.00f, 0.00f, 0.01f, 0.00f, 0.03f, 0.00f, 0.04f, 0.00f, 0.04f, -0.02f, 0.04f, -0.06f, 0.04f, -0.13f, 0.04f, -0.16f, 0.02f, -0.13f, 0.00f, -0.05f,
            -0.03f, 0.08f, -0.05f, 0.23f, -0.08f, 0.40f, -0.11f, 0.59f, -0.13f, 0.74f, -0.15f, 0.84f, -0.17f, 0.89f, -0.18f, 0.91f, -0.20f, 0.92f, -0.22f, 0.94f,
            -0.25f, 0.97f, -0.28f, 1.02f, -0.32f, 1.07f, -0.36f, 1.11f, -0.39f, 1.14f, -0.41f, 1.16f, -0.43f, 1.17f, -0.42f, 1.17f, -0.38f, 1.16f, -0.33f, 1.12f,
            -0.26f, 1.07f, -0.18f, 1.00f, -0.11f, 0.94f, -0.06f, 0.87f, -0.03f, 0.80f, 0.01f, 0.76f, 0.04f, 0.75f, 0.09f, 0.76f, 0.15f, 0.75f, 0.21f, 0.74f,
            0.27f, 0.74f, 0.31f, 0.77f, 0.34f, 0.80f, 0.35f, 0.82f, 0.35f, 0.82f, 0.35f, 0.81f, 0.34f, 0.80f, 0.32f, 0.79f, 0.29f, 0.77f, 0.27f, 0.75f,
            0.23f, 0.73f, 0.17f, 0.72f, 0.14f, 0.71f, 0.13f, 0.70f, 0.14f, 0.64f, 0.11f, 0.56f, 0.07f, 0.45f, 0.01f, 0.35f, -0.05f, 0.25f, -0.11f, 0.17f,
            -0.16f, 0.11f, -0.19f, 0.08f, -0.21f, 0.05f, -0.21f, 0.02f, -0.19f, 0.00f, -0.18f, -0.02f, -0.15f, -0.03f, -0.13f, -0.05f, -0.11f, -0.07f, -0.09f, -0.08f,
            -0.06f, -0.08f, -0.04f, -0.07f, -0.02f, -0.05f, -0.01f, -0.03f, 0.00f, -0.02f
        };

        // 연타(attack02), 1타(클립 0.90초 전)는 왼발, 2타부터 오른발 기준이다. 0.90초 이후 값은 1타 끝 이동에 이어 붙였고 끝에서 닫히지 않는다
        private static readonly float[] G_ComboPath =
        {
            -0.05f, 0.03f, -0.16f, 0.11f, -0.29f, 0.26f, -0.36f, 0.44f, -0.39f, 0.62f, -0.39f, 0.80f, -0.39f, 0.97f, -0.38f, 1.14f, -0.37f, 1.30f, -0.35f, 1.46f,
            -0.30f, 1.60f, -0.24f, 1.73f, -0.15f, 1.86f, -0.02f, 1.97f, 0.14f, 2.05f, 0.30f, 2.08f, 0.38f, 2.07f, 0.41f, 2.00f,
            0.40f, 1.85f, 0.44f, 2.12f, 0.45f, 2.34f, 0.43f, 2.52f, 0.38f, 2.65f, 0.35f, 2.75f, 0.33f, 2.80f, 0.32f, 2.82f, 0.32f, 2.84f, 0.31f, 2.86f,
            0.32f, 2.90f, 0.33f, 2.97f, 0.34f, 3.06f, 0.34f, 3.16f, 0.32f, 3.25f, 0.29f, 3.35f, 0.22f, 3.42f, 0.11f, 3.44f, -0.04f, 3.44f, -0.19f, 3.43f,
            -0.25f, 3.42f, -0.23f, 3.41f, -0.18f, 3.39f, -0.15f, 3.33f, -0.15f, 3.21f, -0.14f, 3.04f, -0.13f, 2.86f, -0.13f, 2.73f, -0.15f, 2.63f, -0.15f, 2.55f,
            -0.13f, 2.51f, -0.05f, 2.48f, 0.05f, 2.43f, 0.14f, 2.33f, 0.21f, 2.21f, 0.25f, 2.09f, 0.26f, 1.97f, 0.25f, 1.84f, 0.21f, 1.70f, 0.17f, 1.57f,
            0.14f, 1.49f, 0.13f, 1.51f, 0.16f, 1.57f, 0.22f, 1.64f, 0.28f, 1.68f, 0.32f, 1.70f, 0.33f, 1.71f, 0.32f, 1.70f, 0.31f, 1.69f
        };

        private static readonly Dictionary<string, MucheolBodyShiftSpec> G_Specs = BuildSpecs();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 패턴의 몸 이동, patternId는 하드 접두를 뗀 기본 ID다. 몸 이동이 없는 패턴이면 거짓이다 </summary>
        public static bool TryGet(string patternId, out MucheolBodyShiftSpec spec)
        {
            if (patternId == null)
            {
                spec = null;
                return false;
            }

            return G_Specs.TryGetValue(patternId, out spec);
        }

        private static Dictionary<string, MucheolBodyShiftSpec> BuildSpecs()
        {
            return new Dictionary<string, MucheolBodyShiftSpec>
            {
                { MucheolPatternFactory.P01_ID, new MucheolBodyShiftSpec(G_SwingPath, SAMPLE_STEP,
                    MucheolBodyShiftKey.AtCast(SWING_START),
                    MucheolBodyShiftKey.AtStrike(FIRST_STRIKE, NO_OFFSET, SWING_IMPACT),
                    MucheolBodyShiftKey.AtStrike(FIRST_STRIKE, SWING_TAIL, SWING_END)) },

                { MucheolPatternFactory.P02_ID, Slam(SLAM_TAIL_P1) },

                { MucheolPatternFactory.P04_ID, Stomp(STOMP_TAIL_P1) },

                { MucheolPatternFactory.P05_ID, new MucheolBodyShiftSpec(G_ComboPath, SAMPLE_STEP,
                    MucheolBodyShiftKey.AtCast(COMBO_START),
                    MucheolBodyShiftKey.AtStrike(FIRST_STRIKE, NO_OFFSET, COMBO_FIRST),
                    MucheolBodyShiftKey.AtStrike(SECOND_STRIKE, NO_OFFSET, COMBO_SECOND_P1),
                    MucheolBodyShiftKey.AtStrike(SECOND_STRIKE, COMBO_TAIL_P1, COMBO_END)) },

                // 1타 확정 순간 클립이 0.78초에서 1.2초로 건너뛴다. 같은 시각의 두 키이며 패턴 시간은 흐르지 않는다
                { MucheolPhase2PatternFactory.P01_ID, new MucheolBodyShiftSpec(G_DoublePath, SAMPLE_STEP,
                    MucheolBodyShiftKey.AtCast(DOUBLE_START),
                    MucheolBodyShiftKey.AtStrike(FIRST_STRIKE, NO_OFFSET, DOUBLE_FIRST),
                    MucheolBodyShiftKey.AtStrike(FIRST_STRIKE, NO_OFFSET, DOUBLE_SECOND_START),
                    MucheolBodyShiftKey.AtStrike(SECOND_STRIKE, NO_OFFSET, DOUBLE_SECOND),
                    MucheolBodyShiftKey.AtStrike(SECOND_STRIKE, DOUBLE_TAIL, DOUBLE_END)) },

                { MucheolPhase2PatternFactory.P02_ID, Slam(SLAM_TAIL_P2) },

                { MucheolPhase2PatternFactory.P04_ID, Stomp(STOMP_TAIL_P2) },

                { MucheolPhase2PatternFactory.P05_ID, new MucheolBodyShiftSpec(G_ComboPath, SAMPLE_STEP,
                    MucheolBodyShiftKey.AtCast(COMBO_START),
                    MucheolBodyShiftKey.AtStrike(FIRST_STRIKE, NO_OFFSET, COMBO_FIRST),
                    MucheolBodyShiftKey.AtStrike(SECOND_STRIKE, NO_OFFSET, COMBO_SECOND_P2),
                    MucheolBodyShiftKey.AtStrike(THIRD_STRIKE, NO_OFFSET, COMBO_THIRD_P2),
                    MucheolBodyShiftKey.AtStrike(THIRD_STRIKE, COMBO_TAIL_P2, COMBO_END)) },

                // 하드 전장 공격은 예고 4초 동안 클립 0~1.15초를 느리게 재생한다
                { MucheolPhase2PatternFactory.P07_ID, Slam(SLAM_TAIL_P7) }
            };
        }

        private static MucheolBodyShiftSpec Slam(float tail)
        {
            return new MucheolBodyShiftSpec(G_SlamPath, SAMPLE_STEP,
                MucheolBodyShiftKey.AtCast(SLAM_START),
                MucheolBodyShiftKey.AtStrike(FIRST_STRIKE, NO_OFFSET, SLAM_IMPACT),
                MucheolBodyShiftKey.AtStrike(FIRST_STRIKE, tail, SLAM_END));
        }

        private static MucheolBodyShiftSpec Stomp(float tail)
        {
            return new MucheolBodyShiftSpec(G_StompPath, SAMPLE_STEP,
                MucheolBodyShiftKey.AtCast(STOMP_START),
                MucheolBodyShiftKey.AtStrike(FIRST_STRIKE, NO_OFFSET, STOMP_IMPACT),
                MucheolBodyShiftKey.AtStrike(FIRST_STRIKE, tail, STOMP_END));
        }
    }
}
