// System
using System;
using System.Net;
using System.Net.Sockets;

namespace Shinshi.Network
{
    /// <summary> 인증 대기 주소별 상한을 셀 때 쓰는 주소 묶음 키, IPv4는 주소 하나, IPv6는 /64 접두사 하나다 </summary>
    public readonly struct AuthAddressKey : IEquatable<AuthAddressKey>
    {
        // IPv6는 가정 회선 하나가 /64 이상을 받으므로 주소 하나로 세면 접두사 안에서 주소만 바꿔 상한을 우회한다.
        // IPv4-mapped IPv6(::ffff:a.b.c.d)는 듀얼 스택 소켓이 IPv4 연결을 그렇게 적는 것이라 IPv4와 같은 키로 본다.
        // 읽지 못한 주소는 모두 한 키로 묶는다(가장 엄격한 쪽). 표가 직접 넣는 끊기 대기 항목은 None이라 어디에도 세지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const int IPV6_BYTES        = 16;
        private const int IPV4_BYTES        = 4;
        private const int PREFIX_64_BYTES   = 8;
        private const int BITS_PER_BYTE     = 8;
        private const int HASH_MULTIPLIER   = 397; // 주소 종류와 주소 값의 해시를 섞는 고정 배수

        private readonly EKind _kind;
        private readonly ulong _value;

        /****************************************
        *              Properties
        ****************************************/

        // 표가 끊기 대기로 직접 넣은 항목, 주소별 상한에 세지 않는다
        public static AuthAddressKey None => default;

        public bool IsNone => _kind == EKind.None;

        /****************************************
        *                Methods
        ****************************************/

        private AuthAddressKey(EKind kind, ulong value)
        {
            _kind  = kind;
            _value = value;
        }

        /// <summary> 전송 계층이 준 주소 문자열을 키로 바꾼다, 연결마다 한 번 부른다(틱 경로가 아니다) </summary>
        public static AuthAddressKey FromAddress(string address)
        {
            if ((string.IsNullOrEmpty(address)) || (!IPAddress.TryParse(address, out IPAddress parsed)))
            {
                return new AuthAddressKey(EKind.Unknown, 0UL);
            }

            if (parsed.AddressFamily == AddressFamily.InterNetwork)
            {
                return FromIPv4(parsed);
            }

            if (parsed.AddressFamily != AddressFamily.InterNetworkV6)
            {
                return new AuthAddressKey(EKind.Unknown, 0UL);
            }

            if (parsed.IsIPv4MappedToIPv6)
            {
                return FromIPv4(parsed.MapToIPv4());
            }

            // 16바이트 고정이라 스택 버퍼로 읽는다
            Span<byte> bytes = stackalloc byte[IPV6_BYTES];
            if ((!parsed.TryWriteBytes(bytes, out int written)) || (written != IPV6_BYTES))
            {
                return new AuthAddressKey(EKind.Unknown, 0UL);
            }

            return new AuthAddressKey(EKind.IPv6Prefix64, ReadBigEndian(bytes.Slice(0, PREFIX_64_BYTES)));
        }

        public bool Equals(AuthAddressKey other)
        {
            return (_kind == other._kind) && (_value == other._value);
        }

        public override bool Equals(object obj)
        {
            return (obj is AuthAddressKey other) && (Equals(other));
        }

        public override int GetHashCode()
        {
            return ((int)_kind * HASH_MULTIPLIER) ^ _value.GetHashCode();
        }

        private static AuthAddressKey FromIPv4(IPAddress address)
        {
            Span<byte> bytes = stackalloc byte[IPV4_BYTES];
            if ((!address.TryWriteBytes(bytes, out int written)) || (written != IPV4_BYTES))
            {
                return new AuthAddressKey(EKind.Unknown, 0UL);
            }

            return new AuthAddressKey(EKind.IPv4, ReadBigEndian(bytes));
        }

        private static ulong ReadBigEndian(ReadOnlySpan<byte> bytes)
        {
            ulong value = 0UL;
            for (int i = 0; i < bytes.Length; ++i)
            {
                value = (value << BITS_PER_BYTE) | bytes[i];
            }

            return value;
        }

        /****************************************
        *                Kind
        ****************************************/

        private enum EKind : byte
        {
            None         = 0,
            Unknown      = 1,
            IPv4         = 2,
            IPv6Prefix64 = 3
        }
    }
}
