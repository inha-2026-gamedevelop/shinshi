// System
using System;

// Project
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> Admin 설정 섹션. TOTP 암호화 키는 환경이나 비밀 저장소로만 주입하며 기본값이 없다 </summary>
    public sealed class AdminOptions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SECTION_NAME = "Admin";

        /****************************************
        *              Properties
        ****************************************/

        // Admin__TotpKeyBase64. 32바이트 무작위 키의 Base64. 바꾸면 기존 운영자의 TOTP를 풀 수 없어 모두 재발급해야 한다
        public string TotpKeyBase64 { get; set; } = string.Empty;

        // Admin__AllowedNetworks__0 = "10.0.0.0/8" 처럼 CIDR이나 단일 IP. 비어 있으면 루프백만 허용한다
        public string[] AllowedNetworks { get; set; } = Array.Empty<string>();

        public int SessionLifetimeMinutes { get; set; } = AdminSecuritySettings.DEFAULT_SESSION_MINUTES;

        // 테스트만 낮춘다. 저장된 해시는 행마다 자기 회수로 검증하므로 바꿔도 기존 비밀번호는 유지된다
        public int PasswordIterations { get; set; } = AdminSecuritySettings.DEFAULT_PASSWORD_ITERATIONS;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 범위를 벗어나면 설정 키만 담은 오류로 시작이나 첫 어드민 요청을 실패시킨다 </summary>
        public AdminSecuritySettings CreateSecuritySettings()
        {
            try
            {
                return new AdminSecuritySettings(PasswordIterations, SessionLifetimeMinutes);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new InvalidOperationException(
                    "Admin:PasswordIterations or Admin:SessionLifetimeMinutes is outside the supported range.");
            }
        }

        public AdminSecretProtector CreateSecretProtector()
        {
            return AdminSecretProtector.FromBase64(TotpKeyBase64);
        }
    }
}
