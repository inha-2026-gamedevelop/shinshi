// System
using System;
using System.Collections.Generic;
using System.Globalization;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.Admin.Web.Models
{
    /// <summary> placeholder에 보이는 값이 실제 기본값이다. 비워 두면 이 값을 보내고 확인 화면에 적용 내용을 알린다 </summary>
    public static class AdminDefaults
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string READ_REASON             = "운영 조회";
        public const string NOTICE_CREATE_REASON    = "공지 작성";
        public const string NOTICE_END_REASON       = "공지 종료";
        public const string GRANT_CREATE_REASON     = "운영 지급";
        public const string GRANT_EXECUTE_REASON    = "운영 지급 실행";
        public const string GRANT_CANCEL_REASON     = "운영 지급 취소";
        public const string ADJUST_REASON           = "재화 조정";
        public const string KICK_REASON             = "강제 종료";
        public const string SERVICE_STATE_REASON    = "서비스 상태 변경";
        public const string RECOVERY_REASON         = "장애 복구";
        public const string INVITE_ISSUE_REASON     = "초대 발급";
        public const string INVITE_REVOKE_REASON    = "초대 폐기";
        public const string OPERATOR_CREATE_REASON  = "운영자 생성";
        public const string OPERATOR_DISABLE_REASON = "운영자 비활성화";

        public const int    LIST_LIMIT      = 50;
        public const int    CLAIM_DAYS      = 30;
        public const int    INVITE_USES     = 1;
        public const int    INVITE_HOURS    = 24;
        public const int    NOTICE_HOURS    = 24;
        public const int    LOG_RANGE_HOURS = 24;
        public const string NOTICE_TARGET   = "Both";
        public const int    NOTICE_SEVERITY = 0;
        public const string NOTICE_CATEGORY = "Notice";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 사유를 정한다. 비었으면 기본 문구를 쓰고 defaulted를 true로 한다 </summary>
        public static string Reason(string? input, string fallback, out bool defaulted)
        {
            string value = input?.Trim() ?? string.Empty;
            defaulted = value.Length == 0;
            if (defaulted)
            {
                value = fallback;
            }
            if (value.Length > AdminContractCodes.MAX_REASON_LENGTH)
            {
                throw new ArgumentException("사유는 1~500자로 입력하세요.");
            }
            return value;
        }

        /// <summary> 정수 입력. 비었으면 기본값, 숫자가 아니거나 범위 밖이면 ArgumentException </summary>
        public static int Int(string? text, int fallback, int min, int max, string label, out bool defaulted)
        {
            string value = text?.Trim() ?? string.Empty;
            defaulted = value.Length == 0;
            if (defaulted)
            {
                return fallback;
            }
            if ((!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                || (number < min) || (number > max))
            {
                throw new ArgumentException(label + "은(는) " + min.ToString(CultureInfo.InvariantCulture) + "~"
                    + max.ToString(CultureInfo.InvariantCulture) + " 사이 숫자로 입력하세요.");
            }
            return number;
        }

        public static long Long(string? text, long fallback, long min, long max, string label, out bool defaulted)
        {
            string value = text?.Trim() ?? string.Empty;
            defaulted = value.Length == 0;
            if (defaulted)
            {
                return fallback;
            }
            if ((!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number))
                || (number < min) || (number > max))
            {
                throw new ArgumentException(label + "은(는) " + min.ToString(CultureInfo.InvariantCulture) + "~"
                    + max.ToString(CultureInfo.InvariantCulture) + " 사이 숫자로 입력하세요.");
            }
            return number;
        }
    }

    /// <summary> 한 번의 쓰기 작성에서 기본값이 적용된 항목. 확인 모달이 그대로 보여 준다 </summary>
    public sealed class WriteDefaults
    {
        /****************************************
        *              Properties
        ****************************************/

        public List<string> Notes { get; } = new();
        public bool ReasonDefaulted { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        public string Reason(string? input, string fallback)
        {
            string reason = AdminDefaults.Reason(input, fallback, out bool defaulted);
            ReasonDefaulted = defaulted;
            return reason;
        }

        public int Int(string? text, int fallback, int min, int max, string label, string unit = "")
        {
            int value = AdminDefaults.Int(text, fallback, min, max, label, out bool defaulted);
            if (defaulted)
            {
                Notes.Add(label + ": " + value.ToString(CultureInfo.InvariantCulture) + unit + " (기본값)");
            }
            return value;
        }

        public long Long(string? text, long fallback, long min, long max, string label, string unit = "")
        {
            long value = AdminDefaults.Long(text, fallback, min, max, label, out bool defaulted);
            if (defaulted)
            {
                Notes.Add(label + ": " + value.ToString(CultureInfo.InvariantCulture) + unit + " (기본값)");
            }
            return value;
        }

        public long Time(string? text, long fallbackUnixMs, string label)
        {
            long value = AdminTime.ResolveOrDefault(text, fallbackUnixMs, label, out bool defaulted);
            if (defaulted)
            {
                Notes.Add(label + ": " + AdminTime.FormatKst(value) + " (기본값)");
            }
            return value;
        }

        public void Note(string text)
        {
            Notes.Add(text);
        }
    }
}
