// System
using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 기절과 속박이 막는 행동의 판단, 서버 판정과 클라이언트 입력의 미리 거르기가 같은 기준을 쓰도록 한곳에 둔다 </summary>
    public static class PlayerControlRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 두 제어의 종료 시각과 지금 시각으로 지금 적용 중인 제어를 정한다, 기절이 속박보다 먼저다 </summary>
        public static EPlayerControlKind Resolve(double stunEndsAt, double rootEndsAt, double now)
        {
            if (now < stunEndsAt)
            {
                return EPlayerControlKind.Stun;
            }

            return (now < rootEndsAt) ? EPlayerControlKind.Root : EPlayerControlKind.None;
        }

        /// <summary> 경로 이동을 막는다, 속박과 기절 모두 </summary>
        public static bool BlocksMovement(EPlayerControlKind kind)
        {
            return kind != EPlayerControlKind.None;
        }

        /// <summary> 회피를 막는다, 속박과 기절 모두 </summary>
        public static bool BlocksDodge(EPlayerControlKind kind)
        {
            return kind != EPlayerControlKind.None;
        }

        /// <summary> 후퇴 사격이나 돌진처럼 시전 중 몸이 움직이는 기술을 막는다, 속박과 기절 모두 </summary>
        public static bool BlocksMovementSkill(EPlayerControlKind kind)
        {
            return kind != EPlayerControlKind.None;
        }

        /// <summary> 제자리 기본 공격과 기술 시전을 막는다, 기절만 막고 속박은 허용한다 </summary>
        public static bool BlocksAction(EPlayerControlKind kind)
        {
            return kind == EPlayerControlKind.Stun;
        }

        /// <summary> 물약 사용을 막는다, 기절만 막는다 </summary>
        public static bool BlocksPotion(EPlayerControlKind kind)
        {
            return kind == EPlayerControlKind.Stun;
        }

        /// <summary> 제어 시간이 쓸 수 있는 값이면 참이다, 0 이하와 NaN, 무한대는 거짓 </summary>
        public static bool IsValidDuration(float seconds)
        {
            return (seconds > 0f) && (!float.IsNaN(seconds)) && (!float.IsInfinity(seconds));
        }

        /// <summary> 같은 종류가 다시 걸릴 때 새 종료 시각, 중첩하지 않고 남은 시간이 긴 쪽을 쓴다 </summary>
        public static double MergeEndsAt(double currentEndsAt, double now, double seconds)
        {
            return Math.Max(currentEndsAt, now + seconds);
        }
    }
}
