// System
using System;

namespace Shinshi.Admin.Web.Models
{
    /// <summary> 공지 게시 기간. 확인 모달을 연 시각 하나로 시작·종료를 모두 확정해 표시한 값과 전송 값이 같다 </summary>
    public static class NoticePeriod
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 시작이 비면 now, 종료가 비면 시작+기본 시간이다. 종료는 시작보다 뒤여야 한다 </summary>
        public static (long Starts, long Ends) Resolve(string? startText, string? endText, DateTimeOffset now,
            int defaultHours, WriteDefaults? defaults = null)
        {
            long nowMs = now.ToUnixTimeMilliseconds();
            long starts = AdminTime.ResolveOrDefault(startText, nowMs, "시작", out bool startDefaulted);
            if (startDefaulted)
            {
                defaults?.Note("시작: " + AdminTime.FormatKst(starts) + " (지금, 기본값)");
            }
            long ends = AdminTime.ResolveOrDefault(endText, starts + defaultHours * 3600_000L, "종료", out bool endDefaulted);
            if (endDefaulted)
            {
                defaults?.Note("종료: " + AdminTime.FormatKst(ends) + " (시작 + " + defaultHours + "시간, 기본값)");
            }
            if (ends <= starts)
            {
                throw new ArgumentException("종료 시각은 시작 시각 이후로 지정하세요.");
            }
            return (starts, ends);
        }
    }
}
