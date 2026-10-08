using System.Collections.Generic;
using Mirror;
using NUnit.Framework;
using Shinshi.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Shinshi.Tests
{
    public sealed class UiSandboxSceneSanitizerTests
    {
        private const string TRANSFORM_TYPE = "UnityEngine.Transform, UnityEngine.CoreModule";

        private UiSandboxTestScene _scene = null;

        [SetUp]
        public void SetUp()
        {
            _scene = UiSandboxTestScene.CreatePreview();
        }

        [TearDown]
        public void TearDown()
        {
            if (_scene != null)
            {
                _scene.Dispose();
                _scene = null;
            }
        }

        private GameObject Spawn(string name, GameObject parent = null)
        {
            return _scene.Spawn(name, parent);
        }

        private static void AddPersistentCall(Button button, UnityEngine.Object target, string typeName, string methodName)
        {
            SerializedObject serialized = new SerializedObject(button);
            SerializedProperty calls = serialized.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            int index = calls.arraySize;
            calls.arraySize = index + 1;

            SerializedProperty call = calls.GetArrayElementAtIndex(index);
            call.FindPropertyRelative("m_Target").objectReferenceValue = target;
            call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = typeName;
            call.FindPropertyRelative("m_MethodName").stringValue = methodName;
            call.FindPropertyRelative("m_Mode").intValue = 1;
            call.FindPropertyRelative("m_CallState").intValue = 2;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void Sanitize_NullRoots_ReturnsEmptyReport()
        {
            UiSandboxSanitizeReport report = UiSandboxSceneSanitizer.Sanitize(null);

            Assert.AreEqual(0, report.RemovedComponents);
            Assert.AreEqual(0, report.StrippedPersistentCalls);
            Assert.AreEqual(0, report.RemovedObjects);
            Assert.AreEqual(0, report.RemainingRoots.Length);
            Assert.IsFalse(report.HasWarnings);
        }

        [Test]
        public void Sanitize_SafeHierarchy_ChangesNothing()
        {
            GameObject root = Spawn("root");
            root.AddComponent<BoxCollider>();

            UiSandboxSanitizeReport report = UiSandboxSceneSanitizer.Sanitize(new[] { root });

            Assert.AreEqual(0, report.RemovedComponents);
            Assert.AreEqual(0, report.StrippedPersistentCalls);
            Assert.AreEqual(0, report.RemovedObjects);
            Assert.IsTrue(root != null);
            Assert.AreEqual(1, report.RemainingRoots.Length);
        }

        [Test]
        public void Sanitize_RootWithOnlyNetworkComponent_RemovesComponentAndEmptyObject()
        {
            GameObject root = Spawn("root");
            root.AddComponent<NetworkIdentity>();

            UiSandboxSanitizeReport report = UiSandboxSceneSanitizer.Sanitize(new[] { root });

            Assert.AreEqual(1, report.RemovedComponents);
            Assert.AreEqual(1, report.RemovedObjects);
            Assert.IsTrue(root == null);
            Assert.AreEqual(0, report.RemainingRoots.Length);
        }

        [Test]
        public void Sanitize_NetworkComponentOnObjectWithOtherComponents_KeepsObject()
        {
            GameObject root = Spawn("root");
            GameObject child = Spawn("child", root);
            BoxCollider collider = child.AddComponent<BoxCollider>();
            child.AddComponent<NetworkIdentity>();

            UiSandboxSanitizeReport report = UiSandboxSceneSanitizer.Sanitize(new[] { root });

            Assert.AreEqual(1, report.RemovedComponents);
            Assert.AreEqual(0, report.RemovedObjects);
            Assert.IsNull(child.GetComponent<NetworkIdentity>());
            Assert.IsTrue(collider != null);
        }

        [Test]
        public void Sanitize_NetworkComponentInInactiveChild_IsRemoved()
        {
            GameObject root = Spawn("root");
            root.AddComponent<BoxCollider>();
            GameObject child = Spawn("child", root);
            child.AddComponent<BoxCollider>();
            child.AddComponent<NetworkIdentity>();
            child.SetActive(false);

            UiSandboxSceneSanitizer.Sanitize(new[] { root });

            Assert.IsNull(child.GetComponent<NetworkIdentity>());
        }

        [Test]
        public void Sanitize_HudChatView_IsRemovedBecauseItSendsChatDirectly()
        {
            GameObject root = Spawn("root");
            root.AddComponent<BoxCollider>();
            GameObject chat = Spawn("chat", root);
            chat.AddComponent<BoxCollider>();
            chat.AddComponent<Shinshi.UI.HudChatView>();

            UiSandboxSanitizeReport report = UiSandboxSceneSanitizer.Sanitize(new[] { root });

            Assert.AreEqual(1, report.RemovedComponents);
            Assert.IsNull(chat.GetComponent<Shinshi.UI.HudChatView>());
            Assert.AreEqual(0, UiSandboxSceneSanitizer.FindUnsafeComponents(new[] { root }).Count);
        }

        [Test]
        public void Sanitize_ButtonWithUnsafePersistentCalls_StripsOnlyUnsafeOnes()
        {
            GameObject root = Spawn("root");
            GameObject target = Spawn("target", root);
            Button button = root.AddComponent<Button>();
            AddPersistentCall(button, target.transform, TRANSFORM_TYPE, "SetParent");
            AddPersistentCall(button, target.transform, TRANSFORM_TYPE, "LoadScene");
            AddPersistentCall(button, null, TRANSFORM_TYPE, "Anything");
            Assert.AreEqual(3, button.onClick.GetPersistentEventCount());

            UiSandboxSanitizeReport report = UiSandboxSceneSanitizer.Sanitize(new[] { root });

            Assert.AreEqual(2, report.StrippedPersistentCalls);
            Assert.AreEqual(1, button.onClick.GetPersistentEventCount());
            Assert.AreEqual("SetParent", button.onClick.GetPersistentMethodName(0));
        }

        [Test]
        public void Sanitize_Twice_SecondRunFindsNothing()
        {
            GameObject root = Spawn("root");
            root.AddComponent<BoxCollider>();
            root.AddComponent<NetworkIdentity>();
            UiSandboxSceneSanitizer.Sanitize(new[] { root });

            UiSandboxSanitizeReport second = UiSandboxSceneSanitizer.Sanitize(new[] { root });

            Assert.AreEqual(0, second.RemovedComponents);
            Assert.AreEqual(0, second.StrippedPersistentCalls);
            Assert.AreEqual(0, second.RemovedObjects);
        }

        [Test]
        public void Sanitize_Removal_RecordsDetailWithTypeName()
        {
            GameObject root = Spawn("root");
            root.AddComponent<BoxCollider>();
            root.AddComponent<NetworkIdentity>();

            UiSandboxSanitizeReport report = UiSandboxSceneSanitizer.Sanitize(new[] { root });

            Assert.IsTrue(report.Details.Exists(line => line.Contains(nameof(NetworkIdentity))));
        }

        [Test]
        public void FindUnsafeComponents_FindsInactiveChildrenAndSkipsSafeOnes()
        {
            GameObject root = Spawn("root");
            root.AddComponent<BoxCollider>();
            GameObject child = Spawn("child", root);
            NetworkIdentity identity = child.AddComponent<NetworkIdentity>();
            child.SetActive(false);

            List<Component> found = UiSandboxSceneSanitizer.FindUnsafeComponents(new[] { root });

            Assert.AreEqual(1, found.Count);
            Assert.AreSame(identity, found[0]);
        }

        [Test]
        public void FindUnsafeComponents_NullInput_ReturnsEmpty()
        {
            Assert.AreEqual(0, UiSandboxSceneSanitizer.FindUnsafeComponents(null).Count);
        }
    }
}
