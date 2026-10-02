// Third Party
using Xunit;

// Project
using Shinshi.Contracts;

namespace Shinshi.Backend.Tests.Accounts
{
    /// <summary> 캐릭터 이름 처리 순서(공백 제거 -> NFKC -> 문자, 길이 검사 -> 소문자 키) 표 검사 </summary>
    public sealed class CharacterNameRulesTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData("신시", "신시", "신시")]
        [InlineData("Fox12", "Fox12", "fox12")]
        [InlineData("  여우Fox  ", "여우Fox", "여우fox")]
        [InlineData("　여우　", "여우", "여우")]
        [InlineData("ＦＯＸ１２", "FOX12", "fox12")]
        [InlineData("ｆｏｘ", "fox", "fox")]
        [InlineData("가나다라마바사아자차카타", "가나다라마바사아자차카타", "가나다라마바사아자차카타")]
        [InlineData("ab", "ab", "ab")]
        public void TryNormalize_ValidInput_ReturnsNameAndKey(string input, string expectedName, string expectedKey)
        {
            Assert.True(CharacterNameRules.TryNormalize(input, out string name, out string key));
            Assert.Equal(expectedName, name);
            Assert.Equal(expectedKey, key);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("가")]
        [InlineData("a")]
        [InlineData("가나다라마바사아자차카타파")]
        [InlineData("fox fox")]
        [InlineData("fox_1")]
        [InlineData("여우!")]
        [InlineData("ㄱㄴ")]
        [InlineData("ｱｲｳ")]
        [InlineData("日本")]
        [InlineData("café")]
        public void TryNormalize_InvalidInput_IsRejected(string? input)
        {
            Assert.False(CharacterNameRules.TryNormalize(input!, out string name, out string key));
            Assert.Null(name);
            Assert.Null(key);
        }

        [Fact]
        public void TryNormalize_LoneSurrogate_IsRejectedWithoutThrowing()
        {
            string input = new string(new[] { '\ud800', 'a', 'b' });

            Assert.False(CharacterNameRules.TryNormalize(input, out _, out _));
        }

        [Fact]
        public void TryNormalize_OverlongInputBeforeNormalization_IsRejected()
        {
            string input = new string('a', CharacterNameRules.MAX_INPUT_LENGTH + 1);

            Assert.False(CharacterNameRules.TryNormalize(input, out _, out _));
        }

        [Fact]
        public void TryNormalize_FullwidthAndCaseVariants_ShareOneKey()
        {
            Assert.True(CharacterNameRules.TryNormalize("Shinshi", out _, out string plain));
            Assert.True(CharacterNameRules.TryNormalize("ＳＨＩＮＳＨＩ", out _, out string fullwidth));
            Assert.True(CharacterNameRules.TryNormalize("sHiNsHi", out _, out string mixed));

            Assert.Equal(plain, fullwidth);
            Assert.Equal(plain, mixed);
        }

        [Fact]
        public void TryNormalize_CompatibilityExpansionPastMaximum_IsRejected()
        {
            // ㎏(U+338F)는 NFKC에서 kg 두 글자가 된다. 정규화 뒤 길이로 판정한다
            string input = "abcdefghijk" + "㎏";

            Assert.False(CharacterNameRules.TryNormalize(input, out _, out _));
            Assert.True(CharacterNameRules.TryNormalize("abcdefghij" + "㎏", out string name, out _));
            Assert.Equal("abcdefghijkg", name);
        }
    }
}
