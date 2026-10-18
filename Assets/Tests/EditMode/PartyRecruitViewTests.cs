// Unity
using UnityEngine;

using UnityEditor;

// 서드파티
using NUnit.Framework;
using TMPro;

// 프로젝트 내부
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class PartyRecruitViewTests
    {
        private const string KOREAN_FONT_PATH = "Assets/01.Scripts/06.UI/Hud/Fonts/HudQuestKorean-Regular.asset";

        private GameObject       _host = null;
        private PartyRecruitView _view = null;

        private static PartyRoomSummary Room(int id, string name)
        {
            return new PartyRoomSummary(id, name, EPartyMode.Normal, "forest", -1, EPartyPhase.Open, 1, 0, false, 1, 0, 0, 0, 0, 0);
        }

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("PartyRecruitTestHost", typeof(RectTransform));
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KOREAN_FONT_PATH);
            Assert.IsNotNull(font, KOREAN_FONT_PATH);
            _view = PartyRecruitView.Create(_host.transform, font);
            _view.SetOpen(true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
        }

        [Test]
        public void SetOpen_True_ReportsOpen()
        {
            Assert.IsTrue(_view.IsOpen);
        }

        [Test]
        public void ApplyPreviewRooms_TwoRooms_ShowsBothAndEntersPreview()
        {
            _view.ApplyPreviewRooms(new[] { Room(1, "알파"), Room(2, "베타") });

            Assert.IsTrue(_view.PreviewMode);
            Assert.AreEqual(2, _view.VisibleRoomCount);
            Assert.IsFalse(_view.IsEmptyShown);
        }

        [Test]
        public void SetSearch_NoMatch_ShowsEmptyState()
        {
            _view.ApplyPreviewRooms(new[] { Room(1, "알파") });

            _view.SetSearch("없는방");

            Assert.AreEqual(0, _view.VisibleRoomCount);
            Assert.IsTrue(_view.IsEmptyShown);
        }

        [Test]
        public void RequestList_WithoutTownConnection_ShowsTownOnlyAndDoesNotLock()
        {
            _view.RequestList();

            Assert.IsFalse(_view.IsRequestLocked);
            StringAssert.Contains("마을에서만", _view.StatusText);
        }

        [Test]
        public void RequestJoin_WithoutTownConnection_DoesNotLockRequests()
        {
            _view.ApplyRooms(new[] { Room(1, "알파") });
            _view.SelectRoom(1);

            _view.RequestJoin();

            Assert.IsFalse(_view.IsRequestLocked);
            StringAssert.Contains("마을에서만", _view.StatusText);
        }

        [Test]
        public void RequestList_InPreview_KeepsPreviewStatus()
        {
            _view.ApplyPreviewRooms(new[] { Room(1, "알파") });

            _view.RequestList();

            StringAssert.DoesNotContain("마을에서만", _view.StatusText);
            Assert.AreEqual(1, _view.VisibleRoomCount);
        }

        [Test]
        public void RequestCreate_InPreview_DoesNotLockRequests()
        {
            _view.ApplyPreviewRooms(new[] { Room(1, "알파") });

            _view.RequestCreate();

            Assert.IsFalse(_view.IsRequestLocked);
        }
    }
}
