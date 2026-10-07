// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 출정의 확정된 시작과 종료, 경매 재입장을 전투 기록기에 알린다 </summary>
    public interface ICombatMeterSink
    {
        void OnSessionStarted(double serverTime);
        void OnSessionClosed(ESessionEndReason reason, double serverTime);
        void ResendFinal(int participantId);
    }
}
