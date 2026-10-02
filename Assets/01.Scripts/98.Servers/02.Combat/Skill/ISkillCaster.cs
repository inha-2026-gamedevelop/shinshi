// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 서버 기술의 소유자, 공격력은 타격 시점 값을 읽는다 </summary>
    public interface ISkillCaster : IDamageable
    {
        /****************************************
        *              Properties
        ****************************************/

        float AttackPower { get; }

        /****************************************
        *                Methods
        ****************************************/

        void SetSkillPosition(Vector3 position);
        void SetSkillFacing(Vector3 direction);
    }
}
