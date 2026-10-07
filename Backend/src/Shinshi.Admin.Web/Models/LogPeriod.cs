// System
using System;

namespace Shinshi.Admin.Web.Models
{
    /// <summary> 로그 조회 기간. 종료가 비면 최초 조회 시각으로 고정해 다음 페이지 요청에도 같은 상한을 쓴다 </summary>
    public static class LogPeriod
    {
        /****************************************
        *                Methods
        ****************************************/

        public static (long FromMs, long ToMs, bool ToDefaulted) Resolve(string? fromText, string? toText,
            DateTimeOffset now, int defaultHours)
        {
            long? typedTo = AdminTime.ParseUnixMs(toText, "종료");
            long to = typedTo ?? now.ToUnixTimeMilliseconds();
            long from = AdminTime.ParseUnixMs(fromText, "시작") ?? (to - defaultHours * 3600_000L);
            if (from >= to)
            {
                throw new ArgumentException("시작 시각은 종료 시각보다 앞서야 합니다.");
            }
            return (from, to, !typedTo.HasValue);
        }
    }
}
