// System
using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 서버 풀에 등록할 접속 주소 검사, 오류에는 입력 값을 넣지 않는다 </summary>
    public static class PublicAddressRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 호스트 이름이나 IP면 참, 비었으면 allowDefault일 때만 루프백을 쓴다 </summary>
        public static bool TryResolve(string raw, bool allowDefault, out string address, out string error)
        {
            address = null;
            error   = null;
            if (string.IsNullOrEmpty(raw))
            {
                if (allowDefault)
                {
                    address = Constants.Network.DEV_PUBLIC_ADDRESS;
                    return true;
                }

                error = $"{Constants.Network.ENV_PUBLIC_ADDRESS} ({Constants.Network.ARG_PUBLIC_ADDRESS}): " +
                    "출시 서버는 클라이언트가 붙을 접속 주소가 필요하다";
                return false;
            }

            if ((raw.Length > Constants.Network.MAX_PUBLIC_ADDRESS_LENGTH) || (ContainsWhitespace(raw))
                || (Uri.CheckHostName(raw) == UriHostNameType.Unknown))
            {
                error = $"{Constants.Network.ENV_PUBLIC_ADDRESS} ({Constants.Network.ARG_PUBLIC_ADDRESS}): " +
                    $"공백 없는 1~{Constants.Network.MAX_PUBLIC_ADDRESS_LENGTH}자 호스트 이름이나 IP가 필요하다";
                return false;
            }

            address = raw;
            return true;
        }

        private static bool ContainsWhitespace(string value)
        {
            for (int i = 0; i < value.Length; ++i)
            {
                if ((char.IsWhiteSpace(value[i])) || (char.IsControl(value[i])))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
