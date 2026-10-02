// System
using System;

// Third Party
using Xunit;

// Project
using Shinshi.Data.Accounts;

namespace Shinshi.Backend.Tests.Accounts
{
    /// <summary> PBKDF2 저장 형식, 기본 회수, 솔트 길이, 비교와 더미 경로 </summary>
    public sealed class AccountPasswordHasherTests
    {
        /****************************************
        *                Fields
        ****************************************/

        // 형식 검사용 낮은 회수. 기본 회수는 따로 한 번만 검사한다
        internal const int TEST_ITERATIONS = 1000;
        private const string PASSWORD = "correct1horse";

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void Hash_DefaultHasher_Uses600000IterationsSixteenByteSaltAndThirtyTwoByteKey()
        {
            AccountPasswordHasher hasher = new AccountPasswordHasher();

            string encoded = hasher.Hash(PASSWORD);

            string[] parts = encoded.Split('$');
            Assert.Equal(4, parts.Length);
            Assert.Equal("pbkdf2-sha256", parts[0]);
            Assert.Equal("600000", parts[1]);
            Assert.Equal(AccountPasswordHasher.SALT_BYTES, Convert.FromBase64String(parts[2]).Length);
            Assert.Equal(AccountPasswordHasher.HASH_BYTES, Convert.FromBase64String(parts[3]).Length);
            Assert.True(hasher.Verify(PASSWORD, encoded));
            Assert.DoesNotContain(PASSWORD, encoded, StringComparison.Ordinal);
        }

        [Fact]
        public void Verify_WrongPasswordOrTamperedHash_ReturnsFalse()
        {
            AccountPasswordHasher hasher = new AccountPasswordHasher(TEST_ITERATIONS);
            string encoded = hasher.Hash(PASSWORD);

            Assert.False(hasher.Verify("wrong1horse", encoded));
            Assert.False(hasher.Verify(null, encoded));
            Assert.False(hasher.Verify(PASSWORD, encoded.Replace("pbkdf2-sha256", "pbkdf2-sha1")));
            Assert.False(hasher.Verify(PASSWORD, "pbkdf2-sha256$1000$AAAA$AAAA"));
            Assert.False(hasher.Verify(PASSWORD, string.Empty));
            Assert.False(hasher.Verify(PASSWORD + new string('x', 100), encoded));
        }

        [Fact]
        public void Hash_SamePasswordTwice_UsesDifferentSalts()
        {
            AccountPasswordHasher hasher = new AccountPasswordHasher(TEST_ITERATIONS);

            Assert.NotEqual(hasher.Hash(PASSWORD), hasher.Hash(PASSWORD));
        }

        [Fact]
        public void Verify_StoredIterationCount_IsUsedInsteadOfCurrentSetting()
        {
            string older = new AccountPasswordHasher(TEST_ITERATIONS).Hash(PASSWORD);
            AccountPasswordHasher current = new AccountPasswordHasher(TEST_ITERATIONS * 2);

            Assert.True(current.Verify(PASSWORD, older));
        }

        [Fact]
        public void VerifyDummy_AlwaysFalse()
        {
            AccountPasswordHasher hasher = new AccountPasswordHasher(TEST_ITERATIONS);

            Assert.False(hasher.VerifyDummy(PASSWORD));
            Assert.False(hasher.VerifyDummy(null));
        }

        [Fact]
        public void Constructor_IterationsOutOfRange_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AccountPasswordHasher(AccountPasswordHasher.MIN_ITERATIONS - 1));
        }
    }
}
