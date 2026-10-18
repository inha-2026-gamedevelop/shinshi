// System
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

// 서드파티
using TMPro;

// 테스트
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.UI;

namespace Shinshi.Tests
{
    public class ModernDarkMailDungeonLayoutTests
    {
        private const string MAIL_PREFAB    = "Assets/02.Prefab/UI/Windows/PF_MailBoxWindow.prefab";
        private const string DUNGEON_PREFAB = "Assets/02.Prefab/UI/Windows/PF_DungeonEntryWindow.prefab";
        private const float WINDOW_HEIGHT   = 820f;

        private readonly List<GameObject> _roots = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in _roots)
            {
                if (root != null)
                {
                    Object.DestroyImmediate(root);
                }
            }

            _roots.Clear();
        }

        [TestCase(5)]
        [TestCase(12)]
        [TestCase(20)]
        public void Mail_ListAndPager_StayInsideWindow(int rowCount)
        {
            Assert.GreaterOrEqual(MailBoxModernLayout.RowHeight(rowCount), 24f);
            Assert.LessOrEqual(MailBoxModernLayout.FooterY(rowCount) + 44f, WINDOW_HEIGHT - 52f);
        }

        [Test]
        public void Mail_TwelveRows_KeepComfortableHeight()
        {
            Assert.AreEqual(44f, MailBoxModernLayout.RowHeight(12), 0.01f);
        }

        [Test]
        public void Mail_Apply_AddsCloseGlyph_AndCalledTwiceAddsNothing()
        {
            var view = Spawn<MailBoxView>(MAIL_PREFAB);
            MailBoxModernLayout.Apply(view);
            Assert.IsNotNull(ModernDarkKit.Field<Button>(view, "_closeButton").transform.Find("MD_Glyph"));

            int children = view.transform.childCount;
            MailBoxModernLayout.Apply(view);
            Assert.AreEqual(children, view.transform.childCount);
        }

        [Test]
        public void Dungeon_Apply_AddsCloseGlyph_AndEnterLabelIsLightAndCentred()
        {
            var view = Spawn<DungeonEntryView>(DUNGEON_PREFAB);
            DungeonEntryModernLayout.Apply(view);
            Assert.IsNotNull(ModernDarkKit.Field<Button>(view, "_closeButton").transform.Find("MD_Glyph"));

            var label = ModernDarkKit.Field<TMP_Text>(view, "_enterLabel");
            Color text = label.color;
            Assert.GreaterOrEqual((text.r + text.g + text.b) / 3f, 0.6f, "입장 글자는 어두운 단추 바탕 위에서 밝아야 한다");
            Assert.AreEqual(TextAlignmentOptions.Center, label.alignment);
        }

        [Test]
        public void Dungeon_Apply_SceneryOnlyWhenCatalogBackgroundExists_AndNeverBlocksRaycasts()
        {
            var view = Spawn<DungeonEntryView>(DUNGEON_PREFAB);
            DungeonEntryModernLayout.Apply(view);

            ModernDarkUiCatalog catalog = ModernDarkUiCatalog.Load();
            Transform art = view.transform.Find("MD_DetailArt");
            if ((catalog == null) || (catalog.Background == null))
            {
                Assert.IsNull(art);
                return;
            }

            Assert.IsNotNull(art);
            Assert.IsFalse(art.GetComponent<Image>().raycastTarget);
            Assert.IsFalse(view.transform.Find("MD_DetailVeil").GetComponent<Image>().raycastTarget);
        }

        [Test]
        public void Dungeon_Apply_CalledTwice_DoesNotAddChildren()
        {
            var view = Spawn<DungeonEntryView>(DUNGEON_PREFAB);
            DungeonEntryModernLayout.Apply(view);
            int children = view.transform.childCount;
            DungeonEntryModernLayout.Apply(view);
            Assert.AreEqual(children, view.transform.childCount);
        }

        private T Spawn<T>(string path) where T : Component
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, path);
            GameObject instance = Object.Instantiate(prefab);
            _roots.Add(instance);
            return instance.GetComponent<T>();
        }
    }
}
