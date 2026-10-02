// System
using System;

namespace Shinshi.Data.Admin
{
    /// <summary> 감사 로그 조회 필터. TargetId는 TargetKind와 함께만 쓴다(대상 인덱스가 종류부터 시작한다) </summary>
    public sealed class AdminAuditQuery
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid?           AdminId    { get; init; }
        public string?         TargetKind { get; init; }
        public string?         TargetId   { get; init; }
        public DateTimeOffset? From       { get; init; }
        public DateTimeOffset? To         { get; init; }
        public AdminLogCursor? Cursor     { get; init; }
        public int             Limit      { get; init; }
    }
}
