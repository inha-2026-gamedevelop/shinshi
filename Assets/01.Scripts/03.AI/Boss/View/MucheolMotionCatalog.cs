// System
using System.Collections.Generic;

namespace Shinshi.AI
{
    // 값은 사용자가 무철 모션 프리뷰 씬에서 확인한 패턴별 MotionCue를 옮긴 것이다. 표현 상수이고 밸런스 값이 아니다.
    // 프리뷰는 예고 시작을 0초로 두었고 그 예고 시간과 타 지연이 서버 정의와 같다. 그래서 타격 키는 서버 타 확정 시각에,
    // 그 앞뒤 키는 같은 간격의 오프셋으로 옮겼다. 서버 시간이 바뀌면 타격 순간은 서버를 따르고 앞뒤 길이만 그대로 남는다.
    // 프리뷰의 P01 후보 비교 항목은 실제 배정이 아니라 옮기지 않았다. 1페이즈 P01만 후보 B(attack01)로 확정됐고 2페이즈 P01은 프리뷰 배정(attack04)이다.
    // 서버 어셈블리의 패턴 ID 상수를 볼 수 없어 같은 문자열을 따로 둔다
    /// <summary> 무철 패턴 ID로 확정한 모션 구간을 찾는다 </summary>
    public static class MucheolMotionCatalog
    {
        /****************************************
        *              Constants
        ****************************************/

        public const string IDLE_CLIP        = "idle";
        public const string SWING_CLIP       = "attack01";
        public const string COMBO_CLIP       = "attack02";
        public const string DOUBLE_CLIP      = "attack04";
        public const string SLAM_CLIP        = "attack_ground02";
        public const string DASH_CLIP        = "attack_DashAtk";
        public const string STOMP_CLIP       = "attack_foot_left";
        public const string IMPACT_CLIP      = "hit_front";
        public const string STUN_START_CLIP  = "Stun_Start";
        public const string STUN_END_CLIP    = "Stun_End";

        /// <summary> 초, 돌진이 일찍 멈췄을 때 들이받는 구간의 끝 자세까지 몰아 가는 시간, 정상 끝과 같은 자세로 마친다 </summary>
        public const float DASH_STOP_SECONDS = 0.15f;

        /// <summary> 같은 클립을 끊어서 다시 시작할 때 섞을 수 있도록 컨트롤러에 둔 두 번째 상태의 접미사 </summary>
        public const string ALTERNATE_SUFFIX = "_B";

        public const string P1_P01_ID = "mucheol_p01";
        public const string P1_P02_ID = "mucheol_p02";
        public const string P1_P03_ID = "mucheol_p03";
        public const string P1_P04_ID = "mucheol_p04";
        public const string P1_P05_ID = "mucheol_p05";
        public const string P1_P06_ID = "mucheol_p06";
        public const string P2_P01_ID = "mucheol_ph2_p01";
        public const string P2_P02_ID = "mucheol_ph2_p02";
        public const string P2_P03_ID = "mucheol_ph2_p03";
        public const string P2_P04_ID = "mucheol_ph2_p04";
        public const string P2_P05_ID = "mucheol_ph2_p05";
        public const string P2_P06_ID = "mucheol_ph2_p06";
        public const string P2_P07_ID = "mucheol_ph2_p07";

        // 클립 시간(초)
        private const float CLIP_ZERO             = 0f;
        private const float SWING_IMPACT          = 1.78f;
        private const float SWING_END             = 3.3057141f;
        private const float SLAM_IMPACT           = 1.15f;
        private const float SLAM_END              = 3.25f;
        private const float STOMP_START           = 0.15f;
        private const float STOMP_IMPACT          = 1.65f;
        private const float STOMP_END             = 3.4f;
        private const float COMBO_FIRST           = 0.84f;
        private const float COMBO_SECOND_P1       = 2.4f;
        private const float COMBO_SECOND_P2       = 1.2f;
        private const float COMBO_THIRD_P2        = 2.4f;
        private const float COMBO_END             = 3.3333335f;
        private const float DOUBLE_START          = 0.15f;
        private const float DOUBLE_FIRST          = 0.78f;
        private const float DOUBLE_SECOND_START   = 1.2f;
        private const float DOUBLE_SECOND         = 1.65f;
        private const float DOUBLE_END            = 3.1f;
        private const float DASH_WINDUP_START     = 0.15f;
        private const float DASH_RUN_START        = 0.75f;
        private const float DASH_RUN_END          = 1.4f;
        private const float IMPACT_PEAK           = 0.2f;
        private const float IMPACT_END            = 0.4f;
        private const float STUN_DOWN_START       = 0.87f;
        private const float STUN_DOWN_HOLD        = 1.6656667f; // 끝 프레임 바로 앞, 반복 클립이 처음으로 돌아가지 않게 한다
        private const float STUN_UP_PEAK          = 0.8f;
        private const float STUN_UP_END           = 1.6f;

