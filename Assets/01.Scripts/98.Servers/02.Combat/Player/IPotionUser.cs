// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 물약을 쓰는 서버 전투 개체, PotionServer는 이 창구로 생사와 체력만 읽고 회복만 넣는다 </summary>
    public interface IPotionUser
    {
        /****************************************
        *              Properties
        ****************************************/

        int                   ParticipantId   { get; }
        EParticipantLifeState LifeState       { get; }
        float                 Health          { get; }
        float                 MaxHealth       { get; }

        // 기절처럼 물약을 막는 제어 상태, 속박은 막지 않는다
        bool                  IsControlLocked { get; }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 최대 체력을 넘는 몫은 버린다, 살아 있을 때만 적용한다 </summary>
        void Heal(float amount);
    }
}
