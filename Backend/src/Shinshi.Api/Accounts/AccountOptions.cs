// Project
using Shinshi.Data.Accounts;

namespace Shinshi.Backend.Accounts
{
    /// <summary> 플레이어 계정 설정. 운영은 기본값(600,000회)을 그대로 쓰고 테스트 호스트만 회수를 낮춘다 </summary>
    public sealed class AccountOptions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SECTION_NAME = "Accounts";

        /****************************************
        *              Properties
        ****************************************/

        // 새 해시의 PBKDF2 회수. 저장된 해시는 행에 적힌 회수로 검증하므로 올려도 기존 비밀번호가 그대로 맞는다
        public int PasswordIterations { get; set; } = AccountPasswordHasher.DEFAULT_ITERATIONS;
    }
}
