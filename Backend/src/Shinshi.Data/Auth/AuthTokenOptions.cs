// System
using System;

namespace Shinshi.Data.Auth
{
    /// <summary> 환경에서만 주입하는 토큰 해시 비밀키이며 기본 비밀값을 제공하지 않는다 </summary>
    public sealed class AuthTokenOptions
    {
        /****************************************
        *              Properties
        ****************************************/

        public string HashKeyBase64 { get; set; } = string.Empty;

        /****************************************
        *                Methods
        ****************************************/

        public byte[] GetValidatedHashKey()
        {
            const int MINIMUM_KEY_BYTES = 32;
            byte[] key;
            try
            {
                key = Convert.FromBase64String(HashKeyBase64);
            }
            catch (FormatException)
            {
                throw new ArgumentException("Auth hash key must contain a base64 encoded random key.");
            }

            if (key.Length < MINIMUM_KEY_BYTES)
            {
                throw new ArgumentException("Auth hash key must contain at least 32 random bytes.");
            }

            return key;
        }
    }
}
