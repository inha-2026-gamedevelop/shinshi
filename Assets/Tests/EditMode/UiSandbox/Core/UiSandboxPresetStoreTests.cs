using NUnit.Framework;
using Shinshi.Editor;
using UnityEngine;

namespace Shinshi.Tests
{
    public sealed class UiSandboxPresetStoreTests
    {
        [TestCase("Assets/Presets/UiSandbox/Login.asset", true)]
        [TestCase("Assets\\Presets\\UiSandbox\\Login.asset", true)]
        [TestCase("assets/Presets/Login.ASSET", true)]
        [TestCase("Assets/Presets/Login.txt", false)]
        [TestCase("Assets/.asset", false)]
        [TestCase("Packages/com.example/Login.asset", false)]
        [TestCase("Assets/../Login.asset", false)]
        [TestCase("Assets//Login.asset", false)]
        [TestCase("Assets/Presets/./Login.asset", false)]
        [TestCase("C:/Assets/Login.asset", false)]
        [TestCase("Assets/00.Scenes/Dev_UiSandbox/Login.asset", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void ValidatePath_Path_ReturnsExpected(string path, bool expected)
        {
            bool valid = UiSandboxPresetStore.ValidatePath(path, out string error);

            Assert.AreEqual(expected, valid);
            Assert.AreEqual(expected, string.IsNullOrEmpty(error));
        }

        [Test]
        public void Load_InvalidPath_ReturnsNull()
        {
            Assert.IsNull(UiSandboxPresetStore.Load("../outside.asset"));
            Assert.IsNull(UiSandboxPresetStore.Load(string.Empty));
        }

        [Test]
        public void Load_MissingAsset_ReturnsNull()
        {
            Assert.IsNull(UiSandboxPresetStore.Load("Assets/__UiSandboxNoSuchFolder__/Missing.asset"));
        }

        [Test]
        public void SaveAs_NullScenario_ReturnsFalseWithError()
        {
            bool saved = UiSandboxPresetStore.SaveAs(null, "Assets/Presets/Login.asset", false, out string error);

            Assert.IsFalse(saved);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void SaveAs_InvalidPath_ReturnsFalseWithError()
        {
            UiSandboxScenarioSO scenario = ScriptableObject.CreateInstance<UiSandboxScenarioSO>();
            try
            {
                bool saved = UiSandboxPresetStore.SaveAs(scenario, "Packages/x/Login.asset", false, out string error);

                Assert.IsFalse(saved);
                Assert.IsNotEmpty(error);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(scenario);
            }
        }

        [Test]
        public void SaveAs_MissingFolder_ReturnsFalseWithoutCreatingAsset()
        {
            UiSandboxScenarioSO scenario = ScriptableObject.CreateInstance<UiSandboxScenarioSO>();
            try
            {
                string path = "Assets/__UiSandboxNoSuchFolder__/Login.asset";

                bool saved = UiSandboxPresetStore.SaveAs(scenario, path, true, out string error);

                Assert.IsFalse(saved);
                StringAssert.Contains("폴더", error);
                Assert.IsNull(UiSandboxPresetStore.Load(path));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(scenario);
            }
        }
    }
}
