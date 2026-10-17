// System
using System;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Combat
{
    /// <summary> 궁수 속성의 기본 공격 보정과 스택 획득, 서버 판정과 클라이언트 입력, 표현이 같은 규칙을 쓴다 </summary>
    public static class ArcherElementRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 기본 공격 단계 주기에 곱하는 값, 바람만 1보다 작다 </summary>
        public static float GetAttackIntervalScale(SkillCatalogSO catalog, EArcherElement element)
        {
            return ((catalog != null) && (element == EArcherElement.Wind)) ? catalog.WindAttackIntervalScale : 1f;
        }

        /// <summary> 기본 공격 화살 속도에 곱하는 값, 바람만 1보다 크다 </summary>
        public static float GetProjectileSpeedScale(SkillCatalogSO catalog, EArcherElement element)
        {
            return ((catalog != null) && (element == EArcherElement.Wind)) ? catalog.WindProjectileSpeedScale : 1f;
        }

        // 속성 없는 화살은 쌓지 않는다. 직전 적중이 없거나(처음, 사망 뒤) 같은 속성이면 유지 값이고 다르면 전환 값이다.
        // 처음 맞힌 화살을 전환으로 치지 않는 것은 사망 뒤 첫 적중이 공짜 보너스가 되지 않게 하려는 것이다
        /// <summary> 기본 공격 화살 한 발이 적을 맞혔을 때 얻는 스택 </summary>
        public static int GetStackGain(SkillCatalogSO catalog, EArcherElement hit, EArcherElement previous)
        {
            if ((catalog == null) || (hit == EArcherElement.None))
            {
                return 0;
            }

            bool switched = (previous != EArcherElement.None) && (previous != hit);
            return switched ? catalog.ArcherStackPerSwitchHit : catalog.ArcherStackPerSameHit;
        }

        /// <summary> 상한을 넘지 않게 더한 스택 </summary>
        public static int AddStacks(int current, int gain, int max)
        {
            if (gain <= 0)
            {
                return current;
            }

            return Math.Min(max, current + gain);
        }
    }
}