        // 기준 시각에서 더하는 시간(초)
        private const float NO_OFFSET             = 0f;
        private const float SWING_TAIL            = 0.6f;
        private const float SLAM_TAIL_P1          = 1.2f;
        private const float SLAM_TAIL_P2          = 1.7f;
        private const float SLAM_TAIL_P7          = 1.8f;
        private const float STOMP_TAIL_P1         = 0.8f;
        private const float STOMP_TAIL_P2         = 1.6f;
        private const float COMBO_TAIL_P1         = 1.2f;
        private const float COMBO_TAIL_P2         = 1.5f;
        private const float DOUBLE_TAIL           = 0.7f;
        private const float IMPACT_PEAK_SECONDS   = 0.2f;
        private const float IMPACT_END_SECONDS    = 0.55f;
        private const float STUN_DOWN_SECONDS     = 0.8f;
        private const float STUN_UP_LEAD          = -0.8f; // 그로기 끝에 다 일어서도록 끝보다 앞서 시작한다
        private const float STUN_UP_PEAK_LEAD     = -0.4f;

        private const int FIRST_STRIKE  = 0;
        private const int SECOND_STRIKE = 1;
        private const int THIRD_STRIKE  = 2;

        /****************************************
        *                Fields
        ****************************************/

        private static readonly Dictionary<string, MucheolMotionSegment[]> G_StrikeMotions = BuildStrikeMotions();
        private static readonly HashSet<string>                            G_DashPatterns  = new HashSet<string>
        {
            P1_P03_ID, P1_P06_ID, P2_P03_ID, P2_P06_ID
        };

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 돌진 예고 동안 몸을 웅크려 모으는 구간, 예고 끝에 멈춰 돌진 시작을 기다린다 </summary>
        public static MucheolMotionSegment DashWindup { get; } = new MucheolMotionSegment(DASH_CLIP,
            new MucheolMotionKey(EMucheolMotionAnchor.CastStart, NO_OFFSET, DASH_WINDUP_START),
            new MucheolMotionKey(EMucheolMotionAnchor.TelegraphEnd, NO_OFFSET, DASH_RUN_START));

        /// <summary> 서버 돌진 시작부터 최대 거리 도달까지 들이받는 구간 </summary>
        public static MucheolMotionSegment DashRun { get; } = new MucheolMotionSegment(DASH_CLIP,
            new MucheolMotionKey(EMucheolMotionAnchor.DashStart, NO_OFFSET, DASH_RUN_START),
            new MucheolMotionKey(EMucheolMotionAnchor.DashEnd, NO_OFFSET, DASH_RUN_END));

        /// <summary> 들이받는 구간 끝의 클립 시간, 돌진이 일찍 멈춰도 이 자세로 마친다 </summary>
        public static float DashRunEndClip => DASH_RUN_END;

        /// <summary> 연속 돌진의 다음 돌진 재조준, 같은 클립을 다시 모으는 자세부터 시작한다 </summary>
        public static MucheolMotionSegment DashReaim { get; } = DashWindup;

        /// <summary> 마지막이 아닌 충돌의 반동, 기믹 1 프리뷰의 중간 충돌 값이다 </summary>
        public static MucheolMotionSegment ChargeImpact { get; } = new MucheolMotionSegment(IMPACT_CLIP,
            new MucheolMotionKey(EMucheolMotionAnchor.Event, NO_OFFSET, CLIP_ZERO),
            new MucheolMotionKey(EMucheolMotionAnchor.Event, IMPACT_PEAK_SECONDS, IMPACT_PEAK),
            new MucheolMotionKey(EMucheolMotionAnchor.Event, IMPACT_END_SECONDS, IMPACT_END));

