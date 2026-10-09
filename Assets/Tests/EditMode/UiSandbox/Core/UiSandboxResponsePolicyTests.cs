using NUnit.Framework;
using Shinshi.Editor;
using UnityEngine;

namespace Shinshi.Tests
{
    public sealed class UiSandboxResponsePolicyTests
    {
        private UiSandboxScenarioSO _scenario = null;

        [SetUp]
        public void SetUp()
        {
            _scenario = ScriptableObject.CreateInstance<UiSandboxScenarioSO>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_scenario);
            _scenario = null;
        }

        [Test]
        public void FromScenario_NullScenario_ReturnsImmediateSuccess()
        {
            UiSandboxResponsePolicy policy = UiSandboxResponsePolicy.FromScenario(null);

            Assert.IsTrue(policy.IsSuccess);
            Assert.AreEqual(0.0f, policy.DelaySeconds);
            Assert.AreEqual(string.Empty, policy.Error);
        }

        [Test]
        public void FromScenario_EmptyScenario_ReturnsImmediateSuccess()
        {
            UiSandboxResponsePolicy policy = UiSandboxResponsePolicy.FromScenario(_scenario);

            Assert.AreEqual(EUiSandboxResponseMode.Success, policy.Mode);
            Assert.AreEqual(0.0f, policy.DelaySeconds);
        }

        [TestCase("Rejected", EUiSandboxResponseMode.Rejected)]
        [TestCase("rejected", EUiSandboxResponseMode.Rejected)]
        [TestCase("  Disconnected ", EUiSandboxResponseMode.Disconnected)]
        [TestCase("TIMEOUT", EUiSandboxResponseMode.Timeout)]
        [TestCase("Success", EUiSandboxResponseMode.Success)]
        public void FromScenario_ModeText_ParsesIgnoringCaseAndSpaces(string text, EUiSandboxResponseMode expected)
        {
            _scenario.SetString(UiSandboxScenarioKeys.RESPONSE_MODE, text);

            Assert.AreEqual(expected, UiSandboxResponsePolicy.FromScenario(_scenario).Mode);
        }

        [TestCase("Explode")]
        [TestCase("99")]
        [TestCase("-1")]
        [TestCase("")]
        public void FromScenario_InvalidMode_FallsBackToSuccess(string text)
        {
            _scenario.SetString(UiSandboxScenarioKeys.RESPONSE_MODE, text);

            Assert.AreEqual(EUiSandboxResponseMode.Success, UiSandboxResponsePolicy.FromScenario(_scenario).Mode);
        }

        [Test]
        public void FromScenario_DelayValue_IsReadFromScenario()
        {
            _scenario.SetFloat(UiSandboxScenarioKeys.RESPONSE_DELAY, 2.5f);

            Assert.AreEqual(2.5f, UiSandboxResponsePolicy.FromScenario(_scenario).DelaySeconds);
        }

        [Test]
        public void Constructor_NegativeOrNaNDelay_BecomesZero()
        {
            Assert.AreEqual(0.0f, new UiSandboxResponsePolicy(EUiSandboxResponseMode.Success, -3.0f, null).DelaySeconds);
            Assert.AreEqual(0.0f, new UiSandboxResponsePolicy(EUiSandboxResponseMode.Success, float.NaN, null).DelaySeconds);
        }

        [Test]
        public void Constructor_HugeOrInfiniteDelay_IsCappedAtMaximum()
        {
            Assert.AreEqual(UiSandboxScenarioKeys.MAX_DELAY_SECONDS,
                new UiSandboxResponsePolicy(EUiSandboxResponseMode.Success, 1e9f, null).DelaySeconds);
            Assert.AreEqual(UiSandboxScenarioKeys.MAX_DELAY_SECONDS,
                new UiSandboxResponsePolicy(EUiSandboxResponseMode.Success, float.PositiveInfinity, null).DelaySeconds);
        }

        [Test]
        public void Constructor_NullError_BecomesEmptyString()
        {
            Assert.AreEqual(string.Empty, new UiSandboxResponsePolicy(EUiSandboxResponseMode.Rejected, 0.0f, null).Error);
        }

        [TestCase(EUiSandboxResponseMode.Rejected, UiSandboxResponsePolicy.DEFAULT_REJECTED)]
        [TestCase(EUiSandboxResponseMode.Disconnected, UiSandboxResponsePolicy.DEFAULT_DISCONNECTED)]
        [TestCase(EUiSandboxResponseMode.Timeout, UiSandboxResponsePolicy.DEFAULT_TIMEOUT)]
        [TestCase(EUiSandboxResponseMode.Success, "")]
        public void ResolveFailureMessage_NoCustomError_UsesModeDefault(EUiSandboxResponseMode mode, string expected)
        {
            Assert.AreEqual(expected, new UiSandboxResponsePolicy(mode, 0.0f, string.Empty).ResolveFailureMessage());
        }

        [Test]
        public void ResolveFailureMessage_CustomError_TakesPrecedence()
        {
            UiSandboxResponsePolicy policy = new UiSandboxResponsePolicy(EUiSandboxResponseMode.Timeout, 0.0f, "직접 쓴 문구");

            Assert.AreEqual("직접 쓴 문구", policy.ResolveFailureMessage());
        }

        [Test]
        public void ResolveFailureMessage_WhitespaceError_UsesModeDefault()
        {
            UiSandboxResponsePolicy policy = new UiSandboxResponsePolicy(EUiSandboxResponseMode.Rejected, 0.0f, "   ");

            Assert.AreEqual(UiSandboxResponsePolicy.DEFAULT_REJECTED, policy.ResolveFailureMessage());
        }

        [Test]
        public void IsSuccess_OnlyTrueForSuccessMode()
        {
            Assert.IsTrue(new UiSandboxResponsePolicy(EUiSandboxResponseMode.Success, 0.0f, null).IsSuccess);
            Assert.IsFalse(new UiSandboxResponsePolicy(EUiSandboxResponseMode.Rejected, 0.0f, null).IsSuccess);
            Assert.IsFalse(new UiSandboxResponsePolicy(EUiSandboxResponseMode.Timeout, 0.0f, null).IsSuccess);
        }

        [Test]
        public void ToString_ContainsModeName()
        {
            StringAssert.Contains("Timeout", new UiSandboxResponsePolicy(EUiSandboxResponseMode.Timeout, 1.5f, null).ToString());
        }
    }
}
