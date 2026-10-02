// System
using System;

namespace Shinshi.Data.Admin
{
    /// <summary> 운영자 비밀번호 해시 회수와 어드민 세션 수명. 범위를 벗어난 설정은 생성할 때 거절한다 </summary>
    public sealed class AdminSecuritySettings
    {
        /****************************************
        *                Fields
        ****************************************/

        // OWASP 2023 PBKDF2-HMAC-SHA256 권장 회수. 테스트만 설정으로 낮춘다
        public const int DEFAULT_PASSWORD_ITERATIONS = 600000;

        // DB CHECK와 같은 하한. 이보다 낮으면 해시가 무차별 대입을 늦추지 못한다
        public const int MIN_PASSWORD_ITERATIONS     = 1000;
        public const int MAX_PASSWORD_ITERATIONS     = 10000000;

        public const int DEFAULT_SESSION_MINUTES = 30;
        public const int MIN_SESSION_MINUTES     = 1;

        // 근무 한 번을 넘는 토큰을 만들지 않는다
        public const int MAX_SESSION_MINUTES     = 480;

        /****************************************
        *              Properties
        ****************************************/

        public int PasswordIterations { get; }
        public TimeSpan SessionLifetime { get; }

        /****************************************
        *                Methods
        ****************************************/

        public AdminSecuritySettings(int passwordIterations, int sessionMinutes)
        {
            if ((passwordIterations < MIN_PASSWORD_ITERATIONS) || (passwordIterations > MAX_PASSWORD_ITERATIONS))
            {
                throw new ArgumentOutOfRangeException(nameof(passwordIterations));
            }
            if ((sessionMinutes < MIN_SESSION_MINUTES) || (sessionMinutes > MAX_SESSION_MINUTES))
            {
                throw new ArgumentOutOfRangeException(nameof(sessionMinutes));
            }

            PasswordIterations = passwordIterations;
            SessionLifetime    = TimeSpan.FromMinutes(sessionMinutes);
        }
    }
}