        // 마지막 충돌 직후 바로 내려앉는다. 몸을 세우는 초반을 건너뛰고 끝 자세를 그로기 끝까지 그대로 유지한다
        // Stun_Loop로 바꾸면 유지 자세가 다시 움직여 확정한 연출과 달라진다
        /// <summary> 마지막 충돌부터 그로기 끝 직전까지 내려앉아 유지하는 구간 </summary>
        public static MucheolMotionSegment GroggyDown { get; } = new MucheolMotionSegment(STUN_START_CLIP,
            new MucheolMotionKey(EMucheolMotionAnchor.Event, NO_OFFSET, STUN_DOWN_START),
            new MucheolMotionKey(EMucheolMotionAnchor.Event, STUN_DOWN_SECONDS, STUN_DOWN_HOLD),
            new MucheolMotionKey(EMucheolMotionAnchor.GroggyEnd, STUN_UP_LEAD, STUN_DOWN_HOLD));

        /// <summary> 그로기 끝을 아직 모를 때의 내려앉기, 끝 자세에서 멈춰 GroggyUp을 기다린다 </summary>
        public static MucheolMotionSegment GroggyDownOpen { get; } = new MucheolMotionSegment(STUN_START_CLIP,
            new MucheolMotionKey(EMucheolMotionAnchor.Event, NO_OFFSET, STUN_DOWN_START),
            new MucheolMotionKey(EMucheolMotionAnchor.Event, STUN_DOWN_SECONDS, STUN_DOWN_HOLD));

        /// <summary> 유지 자세에서 Idle을 거치지 않고 일어서는 구간, 그로기 끝에 끝난다 </summary>
        public static MucheolMotionSegment GroggyUp { get; } = new MucheolMotionSegment(STUN_END_CLIP,
            new MucheolMotionKey(EMucheolMotionAnchor.GroggyEnd, STUN_UP_LEAD, CLIP_ZERO),
            new MucheolMotionKey(EMucheolMotionAnchor.GroggyEnd, STUN_UP_PEAK_LEAD, STUN_UP_PEAK),
            new MucheolMotionKey(EMucheolMotionAnchor.GroggyEnd, NO_OFFSET, STUN_UP_END));

        /// <summary> 컨트롤러에 있어야 하는 클립 상태 이름, 각 이름에 ALTERNATE_SUFFIX를 붙인 상태도 둔다 </summary>
        public static IReadOnlyList<string> ActionClips { get; } = new[]
        {
            SWING_CLIP, COMBO_CLIP, DOUBLE_CLIP, SLAM_CLIP, DASH_CLIP, STOMP_CLIP, IMPACT_CLIP, STUN_START_CLIP, STUN_END_CLIP
        };

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이 패턴의 몸 동작을 확정 모션이 맡는다 </summary>
        public static bool Handles(string patternId)
        {
            return (patternId != null) && (G_StrikeMotions.ContainsKey(patternId) || G_DashPatterns.Contains(patternId));
        }

        public static bool IsDash(string patternId)
        {
            return (patternId != null) && G_DashPatterns.Contains(patternId);
        }

        /// <summary> 타격형 패턴(부채꼴, 원형, 콤보, 순차 타격, 전장 공격)의 구간들 </summary>
        public static bool TryGetStrike(string patternId, out MucheolMotionSegment[] segments)
        {
            if (patternId == null)
            {
                segments = null;
                return false;
            }

            return G_StrikeMotions.TryGetValue(patternId, out segments);
        }

        private static MucheolMotionSegment[] Single(string clip, params MucheolMotionKey[] keys)
        {
            return new[] { new MucheolMotionSegment(clip, keys) };
        }

