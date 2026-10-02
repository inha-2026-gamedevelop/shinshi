// System
using System;

// Project
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 로그 조회 조회 문자열 검증. 형식이나 범위가 틀리면 AdminRequestException(400 INVALID_REQUEST)이다 </summary>
    public static class AdminLogRequests
    {
        /****************************************
        *                Methods
        ****************************************/

        public static int ReadLimit(int? limit)
        {
            int value = limit ?? AdminLogQueryParameters.DEFAULT_LIMIT;
            if ((value <= 0) || (value > AdminLogQueryParameters.MAX_LIMIT))
            {
                throw new AdminRequestException("limit must be between 1 and 500.");
            }

            return value;
        }

        public static AdminLogCursor? ReadCursor(string? cursor)
        {
            if (cursor == null)
            {
                return null;
            }
            if (!AdminLogCursor.TryDecode(cursor, out AdminLogCursor? decoded))
            {
                throw new AdminRequestException("cursor is not a value returned by this API.");
            }

            return decoded;
        }

        /// <summary> 민감 조회 감사 사유. 비었거나 공백뿐이거나 500자를 넘으면 400이다 </summary>
        public static string ReadAuditReason(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason) || (reason.Length > AdminContractCodes.MAX_REASON_LENGTH))
            {
                throw new AdminRequestException("reason must be a non-empty string of at most 500 characters.");
            }

            return reason;
        }

        /// <summary> from은 포함, to는 제외다. 둘 다 있으면 from이 to보다 앞서야 한다 </summary>
        public static (DateTimeOffset? From, DateTimeOffset? To) ReadRange(long? fromUnixMs, long? toUnixMs)
        {
            DateTimeOffset? from = ReadTime(fromUnixMs);
            DateTimeOffset? to = ReadTime(toUnixMs);
            if ((from.HasValue) && (to.HasValue) && (from.Value >= to.Value))
            {
                throw new AdminRequestException("fromUnixMs must be earlier than toUnixMs.");
            }

            return (from, to);
        }

        private static DateTimeOffset? ReadTime(long? unixMs)
        {
            if (!unixMs.HasValue)
            {
                return null;
            }

            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(unixMs.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new AdminRequestException("Unix time is out of range.");
            }
        }
    }
}
