// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 내려찍기는 왼발을 뒤로 빼는 동작이라 왼발을 지면에 고정하고 그 위로 몸이 나가도록 한다.
    // 값은 PF_MucheolBoss 모델에 클립을 올려 왼발이 클립 시작 자세에서 움직인 만큼을 반대로 잰 경로이고, 보스 기준 오른쪽(x)과 앞(z) 미터이다. 표현 상수이고 밸런스 값이 아니다.
    // 샘플은 0.05초마다 (x, z)를 이어 적은 것이고 앞뒤 2프레임을 평균해 떨림을 줄였다. 샘플 사이는 선형으로 잇고, 끝은 시작 자세로 돌아와 이동이 닫힌다.
    // 다른 패턴의 경로는 아직 확정하지 않아 표에 없다. 서버가 같은 경로로 보스를 옮기게 되면 이 표는 쓰지 않는다
    /// <summary> 왼발을 지면에 고정하는 무철 클립의 몸 이동 경로 </summary>
    public static class MucheolClipBodyShiftCatalog
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float SAMPLE_STEP = 0.05f; // 초, 샘플 한 칸의 클립 시간
        private const int   PAIR_SIZE   = 2;

        /****************************************
        *                Fields
        ****************************************/

        private static readonly Dictionary<string, float[]> G_Paths = new Dictionary<string, float[]>
        {
            { MucheolMotionCatalog.SLAM_CLIP, new[]
                {
                    -0.02f, 0.02f, -0.09f, 0.09f, -0.14f, 0.17f, -0.18f, 0.28f, -0.24f, 0.45f, -0.32f, 0.70f, -0.37f, 0.97f, -0.37f, 1.18f, -0.35f, 1.34f, -0.34f, 1.47f,
                    -0.34f, 1.57f, -0.33f, 1.65f, -0.30f, 1.73f, -0.23f, 1.83f, -0.15f, 1.92f, -0.06f, 2.00f, 0.01f, 2.06f, 0.06f, 2.10f, 0.09f, 2.13f, 0.11f, 2.17f,
                    0.10f, 2.15f, 0.02f, 1.95f, -0.05f, 1.75f, -0.08f, 1.60f, -0.09f, 1.46f, -0.08f, 1.37f, -0.06f, 1.32f, -0.05f, 1.30f, -0.05f, 1.31f, -0.05f, 1.34f,
                    -0.04f, 1.38f, -0.05f, 1.42f, -0.08f, 1.45f, -0.11f, 1.45f, -0.13f, 1.42f, -0.14f, 1.38f, -0.16f, 1.36f, -0.20f, 1.34f, -0.25f, 1.33f, -0.28f, 1.33f,
                    -0.28f, 1.34f, -0.25f, 1.36f, -0.22f, 1.38f, -0.20f, 1.39f, -0.19f, 1.40f, -0.18f, 1.38f, -0.18f, 1.28f, -0.18f, 1.12f, -0.19f, 0.95f, -0.22f, 0.76f,
                    -0.26f, 0.57f, -0.28f, 0.37f, -0.29f, 0.19f, -0.28f, 0.05f, -0.27f, -0.07f, -0.25f, -0.16f, -0.22f, -0.20f, -0.17f, -0.21f, -0.12f, -0.18f, -0.06f, -0.13f,
                    0.02f, -0.06f, 0.09f, 0.01f, 0.14f, 0.05f, 0.16f, 0.05f, 0.16f, 0.02f, 0.12f, -0.01f, 0.08f, -0.05f
                } },

            // 발 구르기는 오른발이 클립 내내 지면에서 거의 떨어지지 않아 처음부터 끝까지 오른발이 주축이다. 클립 0.15초 자세를 기준으로 쟀다
            { MucheolMotionCatalog.STOMP_CLIP, new[]
                {
                    0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, -0.01f, -0.01f, -0.07f, -0.02f, -0.12f, -0.03f, -0.14f, -0.03f, -0.15f, -0.02f, -0.15f, 0.03f, -0.15f,
                    0.12f, -0.14f, 0.26f, -0.09f, 0.44f, 0.00f, 0.62f, 0.09f, 0.74f, 0.14f, 0.82f, 0.17f, 0.86f, 0.19f, 0.89f, 0.20f, 0.91f, 0.21f, 0.92f, 0.21f,
                    0.92f, 0.21f, 0.93f, 0.21f, 0.92f, 0.21f, 0.92f, 0.21f, 0.90f, 0.21f, 0.88f, 0.22f, 0.85f, 0.22f, 0.81f, 0.22f, 0.74f, 0.23f, 0.65f, 0.24f,
                    0.54f, 0.27f, 0.40f, 0.35f, 0.25f, 0.55f, 0.15f, 0.82f, 0.11f, 1.05f, 0.11f, 1.14f, 0.09f, 1.11f, 0.07f, 1.04f, 0.05f, 0.99f, 0.05f, 0.95f,
                    0.06f, 0.91f, 0.07f, 0.88f, 0.06f, 0.85f, 0.02f, 0.84f, -0.05f, 0.85f, -0.14f, 0.86f, -0.25f, 0.88f, -0.34f, 0.90f, -0.42f, 0.92f, -0.48f, 0.93f,
                    -0.51f, 0.93f, -0.52f, 0.92f, -0.51f, 0.89f, -0.48f, 0.86f, -0.43f, 0.83f, -0.37f, 0.80f, -0.30f, 0.76f, -0.23f, 0.72f, -0.16f, 0.68f, -0.11f, 0.62f,
                    -0.08f, 0.54f, -0.06f, 0.46f, -0.04f, 0.39f, -0.03f, 0.32f, -0.02f, 0.28f, 0.00f, 0.25f, 0.02f, 0.23f, 0.03f, 0.23f, 0.05f, 0.22f, 0.06f, 0.22f
                } },

            // 2페이즈 연속 팔 휘두르기는 두 구간이 같은 클립이다. 1타(클립 0.15~0.78초)는 왼발, 둘째 구간이 시작하는 1.2초부터는 새 자세의 왼발을 잇고
            // 오른발이 지면에 내려오는 1.40초부터 오른발이 주축이다. 클립이 0.78초에서 1.2초로 건너뛰는 동안에는 이동을 유지하고, 끝에서 닫히지 않는다
            { MucheolMotionCatalog.DOUBLE_CLIP, new[]
                {
                    0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, -0.01f, 0.00f, -0.03f, 0.04f, -0.07f, 0.11f, -0.12f, 0.25f, -0.17f, 0.41f, -0.23f, 0.58f,
                    -0.29f, 0.74f, -0.35f, 0.90f, -0.41f, 1.04f, -0.48f, 1.16f, -0.56f, 1.27f, -0.65f, 1.36f, -0.65f, 1.36f, -0.65f, 1.36f, -0.65f, 1.36f, -0.65f, 1.36f,
                    -0.65f, 1.36f, -0.65f, 1.36f, -0.65f, 1.36f, -0.65f, 1.36f, -0.65f, 1.36f, -0.78f, 1.26f, -0.90f, 1.24f, -0.98f, 1.28f, -0.99f, 1.38f, -0.99f, 1.52f,
                    -1.05f, 1.65f, -1.16f, 1.76f, -1.32f, 1.82f, -1.47f, 1.88f, -1.55f, 1.91f, -1.56f, 1.93f, -1.54f, 1.94f, -1.51f, 1.95f, -1.48f, 1.96f, -1.44f, 1.98f,
                    -1.42f, 2.00f, -1.40f, 2.03f, -1.39f, 2.08f, -1.38f, 2.13f, -1.38f, 2.18f, -1.36f, 2.22f, -1.35f, 2.23f, -1.33f, 2.21f, -1.32f, 2.15f, -1.33f, 2.06f,
                    -1.37f, 1.93f, -1.42f, 1.78f, -1.49f, 1.63f, -1.54f, 1.54f, -1.59f, 1.50f, -1.63f, 1.50f, -1.68f, 1.51f, -1.72f, 1.51f, -1.75f, 1.52f, -1.76f, 1.52f,
                    -1.74f, 1.52f, -1.71f, 1.51f, -1.67f, 1.51f, -1.63f, 1.51f, -1.61f, 1.51f, -1.60f, 1.51f, -1.60f, 1.51f
                } },

            // 팔 휘두르기도 클립 끝까지 왼발이 주축이고 끝에서 닫힌다
            { MucheolMotionCatalog.SWING_CLIP, new[]
                {
                    0.00f, 0.00f, 0.01f, 0.00f, 0.03f, 0.00f, 0.04f, 0.00f, 0.04f, -0.02f, 0.04f, -0.06f, 0.04f, -0.13f, 0.04f, -0.16f, 0.02f, -0.13f, 0.00f, -0.05f,
                    -0.03f, 0.08f, -0.05f, 0.23f, -0.08f, 0.40f, -0.11f, 0.59f, -0.13f, 0.74f, -0.15f, 0.84f, -0.17f, 0.89f, -0.18f, 0.91f, -0.20f, 0.92f, -0.22f, 0.94f,
                    -0.25f, 0.97f, -0.28f, 1.02f, -0.32f, 1.07f, -0.36f, 1.11f, -0.39f, 1.14f, -0.41f, 1.16f, -0.43f, 1.17f, -0.42f, 1.17f, -0.38f, 1.16f, -0.33f, 1.12f,
                    -0.26f, 1.07f, -0.18f, 1.00f, -0.11f, 0.94f, -0.06f, 0.87f, -0.03f, 0.80f, 0.01f, 0.76f, 0.04f, 0.75f, 0.09f, 0.76f, 0.15f, 0.75f, 0.21f, 0.74f,
                    0.27f, 0.74f, 0.31f, 0.77f, 0.34f, 0.80f, 0.35f, 0.82f, 0.35f, 0.82f, 0.35f, 0.81f, 0.34f, 0.80f, 0.32f, 0.79f, 0.29f, 0.77f, 0.27f, 0.75f,
                    0.23f, 0.73f, 0.17f, 0.72f, 0.14f, 0.71f, 0.13f, 0.70f, 0.14f, 0.64f, 0.11f, 0.56f, 0.07f, 0.45f, 0.01f, 0.35f, -0.05f, 0.25f, -0.11f, 0.17f,
                    -0.16f, 0.11f, -0.19f, 0.08f, -0.21f, 0.05f, -0.21f, 0.02f, -0.19f, 0.00f, -0.18f, -0.02f, -0.15f, -0.03f, -0.13f, -0.05f, -0.11f, -0.07f, -0.09f, -0.08f,
                    -0.06f, -0.08f, -0.04f, -0.07f, -0.02f, -0.05f, -0.01f, -0.03f, 0.00f, -0.02f
                } },

            // 연타는 1타(클립 0.90초 전)는 왼발, 2타 시작(0.90초)부터는 오른발이 주축이다. 0.90초 이후 값은 1타 끝 이동에 이어 붙였고 끝에서 닫히지 않는다
            { MucheolMotionCatalog.COMBO_CLIP, new[]
                {
                    -0.05f, 0.03f, -0.16f, 0.11f, -0.29f, 0.26f, -0.36f, 0.44f, -0.39f, 0.62f, -0.39f, 0.80f, -0.39f, 0.97f, -0.38f, 1.14f, -0.37f, 1.30f, -0.35f, 1.46f,
                    -0.30f, 1.60f, -0.24f, 1.73f, -0.15f, 1.86f, -0.02f, 1.97f, 0.14f, 2.05f, 0.30f, 2.08f, 0.38f, 2.07f, 0.41f, 2.00f,
                    0.40f, 1.85f, 0.44f, 2.12f, 0.45f, 2.34f, 0.43f, 2.52f, 0.38f, 2.65f, 0.35f, 2.75f, 0.33f, 2.80f, 0.32f, 2.82f, 0.32f, 2.84f, 0.31f, 2.86f,
                    0.32f, 2.90f, 0.33f, 2.97f, 0.34f, 3.06f, 0.34f, 3.16f, 0.32f, 3.25f, 0.29f, 3.35f, 0.22f, 3.42f, 0.11f, 3.44f, -0.04f, 3.44f, -0.19f, 3.43f,
                    -0.25f, 3.42f, -0.23f, 3.41f, -0.18f, 3.39f, -0.15f, 3.33f, -0.15f, 3.21f, -0.14f, 3.04f, -0.13f, 2.86f, -0.13f, 2.73f, -0.15f, 2.63f, -0.15f, 2.55f,
                    -0.13f, 2.51f, -0.05f, 2.48f, 0.05f, 2.43f, 0.14f, 2.33f, 0.21f, 2.21f, 0.25f, 2.09f, 0.26f, 1.97f, 0.25f, 1.84f, 0.21f, 1.70f, 0.17f, 1.57f,
                    0.14f, 1.49f, 0.13f, 1.51f, 0.16f, 1.57f, 0.22f, 1.64f, 0.28f, 1.68f, 0.32f, 1.70f, 0.33f, 1.71f, 0.32f, 1.70f, 0.31f, 1.69f
                } },

            // 돌진은 클립 0.15초(예고 시작)부터 왼발을 기준으로 하고, 0.90초에 왼발이 떨어지면 기준을 풀어 이동을 0.3초에 걸쳐 0으로 돌린다
            { MucheolMotionCatalog.DASH_CLIP, new[]
                {
                    0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, 0.00f, -0.01f, 0.20f, -0.04f, 0.28f, -0.06f, 0.23f, -0.08f, 0.12f, -0.08f, 0.06f, -0.10f, 0.06f,
                    -0.12f, 0.08f, -0.14f, 0.11f, -0.16f, 0.15f, -0.18f, 0.20f, -0.19f, 0.24f, -0.21f, 0.27f, -0.24f, 0.33f, -0.26f, 0.46f, -0.20f, 0.64f, -0.17f, 0.53f,
                    -0.13f, 0.43f, -0.10f, 0.32f, -0.07f, 0.21f, -0.03f, 0.11f, 0.00f, 0.00f
                } }
        };

        // 왼발을 기준으로 삼는 클립 시각의 끝, 표에 없으면 클립 끝까지이다. 이 시각 이후에는 양발의 고정을 모두 푼다
        private static readonly Dictionary<string, float> G_AnchorEnds = new Dictionary<string, float>
        {
            { MucheolMotionCatalog.DASH_CLIP, 0.90f }
        };

        // 왼발에서 오른발로 기준을 넘기는 클립 시각, 표에 없으면 넘기지 않는다
        private static readonly Dictionary<string, float> G_RightFrom = new Dictionary<string, float>
        {
            { MucheolMotionCatalog.COMBO_CLIP, 0.90f },
            { MucheolMotionCatalog.STOMP_CLIP, 0f },
            { MucheolMotionCatalog.DOUBLE_CLIP, 1.40f }
        };

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이 클립의 이 시각에 왼발을 지면에 고정한다, 발 고정이 왼발을 풀지 않고 높이도 지면에 둔다 </summary>
        public static bool IsLeftFootAnchored(string clipName, float clipSeconds)
        {
            return (clipName != null) && G_Paths.ContainsKey(clipName) && (clipSeconds < AnchorEnd(clipName)) && (clipSeconds < RightFrom(clipName));
        }

        /// <summary> 이 클립의 이 시각에 오른발을 지면에 고정한다, 기준이 왼발에서 넘어온 뒤이다 </summary>
        public static bool IsRightFootAnchored(string clipName, float clipSeconds)
        {
            return (clipName != null) && G_Paths.ContainsKey(clipName) && (clipSeconds >= RightFrom(clipName));
        }

        private static float RightFrom(string clipName)
        {
            return G_RightFrom.TryGetValue(clipName, out float from) ? from : float.MaxValue;
        }

        /// <summary> 왼발 기준이 끝나 양발의 고정을 모두 푼다, 표에 있는 클립이 기준 끝을 지난 뒤이다 </summary>
        public static bool IsFootLockReleased(string clipName, float clipSeconds)
        {
            return (clipName != null) && G_Paths.ContainsKey(clipName) && (clipSeconds >= AnchorEnd(clipName));
        }

        private static float AnchorEnd(string clipName)
        {
            return G_AnchorEnds.TryGetValue(clipName, out float end) ? end : float.MaxValue;
        }

        /// <summary> 이 클립의 clipSeconds 순간 이동량(x, z), 표에 없는 클립은 0이고 표 끝을 지나면 마지막 값을 유지한다 </summary>
        public static Vector2 Sample(string clipName, float clipSeconds)
        {
            if ((clipName == null) || (!G_Paths.TryGetValue(clipName, out float[] path)))
            {
                return Vector2.zero;
            }

            int   count = path.Length / PAIR_SIZE;
            float pos   = Mathf.Max(0f, clipSeconds) / SAMPLE_STEP;
            int   index = Mathf.FloorToInt(pos);
            if (index >= (count - 1))
            {
                return new Vector2(path[(count - 1) * PAIR_SIZE], path[((count - 1) * PAIR_SIZE) + 1]);
            }

            float t    = pos - index;
            int   from = index * PAIR_SIZE;
            int   to   = from + PAIR_SIZE;
            return new Vector2(Mathf.Lerp(path[from], path[to], t), Mathf.Lerp(path[from + 1], path[to + 1], t));
        }
    }
}
