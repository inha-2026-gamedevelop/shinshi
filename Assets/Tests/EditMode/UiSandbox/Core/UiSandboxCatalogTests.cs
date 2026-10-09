using System;
using System.Collections.Generic;
using NUnit.Framework;
using Shinshi.Editor;

namespace Shinshi.Tests
{
    public sealed class UiSandboxCatalogTests
    {
        private const string ID_PREFIX = "catalogtest_";

        private class FakePage : IUiSandboxPage
        {
            private readonly string _id;

            public FakePage(string id)
            {
                _id = id;
            }

            public string Id { get { return _id; } }
            public string Title { get { return _id; } }
            public string SourceScenePath { get { return string.Empty; } }
            public string Requirement { get { return "테스트용 요구 사항"; } }
            public bool HasView { get { return false; } }

            public void Attach(UiSandboxContext context) { }
            public void Apply(UiSandboxContext context) { }
            public void DrawControls(UiSandboxContext context) { }
            public void Detach() { }
        }

        private sealed class OtherFakePage : FakePage
        {
            public OtherFakePage(string id) : base(id) { }
        }

        private readonly List<string> _registered = new List<string>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _registered.Count; ++i)
            {
                UiSandboxCatalog.Unregister(_registered[i]);
            }

            _registered.Clear();
        }

        private void RegisterTracked(IUiSandboxPage page)
        {
            UiSandboxCatalog.Register(page);
            _registered.Add(page.Id);
        }

        [Test]
        public void Register_NewPage_AppearsInPagesAndFind()
        {
            FakePage page = new FakePage(ID_PREFIX + "a");

            RegisterTracked(page);

            Assert.That(ToList(UiSandboxCatalog.Pages), Does.Contain(page));
            Assert.AreSame(page, UiSandboxCatalog.Find(ID_PREFIX + "a"));
        }

        [Test]
        public void Register_DuplicateId_ThrowsNamingBothTypes()
        {
            RegisterTracked(new FakePage(ID_PREFIX + "dup"));

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => UiSandboxCatalog.Register(new OtherFakePage(ID_PREFIX + "dup")));

            StringAssert.Contains(ID_PREFIX + "dup", exception.Message);
            StringAssert.Contains(nameof(FakePage), exception.Message);
            StringAssert.Contains(nameof(OtherFakePage), exception.Message);
        }

        [Test]
        public void Register_DuplicateIdDifferentCase_Throws()
        {
            RegisterTracked(new FakePage(ID_PREFIX + "Case"));

            Assert.Throws<ArgumentException>(() => UiSandboxCatalog.Register(new FakePage(ID_PREFIX + "CASE")));
        }

        [Test]
        public void Register_DuplicateId_KeepsFirstPageOnly()
        {
            FakePage first = new FakePage(ID_PREFIX + "keep");
            RegisterTracked(first);

            Assert.IsFalse(UiSandboxCatalog.TryRegister(new OtherFakePage(ID_PREFIX + "keep"), out string error));

            StringAssert.Contains(ID_PREFIX + "keep", error);
            Assert.AreSame(first, UiSandboxCatalog.Find(ID_PREFIX + "keep"));
        }

        [Test]
        public void Register_NullPage_Throws()
        {
            Assert.Throws<ArgumentException>(() => UiSandboxCatalog.Register(null));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void Register_EmptyId_Throws(string id)
        {
            Assert.Throws<ArgumentException>(() => UiSandboxCatalog.Register(new FakePage(id)));
        }

        [Test]
        public void Unregister_ExistingId_RemovesPage()
        {
            RegisterTracked(new FakePage(ID_PREFIX + "gone"));

            Assert.IsTrue(UiSandboxCatalog.Unregister(ID_PREFIX + "gone"));
            Assert.IsNull(UiSandboxCatalog.Find(ID_PREFIX + "gone"));
        }

        [Test]
        public void Unregister_UnknownId_ReturnsFalse()
        {
            Assert.IsFalse(UiSandboxCatalog.Unregister(ID_PREFIX + "never"));
        }

        [Test]
        public void Register_AfterUnregister_SameIdAllowedAgain()
        {
            RegisterTracked(new FakePage(ID_PREFIX + "again"));
            UiSandboxCatalog.Unregister(ID_PREFIX + "again");

            Assert.DoesNotThrow(() => RegisterTracked(new FakePage(ID_PREFIX + "again")));
        }

        [Test]
        public void Changed_RegisterAndUnregister_RaisedEachTime()
        {
            int raised = 0;
            Action handler = () => ++raised;
            UiSandboxCatalog.Changed += handler;
            try
            {
                RegisterTracked(new FakePage(ID_PREFIX + "event"));
                UiSandboxCatalog.Unregister(ID_PREFIX + "event");
            }
            finally
            {
                UiSandboxCatalog.Changed -= handler;
            }

            Assert.AreEqual(2, raised);
        }

        [Test]
        public void Changed_FailedRegister_NotRaised()
        {
            RegisterTracked(new FakePage(ID_PREFIX + "quiet"));
            int raised = 0;
            Action handler = () => ++raised;
            UiSandboxCatalog.Changed += handler;
            try
            {
                UiSandboxCatalog.TryRegister(new FakePage(ID_PREFIX + "quiet"), out _);
            }
            finally
            {
                UiSandboxCatalog.Changed -= handler;
            }

            Assert.AreEqual(0, raised);
        }

        [TestCase("")]
        [TestCase(null)]
        public void Find_EmptyId_ReturnsNull(string id)
        {
            Assert.IsNull(UiSandboxCatalog.Find(id));
        }

        private static List<IUiSandboxPage> ToList(IReadOnlyList<IUiSandboxPage> pages)
        {
            return new List<IUiSandboxPage>(pages);
        }
    }
}
