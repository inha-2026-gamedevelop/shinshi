// System
using System;
using System.Globalization;
using System.Text;

namespace Shinshi.Data.Admin
{
    /// <summary> 시각과 ID로 정렬한 어드민 조회의 keyset 위치. 클라이언트에는 불투명 문자열로만 준다 </summary>
    public sealed class AdminLogCursor
    {
        /****************************************
        *                Fields
        ****************************************/

        // 형식: base64url("1:{UTC ticks}:{Guid N}"). 앞 숫자는 형식 버전이다. ticks를 쓰는 이유는 timestamptz의
        // 마이크로초까지 그대로 되돌려야 같은 ms 안의 행을 건너뛰거나 반복하지 않기 때문이다
        private const string VERSION      = "1";
        private const char   SEPARATOR    = ':';
        private const int    PARTS        = 3;
        private const int    MAX_TEXT     = 128;

        // base64 한 묶음의 글자 수. base64url은 끝의 '='를 빼므로 해독 전에 다시 채운다
        private const int    BASE64_BLOCK = 4;

        /****************************************
        *              Properties
        ****************************************/

        public DateTime At { get; }
        public Guid Id { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminLogCursor(DateTime at, Guid id)
        {
            At = DateTime.SpecifyKind(at, DateTimeKind.Utc);
            Id = id;
        }

        public string Encode()
        {
            string text = VERSION + SEPARATOR + At.Ticks.ToString(CultureInfo.InvariantCulture) + SEPARATOR
                + Id.ToString("N");
            return Convert.ToBase64String(Encoding.ASCII.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary> 형식이 다르거나 범위를 벗어나면 false다. 호출자는 400으로 거절한다 </summary>
        public static bool TryDecode(string? value, out AdminLogCursor? cursor)
        {
            cursor = null;
            if (string.IsNullOrEmpty(value) || (value.Length > MAX_TEXT))
            {
                return false;
            }

            string text;
            try
            {
                string base64 = value.Replace('-', '+').Replace('_', '/');
                base64 = base64.PadRight(base64.Length + ((BASE64_BLOCK - (base64.Length % BASE64_BLOCK)) % BASE64_BLOCK),
                    '=');
                text = Encoding.ASCII.GetString(Convert.FromBase64String(base64));
            }
            catch (FormatException)
            {
                return false;
            }

            string[] parts = text.Split(SEPARATOR);
            if ((parts.Length != PARTS) || (parts[0] != VERSION)
                || (!long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks))
                || (ticks < DateTime.MinValue.Ticks) || (ticks > DateTime.MaxValue.Ticks)
                || (!Guid.TryParseExact(parts[2], "N", out Guid id)))
            {
                return false;
            }

            cursor = new AdminLogCursor(new DateTime(ticks, DateTimeKind.Utc), id);
            return true;
        }
    }
}
