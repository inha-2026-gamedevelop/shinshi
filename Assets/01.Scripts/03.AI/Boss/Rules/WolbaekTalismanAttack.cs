// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 범위와 시각은 서버가 확정한 값이다. 피해와 밀려남과 방어 불가 판정은 서버가 하고 클라이언트는 예고 도형만 그린다
    /// <summary> 부적진 기믹 중 보스 공격 하나의 예고 입력, 고리 또는 직선이다 </summary>
    public readonly struct WolbaekTalismanAttack
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 기믹 안에서 1부터 오르는 공격 번호 </summary>
        public int AttackId { get; }

        public EWolbaekTalismanAttackKind Kind { get; }

        /// <summary> 예고를 시작한 서버 시각 </summary>
        public double TelegraphServerTime { get; }

        /// <summary> 즉발 판정이 나는 서버 시각 </summary>
        public double StrikeServerTime { get; }

        /// <summary> 고리의 중심이거나 직선의 시작점 </summary>
        public Vector3 Origin { get; }

        /// <summary> 직선의 수평 단위 방향, 고리에서는 0벡터다 </summary>
        public Vector3 Direction { get; }

        /// <summary> m, 고리의 안쪽 반경, 직선에서는 0이다 </summary>
        public float InnerRadius { get; }

        /// <summary> m, 고리의 바깥쪽 반경, 직선에서는 0이다 </summary>
        public float OuterRadius { get; }

        /// <summary> m, 직선의 길이, 고리에서는 0이다 </summary>
        public float Length { get; }

        /// <summary> m, 직선의 폭, 고리에서는 0이다 </summary>
        public float Width { get; }

        /// <summary> 직선이 겨눈 구조물의 배정 참가자 전투 번호, 고리에서는 없음이다 </summary>
        public int TargetCombatId { get; }

        /****************************************
        *                Methods
        ****************************************/

        private WolbaekTalismanAttack(int attackId, EWolbaekTalismanAttackKind kind, double telegraphServerTime,
            double strikeServerTime, Vector3 origin, Vector3 direction, float innerRadius, float outerRadius, float length,
            float width, int targetCombatId)
        {
            AttackId            = attackId;
            Kind                = kind;
            TelegraphServerTime = telegraphServerTime;
            StrikeServerTime    = strikeServerTime;
            Origin              = origin;
            Direction           = direction;
            InnerRadius         = innerRadius;
            OuterRadius         = outerRadius;
            Length              = length;
            Width               = width;
            TargetCombatId      = targetCombatId;
        }

        public static WolbaekTalismanAttack Ring(int attackId, double telegraphServerTime, double strikeServerTime,
            Vector3 origin, float innerRadius, float outerRadius)
        {
            return new WolbaekTalismanAttack(attackId, EWolbaekTalismanAttackKind.Ring, telegraphServerTime, strikeServerTime,
                origin, Vector3.zero, innerRadius, outerRadius, 0f, 0f, 0);
        }

        public static WolbaekTalismanAttack Line(int attackId, double telegraphServerTime, double strikeServerTime,
            Vector3 origin, Vector3 direction, float length, float width, int targetCombatId)
        {
            return new WolbaekTalismanAttack(attackId, EWolbaekTalismanAttackKind.Line, telegraphServerTime, strikeServerTime,
                origin, direction, 0f, 0f, length, width, targetCombatId);
        }
    }
}
