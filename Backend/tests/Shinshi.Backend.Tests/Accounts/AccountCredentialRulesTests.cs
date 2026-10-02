// Third Party
using Xunit;

// Project
using Shinshi.Contracts;

namespace Shinshi.Backend.Tests.Accounts
{
    /// <summary> 아이디, 비밀번호, 이메일 형식 규칙 표 검사 </summary>
    public sealed class AccountCredentialRulesTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData("abcd", "abcd")]
        [InlineData("Alice_01", "alice_01")]
        [InlineData("ABCDEFGHIJKLMNOP", "abcdefghijklmnop")]
        public void TryNormalizeLoginId_Valid_Lowercases(string input, string expected)
        {
            Assert.True(AccountCredentialRules.TryNormalizeLoginId(input, out string loginId));
            Assert.Equal(expected, loginId);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("abc")]
        [InlineData("abcdefghijklmnopq")]
        [InlineData("ab cd")]
        [InlineData("ab-cd")]
        [InlineData("앨리스앨리")]
        [InlineData("Kelvin")]
        [InlineData("ａｂｃｄ")]
        public void TryNormalizeLoginId_Invalid_IsRejected(string? input)
        {
            Assert.False(AccountCredentialRules.TryNormalizeLoginId(input!, out string loginId));
            Assert.Null(loginId);
        }

        [Theory]
        [InlineData("password1", "alice", true)]
        [InlineData("12345678a", "alice", true)]
        [InlineData("비밀번호는abc123", "alice", true)]
        [InlineData("passwor1", "alice", true)]
        [InlineData("passw1", "alice", false)]
        [InlineData("password", "alice", false)]
        [InlineData("12345678", "alice", false)]
        [InlineData("alice1234", "alice1234", false)]
        [InlineData("ALICE1234", "alice1234", false)]
        [InlineData("pass\tword1", "alice", false)]
        public void IsAcceptablePassword_Table(string password, string loginId, bool expected)
        {
            Assert.Equal(expected, AccountCredentialRules.IsAcceptablePassword(password, loginId));
        }

        [Fact]
        public void IsAcceptablePassword_LengthBounds()
        {
            Assert.True(AccountCredentialRules.IsAcceptablePassword(
                "a1" + new string('x', AccountCredentialRules.PASSWORD_MAX_LENGTH - 2), "alice"));
            Assert.False(AccountCredentialRules.IsAcceptablePassword(
                "a1" + new string('x', AccountCredentialRules.PASSWORD_MAX_LENGTH - 1), "alice"));
            Assert.False(AccountCredentialRules.IsAcceptablePassword(null!, "alice"));
        }

        [Theory]
        [InlineData("user@example.com", "user@example.com", "user@example.com")]
        [InlineData("  User.Name+tag@Example.CO.kr ", "User.Name+tag@Example.CO.kr", "user.name+tag@example.co.kr")]
        [InlineData("a@b.cd", "a@b.cd", "a@b.cd")]
        public void TryNormalizeEmail_Valid_TrimsAndLowercasesKey(string input, string expectedEmail, string expectedKey)
        {
            Assert.True(AccountCredentialRules.TryNormalizeEmail(input, out string email, out string key));
            Assert.Equal(expectedEmail, email);
            Assert.Equal(expectedKey, key);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("plain")]
        [InlineData("@example.com")]
        [InlineData("user@")]
        [InlineData("user@example")]
        [InlineData("user@@example.com")]
        [InlineData("us er@example.com")]
        [InlineData(".user@example.com")]
        [InlineData("us..er@example.com")]
        [InlineData("user@-example.com")]
        [InlineData("user@example..com")]
        [InlineData("사용자@example.com")]
        public void TryNormalizeEmail_Invalid_IsRejected(string? input)
        {
            Assert.False(AccountCredentialRules.TryNormalizeEmail(input!, out string email, out string key));
            Assert.Null(email);
            Assert.Null(key);
        }

        [Fact]
        public void TryNormalizeEmail_OverMaximumLength_IsRejected()
        {
            string domain = new string('a', 63) + "." + new string('b', 63) + "." + new string('c', 63) + ".com";
            string atLimit = new string('u', AccountCredentialRules.EMAIL_MAX_LENGTH - domain.Length - 1) + "@" + domain;

            Assert.True(AccountCredentialRules.TryNormalizeEmail(atLimit, out _, out _));
            Assert.False(AccountCredentialRules.TryNormalizeEmail("u" + atLimit, out _, out _));
        }
    }
}
