// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 노란 여우불은 중앙 바닥의 검증된 위치에 고정되고 위치가 바뀌지 않는다
    /// <summary> 노란 여우불 하나의 입력, 번호와 피격 대상 번호와 위치 </summary>
    public readonly struct WolbaekFinalYellow
    {
        /****************************************
        *              Properties
        ****************************************/

        public int Index { get; }

        /// <summary> 플레이어 공격이 맞히는 피격 대상의 전투 번호 </summary>
        public int CombatId { get; }

        public Vector3 Position { get; }

        public float MaxHealth { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekFinalYellow(int index, int combatId, Vector3 position, float maxHealth)
        {
            Index     = index;
            CombatId  = combatId;
            Position  = position;
            MaxHealth = maxHealth;
        }
    }
}
