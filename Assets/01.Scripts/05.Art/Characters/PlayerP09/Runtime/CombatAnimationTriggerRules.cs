namespace Shinshi.Art
{
    /// <summary> 공개 전투 상태 변화로 어떤 행동 모션을 재생할지 정하는 순수 규칙, 엔진 비의존이라 EditMode에서 시험한다 </summary>
    public static class CombatAnimationTriggerRules
    {
        /****************************************
        *                Fields
        ****************************************/

        public enum EAction
        {
            None,
            Dodge,
            Skill,
            Attack
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 한 프레임에 여러 행동이 함께 바뀌면 하나만 고른다. 회피가 기술을 끊고, 기술이 기본 공격보다 의미가 크다 </summary>
        public static EAction SelectAction(bool isDodgeNew, bool isSkillNew, bool isAttackNew)
        {
            if (isDodgeNew)
            {
                return EAction.Dodge;
            }

            if (isSkillNew)
            {
                return EAction.Skill;
            }

            return isAttackNew ? EAction.Attack : EAction.None;
        }

        /// <summary> 255 다음 0으로 도는 1바이트 순번이라 크기가 아니라 같은지만 본다 </summary>
        public static bool IsNewSequence(byte baseline, byte current) { return baseline != current; }

        /// <summary> 새 시전인지, 0은 시전 없음이다 </summary>
        public static bool IsNewCast(int baseline, int current) { return (current != 0) && (current != baseline); }

        /// <summary> 클립 길이를 목표 시간에 맞추는 재생 속도, 범위로 자른다 </summary>
        public static float ComputeSpeed(float clipLength, float targetSeconds, float minSpeed, float maxSpeed)
        {
            if ((clipLength <= 0f) || (targetSeconds <= 0f))
            {
                return 1f;
            }

            float speed = clipLength / targetSeconds;
            return (speed < minSpeed) ? minSpeed : ((speed > maxSpeed) ? maxSpeed : speed);
        }

        /// <summary> 진행 중 기술을 어디부터 재생할지. 경과가 음수면 아직 시작 전이라 기다리고(거짓, isWaiting 참),
        /// 재생 시간을 넘었으면 이미 끝나 재생하지 않는다(거짓, isWaiting 거짓) </summary>
        public static bool TryGetResumePosition(double elapsedSeconds, float speed, float clipLength, out float normalizedTime,
            out bool isWaiting)
        {
            normalizedTime = 0f;
            isWaiting      = elapsedSeconds < 0.0;
            if ((isWaiting) || (clipLength <= 0f) || (speed <= 0f))
            {
                return false;
            }

            double normalized = (elapsedSeconds * speed) / clipLength;
            if (normalized >= 1.0)
            {
                return false;
            }

            normalizedTime = (float)normalized;
            return true;
        }

        /// <summary> 기본 공격 콤보 다음 순서, 마지막 공격에서 오래 지났으면 처음부터 </summary>
        public static int NextAttackIndex(int previousIndex, float secondsSinceLastAttack, float resetSeconds, int count)
        {
            if ((previousIndex < 0) || (secondsSinceLastAttack > resetSeconds) || (count <= 0))
            {
                return 0;
            }

            return (previousIndex + 1) % count;
        }
    }
}
