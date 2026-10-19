// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    /// <summary> 일반 적 종류별 임시 표시 색과 크기, 모델이 들어오기 전 대체 표현에 쓴다 </summary>
    public static class EnemyRoleColors
    {
        /****************************************
        *              Constants
        ****************************************/

        public const float ELITE_SCALE = 1.25f;

        /****************************************
        *                Methods
        ****************************************/

        public static Color Get(EEnemyRole role)
        {
            switch (role)
            {
                case EEnemyRole.Ranged:
                    return new Color(0.3f, 0.6f, 0.95f, 1f);
                case EEnemyRole.Charger:
                    return new Color(0.8f, 0.2f, 0.7f, 1f);
                case EEnemyRole.Elite:
                    return new Color(0.95f, 0.8f, 0.15f, 1f);
                default:
                    return new Color(0.85f, 0.35f, 0.2f, 1f);
            }
        }

        public static float GetScale(EEnemyRole role)
        {
            return (role == EEnemyRole.Elite) ? ELITE_SCALE : 1f;
        }
    }
}
