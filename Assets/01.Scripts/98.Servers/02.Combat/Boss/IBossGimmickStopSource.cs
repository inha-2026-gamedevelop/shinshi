// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    // 전멸로 인한 기믹 실패는 여기서 알리지 않는다. 세션이 같은 틱에 전멸로 닫으므로 두 번 닫히지 않게 한다
    /// <summary> 보스 기믹 묶음의 정지와 실패 조회 창구, 세션 연결이 틱마다 읽는다 </summary>
    public interface IBossGimmickStopSource
    {
        /// <summary> 정지나 전멸 아닌 실패가 있으면 참이다, failureReason은 kind가 Failed일 때만 뜻이 있다 </summary>
        bool TryGetStop(out EBossGimmickStop kind, out ESessionEndReason failureReason, out string detail);
    }
}
