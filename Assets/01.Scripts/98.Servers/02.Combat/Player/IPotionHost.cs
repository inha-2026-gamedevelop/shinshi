// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> PotionServer가 확정 시점에 참가자 개체를 찾고 소유자에게 상태를 알리는 창구 </summary>
    public interface IPotionHost
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버에 남아 있는 참가자 개체, 연결이 끊겨 남은 개체도 포함한다. 개체가 지워졌으면 거짓 </summary>
        bool TryGetPotionUser(int participantId, out IPotionUser user);

        /// <summary> 물약 상태가 바뀌었다, sequence는 답하는 요청 순번이고 요청 없이 바뀌었으면 0이다 </summary>
        void OnPotionState(int participantId, uint sequence, ERejectReason reason);
    }
}
