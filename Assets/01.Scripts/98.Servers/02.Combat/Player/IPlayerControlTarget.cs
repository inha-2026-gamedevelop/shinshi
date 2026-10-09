// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 플레이어에게 기절과 속박을 거는 서버 창구, 적 두뇌는 피해를 적용한 뒤 대상을 이 인터페이스로 받아 부른다 </summary>
    public interface IPlayerControlTarget
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 적용 중인 제어, 기절이 속박보다 먼저다 </summary>
        EPlayerControlKind ControlKind { get; }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 제어를 건다, 같은 종류는 중첩하지 않고 남은 시간이 긴 쪽으로 갱신하며 제어가 끝난 뒤 면역 시간에는 걸리지 않는다 </summary>
        EPlayerControlResult ApplyControl(EPlayerControlKind kind, float seconds, double serverTime);
    }
}