        private static Dictionary<string, MucheolMotionSegment[]> BuildStrikeMotions()
        {
            var castStart = EMucheolMotionAnchor.CastStart;

            return new Dictionary<string, MucheolMotionSegment[]>
            {
                // 1페이즈 P01 팔 휘두르기, 후보 B. 0.7초 타격에 클립 1.78초를 맞춘다
                { P1_P01_ID, Single(SWING_CLIP,
                    new MucheolMotionKey(castStart, NO_OFFSET, CLIP_ZERO),
                    MucheolMotionKey.AtStrike(FIRST_STRIKE, NO_OFFSET, SWING_IMPACT),
                    MucheolMotionKey.AtStrike(FIRST_STRIKE, SWING_TAIL, SWING_END)) },

                { P1_P02_ID, Single(SLAM_CLIP,
                    new MucheolMotionKey(castStart, NO_OFFSET, CLIP_ZERO),
                    MucheolMotionKey.AtStrike(FIRST_STRIKE, NO_OFFSET, SLAM_IMPACT),
                    MucheolMotionKey.AtStrike(FIRST_STRIKE, SLAM_TAIL_P1, SLAM_END)) },

                { P1_P04_ID, Single(STOMP_CLIP,
                    new MucheolMotionKey(castStart, NO_OFFSET, STOMP_START),
                    MucheolMotionKey.AtStrike(FIRST_STRIKE, NO_OFFSET, STOMP_IMPACT),
                    MucheolMotionKey.AtStrike(FIRST_STRIKE, STOMP_TAIL_P1, STOMP_END)) },

                // P05는 이지와 하드, 1페이즈와 2페이즈 모두 attack02 한 클립이다. 타마다 다시 시작하지 않고 타 사이 속도만 바꾼다
                { P1_P05_ID, Single(COMBO_CLIP,
                    new MucheolMotionKey(castStart, NO_OFFSET, CLIP_ZERO),
                    MucheolMotionKey.AtStrike(FIRST_STRIKE, NO_OFFSET, COMBO_FIRST),
                    MucheolMotionKey.AtStrike(SECOND_STRIKE, NO_OFFSET, COMBO_SECOND_P1),
                    MucheolMotionKey.AtStrike(SECOND_STRIKE, COMBO_TAIL_P1, COMBO_END)) },

                // 2페이즈 P01은 프리뷰 배정 그대로 attack04를 두 구간으로 쓴다. 둘째 구간은 같은 클립의 뒤쪽에서 다시 시작한다
                { P2_P01_ID, new[]
                    {
                        new MucheolMotionSegment(DOUBLE_CLIP,
                            new MucheolMotionKey(castStart, NO_OFFSET, DOUBLE_START),
                            MucheolMotionKey.AtStrike(FIRST_STRIKE, NO_OFFSET, DOUBLE_FIRST)),
                        new MucheolMotionSegment(DOUBLE_CLIP,
                            MucheolMotionKey.AtStrike(FIRST_STRIKE, NO_OFFSET, DOUBLE_SECOND_START),
                            MucheolMotionKey.AtStrike(SECOND_STRIKE, NO_OFFSET, DOUBLE_SECOND),
                            MucheolMotionKey.AtStrike(SECOND_STRIKE, DOUBLE_TAIL, DOUBLE_END))
                    } },

                { P2_P02_ID, Single(SLAM_CLIP,
                    new MucheolMotionKey(castStart, NO_OFFSET, CLIP_ZERO),
                    MucheolMotionKey.AtStrike(FIRST_STRIKE, NO_OFFSET, SLAM_IMPACT),
                    MucheolMotionKey.AtStrike(FIRST_STRIKE, SLAM_TAIL_P2, SLAM_END)) },

                { P2_P04_ID, Single(STOMP_CLIP,
                    new MucheolMotionKey(castStart, NO_OFFSET, STOMP_START),
                    MucheolMotionKey.AtStrike(FIRST_STRIKE, NO_OFFSET, STOMP_IMPACT),
                    MucheolMotionKey.AtStrike(FIRST_STRIKE, STOMP_TAIL_P2, STOMP_END)) },

                { P2_P05_ID, Single(COMBO_CLIP,
                    new MucheolMotionKey(castStart, NO_OFFSET, CLIP_ZERO),
                    MucheolMotionKey.AtStrike(FIRST_STRIKE, NO_OFFSET, COMBO_FIRST),
                    MucheolMotionKey.AtStrike(SECOND_STRIKE, NO_OFFSET, COMBO_SECOND_P2),
                    MucheolMotionKey.AtStrike(THIRD_STRIKE, NO_OFFSET, COMBO_THIRD_P2),
                    MucheolMotionKey.AtStrike(THIRD_STRIKE, COMBO_TAIL_P2, COMBO_END)) },

                // 하드 전장 공격은 예고 4초 동안 천천히 모았다가 예고 끝 타격에 내려찍는다
                { P2_P07_ID, Single(SLAM_CLIP,
                    new MucheolMotionKey(castStart, NO_OFFSET, CLIP_ZERO),
                    MucheolMotionKey.AtStrike(FIRST_STRIKE, NO_OFFSET, SLAM_IMPACT),
                    MucheolMotionKey.AtStrike(FIRST_STRIKE, SLAM_TAIL_P7, SLAM_END)) }
            };
        }
    }
}
