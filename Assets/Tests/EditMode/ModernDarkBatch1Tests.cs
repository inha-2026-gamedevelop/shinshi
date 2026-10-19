// System
using System;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 테스트
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.UI;

namespace Shinshi.Tests
{
    public class ModernDarkBatch1Tests
    {
        // 1차 묶음(길드 · 캐릭터 상세 · 장비 강화 · 던전 선택 · 우편)의 개편 배치가 기대하는 직렬화 필드와 부모 계약을 검사한다

        private const string APPLIED = "MD_Applied";

        private readonly List<GameObject> _roots = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in _roots)
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            _roots.Clear();
        }

        [Test]
        public void LayoutFields_ExistWithExpectedTypes()
        {
            Check(typeof(MailBoxView), new Dictionary<string, Type>
            {
                { "_closeButton", typeof(Button) }, { "_statusLabel", typeof(TMP_Text) }, { "_rows", typeof(ListRowView[]) },
                { "_emptyMark", typeof(GameObject) }, { "_prevButton", typeof(Button) }, { "_nextButton", typeof(Button) },
                { "_pageLabel", typeof(TMP_Text) }, { "_detailPanel", typeof(GameObject) }, { "_detailHintMark", typeof(GameObject) },
                { "_detailTitle", typeof(TMP_Text) }, { "_detailSender", typeof(TMP_Text) }, { "_detailDate", typeof(TMP_Text) },
                { "_detailBody", typeof(TMP_Text) }, { "_claimButton", typeof(Button) },
            });

            Check(typeof(GuildView), new Dictionary<string, Type>
            {
                { "_closeButton", typeof(Button) }, { "_titleLabel", typeof(TMP_Text) }, { "_countLabel", typeof(TMP_Text) },
                { "_noGuildGroup", typeof(GameObject) }, { "_createInput", typeof(TMP_InputField) }, { "_createButton", typeof(Button) },
                { "_applyInput", typeof(TMP_InputField) }, { "_applyButton", typeof(Button) }, { "_guildGroup", typeof(GameObject) },
                { "_tabButtons", typeof(Button[]) }, { "_leaderGroup", typeof(GameObject) }, { "_inviteInput", typeof(TMP_InputField) },
                { "_inviteButton", typeof(Button) }, { "_leaveButton", typeof(Button) }, { "_listTitle", typeof(TMP_Text) },
                { "_rows", typeof(SocialRowView[]) }, { "_emptyLabel", typeof(TMP_Text) }, { "_prevPage", typeof(Button) },
                { "_nextPage", typeof(Button) }, { "_pageLabel", typeof(TMP_Text) }, { "_statusLabel", typeof(TMP_Text) },
                { "_confirmPanel", typeof(GameObject) }, { "_confirmLabel", typeof(TMP_Text) }, { "_confirmAccept", typeof(Button) },
                { "_confirmCancel", typeof(Button) },
            });

            Check(typeof(SocialRowView), new Dictionary<string, Type>
            {
                { "_presenceDot", typeof(Image) }, { "_nameLabel", typeof(TMP_Text) }, { "_detailLabel", typeof(TMP_Text) },
                { "_infoLabel", typeof(TMP_Text) }, { "_buttons", typeof(Button[]) },
            });

            Check(typeof(ListRowView), new Dictionary<string, Type> { { "_columns", typeof(TMP_Text[]) } });

            Check(typeof(CharDetailView), new Dictionary<string, Type>
            {
                { "_closeButton", typeof(Button) }, { "_nameLabel", typeof(TMP_Text) }, { "_classLabel", typeof(TMP_Text) },
                { "_attackLabel", typeof(TMP_Text) }, { "_healthLabel", typeof(TMP_Text) }, { "_defenseLabel", typeof(TMP_Text) },
                { "_critLabel", typeof(TMP_Text) }, { "_energyLabel", typeof(TMP_Text) }, { "_speedLabel", typeof(TMP_Text) },
                { "_lifeStealLabel", typeof(TMP_Text) }, { "_combatPowerLabel", typeof(TMP_Text) },
                { "_slotNameLabels", typeof(TMP_Text[]) }, { "_slotEnhanceLabels", typeof(TMP_Text[]) },
                { "_trainingLabels", typeof(TMP_Text[]) }, { "_talismanLabel", typeof(TMP_Text) }, { "_previewStage", typeof(GameObject) },
            });

            Check(typeof(EquipmentEnhanceView), new Dictionary<string, Type>
            {
                { "_slotButtons", typeof(Button[]) }, { "_slotFrames", typeof(Image[]) }, { "_slotIcons", typeof(Image[]) },
                { "_slotNameTexts", typeof(TMP_Text[]) }, { "_slotLevelTexts", typeof(TMP_Text[]) }, { "_slotSelections", typeof(GameObject[]) },
                { "_itemFrame", typeof(Image) }, { "_itemIcon", typeof(Image) }, { "_itemNameText", typeof(TMP_Text) },
                { "_itemGradeText", typeof(TMP_Text) }, { "_levelFromText", typeof(TMP_Text) }, { "_levelToText", typeof(TMP_Text) },
                { "_levelArrow", typeof(GameObject) }, { "_rateText", typeof(TMP_Text) }, { "_rateCaption", typeof(TMP_Text) },
                { "_pityFill", typeof(Image) }, { "_pityText", typeof(TMP_Text) }, { "_statRows", typeof(GameObject[]) },
                { "_statNameTexts", typeof(TMP_Text[]) }, { "_statBeforeTexts", typeof(TMP_Text[]) }, { "_statArrows", typeof(GameObject[]) },
                { "_statAfterTexts", typeof(TMP_Text[]) }, { "_essenceCostText", typeof(TMP_Text) }, { "_shardCostText", typeof(TMP_Text) },
                { "_essenceText", typeof(TMP_Text) }, { "_shardText", typeof(TMP_Text) }, { "_noteText", typeof(TMP_Text) },
                { "_statusText", typeof(TMP_Text) }, { "_enhanceButton", typeof(Button) }, { "_enhanceLabel", typeof(TMP_Text) },
                { "_closeButton", typeof(Button) }, { "_resultGroup", typeof(CanvasGroup) }, { "_previewTag", typeof(GameObject) },
            });

            Check(typeof(DungeonEntryView), new Dictionary<string, Type>
            {
                { "_closeButton", typeof(Button) }, { "_rowButtons", typeof(Button[]) }, { "_rowTitles", typeof(TMP_Text[]) },
                { "_rowCategories", typeof(TMP_Text[]) }, { "_rowMarks", typeof(GameObject[]) }, { "_emptyRoot", typeof(GameObject) },
                { "_detailRoot", typeof(GameObject) }, { "_detailCategory", typeof(TMP_Text) }, { "_detailTitle", typeof(TMP_Text) },
                { "_detailSummary", typeof(TMP_Text) }, { "_detailRequirement", typeof(TMP_Text) }, { "_detailCost", typeof(TMP_Text) },
                { "_enterButton", typeof(Button) }, { "_enterLabel", typeof(TMP_Text) }, { "_noticeLabel", typeof(TMP_Text) },
            });
        }

        [Test]
        public void Apply_IsIdempotent_ForEveryBatchOneView()
        {
            AssertIdempotent<MailBoxView>(MailBoxModernLayout.Apply);
            AssertIdempotent<GuildView>(GuildModernLayout.Apply);
            AssertIdempotent<CharDetailView>(CharDetailModernLayout.Apply);
            AssertIdempotent<EquipmentEnhanceView>(EquipmentEnhanceModernLayout.Apply);
            AssertIdempotent<DungeonEntryView>(DungeonEntryModernLayout.Apply);
        }

        [Test]
        public void DungeonEntry_KeepsGroupAndAncestorContracts()
        {
            RectTransform window = NewWindow();
            var view = window.gameObject.AddComponent<DungeonEntryView>();

            RectTransform detail = NewChild(window, "Detail");
            detail.gameObject.AddComponent<CanvasGroup>();
            TMP_Text title = NewText(detail, "Title");

            RectTransform list = NewChild(window, "List");
            list.gameObject.AddComponent<CanvasGroup>();
            Button row = NewButton(list, "Row0");
            TMP_Text rowTitle = NewText(row.transform, "RowTitle");

            Button enter = NewButton(window, "Enter");
            GameObject legacy = NewChild(window, "Legacy").gameObject;

            SetField(view, "_detailRoot", detail.gameObject);
            SetField(view, "_detailTitle", title);
            SetField(view, "_rowButtons", new[] { row });
            SetField(view, "_rowTitles", new[] { rowTitle });
            SetField(view, "_enterButton", enter);

            DungeonEntryModernLayout.Apply(view);

            Assert.AreSame(detail, title.transform.parent, "묶음 안 글자는 묶음에 남아야 한다");
            Assert.AreSame(window, detail.parent);
            Assert.AreSame(list, row.transform.parent, "CanvasGroup을 가진 조상 아래 줄은 부모를 유지해야 한다");
            Assert.AreSame(window, enter.transform.parent);
            Assert.IsFalse(legacy.activeSelf, "참조 없는 옛 직속 자식은 숨겨야 한다");
            Assert.IsTrue(detail.gameObject.activeSelf, "참조한 묶음은 숨기면 안 된다");

            detail.gameObject.SetActive(false);
            Assert.IsFalse(title.gameObject.activeInHierarchy, "묶음을 끄면 안쪽 글자도 꺼져야 한다");
            detail.gameObject.SetActive(true);
            Assert.IsTrue(title.gameObject.activeInHierarchy);

            Assert.AreEqual(new Vector2(420f, 64f), ((RectTransform)enter.transform).sizeDelta);
            Assert.AreEqual(1, CountNamed(window, APPLIED));
        }

        [Test]
        public void CollectReferences_FollowsSerializableStructsAndArrays()
        {
            RectTransform window = NewWindow();
            Button single    = NewButton(window, "Single");
            Button inArray   = NewButton(window, "InArray");
            GameObject deep  = NewChild(window, "Deep").gameObject;
            GameObject plain = NewChild(window, "Plain").gameObject;

            var owner = new Owner
            {
                One   = new Holder { Button = single },
                Many  = new[] { new Holder { Button = inArray, Objects = new[] { deep } } },
                Count = 3,
                Tint  = Color.red,
            };

            var found = new HashSet<Transform>();
            ModernDarkKit.CollectReferences(owner, found);

            Assert.IsTrue(found.Contains(single.transform), "구조체 안 Button이 빠졌다");
            Assert.IsTrue(found.Contains(inArray.transform), "구조체 배열 안 Button이 빠졌다");
            Assert.IsTrue(found.Contains(deep.transform), "구조체 배열 안 배열의 GameObject가 빠졌다");
            Assert.IsFalse(found.Contains(plain.transform), "참조하지 않은 오브젝트가 들어갔다");
        }

        [Serializable]
        private struct Holder
        {
            public Button       Button;
            public GameObject[] Objects;
        }

        private sealed class Owner
        {
            public Holder   One;
            public Holder[] Many;
            public int      Count;
            public Color    Tint;
        }

        private void AssertIdempotent<T>(Action<T> apply) where T : MonoBehaviour
        {
            RectTransform window = NewWindow();
            var view = window.gameObject.AddComponent<T>();

            apply(view);
            int first = CountDecor(window);
            Assert.AreEqual(1, CountNamed(window, APPLIED), typeof(T).Name + " 표식이 하나여야 한다");

            apply(view);
            Assert.AreEqual(1, CountNamed(window, APPLIED), typeof(T).Name + " 두 번째 적용이 표식을 늘렸다");
            Assert.AreEqual(first, CountDecor(window), typeof(T).Name + " 두 번째 적용이 장식을 늘렸다");
        }

        [Test]
        public void GuildConfirm_BackdropKeepsBlockingAfterApplyAndShow()
        {
            RectTransform window = NewWindow();
            var view = window.gameObject.AddComponent<GuildView>();
            SetField(view, "_statusLabel", NewText(window, "Status"));

            RectTransform confirm = NewChild(window, "Confirm");
            var backdrop = confirm.gameObject.AddComponent<Image>();
            backdrop.raycastTarget = true;
            SetField(view, "_confirmPanel", confirm.gameObject);
            SetField(view, "_confirmLabel", NewText(confirm, "ConfirmLabel"));
            confirm.gameObject.SetActive(false);

            GuildModernLayout.Apply(view);
            Assert.IsTrue(backdrop.enabled, "확인창 루트 배경이 꺼졌다");
            Assert.IsTrue(backdrop.raycastTarget, "개편 배치가 확인창 배경의 클릭 차단을 껐다");

            GuildModernLayout.Apply(view);
            view.ShowConfirm("견본 확인 문구");
            Assert.IsTrue(confirm.gameObject.activeSelf, "확인창이 열리지 않았다");
            Assert.IsTrue(backdrop.enabled && backdrop.raycastTarget, "열린 확인창 뒤의 창이 클릭을 받을 수 있다");
        }

        [Test]
        public void PlaceChild_SetsLocalRectWithoutReparenting()
        {
            RectTransform window = NewWindow();
            RectTransform parent = NewChild(window, "Parent");
            RectTransform child  = NewChild(parent, "Child");

            ModernDarkKit.PlaceChild(child, 8f, 12f, 56f, 40f);
            ModernDarkKit.PlaceChild(child, 8f, 12f, 56f, 40f);

            Assert.AreSame(parent, child.parent, "PlaceChild가 부모를 바꿨다");
            Assert.AreEqual(56f, child.rect.width, 0.01f);
            Assert.AreEqual(40f, child.rect.height, 0.01f);
        }

        [Test]
        public void StyleClose_AddsSingleGlyphWhenCalledTwice()
        {
            RectTransform window = NewWindow();
            Button close = NewButton(window, "Close");
            var owner = window.gameObject.AddComponent<GuildView>();
            Assert.IsTrue(ModernDarkKit.TryBegin(owner, window, NewText(window, "Font"), out ModernDarkKit kit));

            kit.StyleClose(close);
            kit.StyleClose(close);

            Assert.AreEqual(1, CountNamed(close.transform, UiModernDark.DECOR_PREFIX + "Glyph"), "닫기 글리프가 중복됐다");
        }

        [Test]
        public void GuildRowButtons_StayRightOfInfoLabelAfterLayoutRebuild()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/02.Prefab/UI/Windows/PF_GuildWindow.prefab");
            Assert.IsNotNull(prefab, "PF_GuildWindow를 찾지 못했다");

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            _roots.Add(instance);
            var view = instance.GetComponentInChildren<GuildView>(true);
            Assert.IsNotNull(view);
            SocialRowView row = view.Row(0);
            Assert.IsNotNull(row, "첫 줄이 없다");
            // 가로 배치는 activeInHierarchy인 자식만 계산한다. 프리팹의 '길드 있음' 묶음이 꺼져 있어 줄까지 올라가며 모두 켠다
            for (Transform t = row.transform; t != null; t = t.parent)
            {
                t.gameObject.SetActive(true);
            }

            const float width = 960f, height = 52f;
            var rowRect = (RectTransform)row.transform;
            rowRect.sizeDelta = new Vector2(width, height);

            for (int pass = 0; pass < 2; ++pass)
            {
                SocialRowModernLayout.Style(row, width, height);
                for (int i = 0; i < row.ButtonCount; ++i)
                {
                    row.SetButton(i, "버튼" + i, true);
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(rowRect);

                var info    = ModernDarkKit.Field<TMP_Text>(row, "_infoLabel");
                var buttons = ModernDarkKit.Field<Button[]>(row, "_buttons");
                Assert.IsNotNull(info);
                Rect infoRect = WorldRect((RectTransform)info.transform);
                Rect rowWorld = WorldRect(rowRect);
                for (int i = 0; i < buttons.Length; ++i)
                {
                    Rect button = WorldRect((RectTransform)buttons[i].transform);
                    Assert.IsFalse(button.Overlaps(infoRect), "패스 " + pass + ": 버튼 " + i + "이 정보 글자와 겹친다");
                    Assert.GreaterOrEqual(button.xMin, rowWorld.xMin - 0.5f, "버튼 " + i + "이 줄 왼쪽 밖이다");
                    Assert.LessOrEqual(button.xMax, rowWorld.xMax + 0.5f, "버튼 " + i + "이 줄 오른쪽 밖이다");
                    for (int j = i + 1; j < buttons.Length; ++j)
                    {
                        Assert.IsFalse(button.Overlaps(WorldRect((RectTransform)buttons[j].transform)), "버튼 " + i + "과 " + j + "이 겹친다");
                    }
                }
            }
        }

        [Test]
        public void GuildNoGuild_CardsStaySeparateFromInvitationTitleAndEmptyMessage()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/02.Prefab/UI/Windows/PF_GuildWindow.prefab");
            Assert.IsNotNull(prefab, "PF_GuildWindow를 찾지 못했다");

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            _roots.Add(instance);
            var view = instance.GetComponentInChildren<GuildView>(true);
            Assert.IsNotNull(view);

            GuildModernLayout.Apply(view);
            view.SetMode(EGuildWindowMode.NoGuild, false);
            view.SetListTitle("받은 길드 초대");
            view.SetEmpty("받은 길드 초대가 없습니다. 길드를 만들거나 이름으로 가입을 신청하세요.");

            var title = ModernDarkKit.Field<TMP_Text>(view, "_listTitle");
            var empty = ModernDarkKit.Field<TMP_Text>(view, "_emptyLabel");
            var cards = new Component[]
            {
                ModernDarkKit.Field<TMP_InputField>(view, "_createInput"),
                ModernDarkKit.Field<Button>(view, "_createButton"),
                ModernDarkKit.Field<TMP_InputField>(view, "_applyInput"),
                ModernDarkKit.Field<Button>(view, "_applyButton"),
            };
            Assert.IsNotNull(title);
            Assert.IsNotNull(empty);
            Assert.IsTrue(empty.gameObject.activeSelf, "빈 안내가 켜지지 않았다");

            Rect titleRect = WorldRect((RectTransform)title.transform);
            Rect emptyRect = WorldRect((RectTransform)empty.transform);
            Assert.IsFalse(titleRect.Overlaps(emptyRect), "초대 제목과 빈 안내가 겹친다");
            foreach (Component card in cards)
            {
                Assert.IsNotNull(card);
                Rect cardRect = WorldRect((RectTransform)card.transform);
                Assert.IsFalse(cardRect.Overlaps(emptyRect), card.name + "이 빈 안내와 겹친다");
                Assert.IsFalse(cardRect.Overlaps(titleRect), card.name + "이 초대 제목과 겹친다");
            }
        }

        [Test]
        public void CharDetailBinder_DisplayEquipmentName_KeepsRawTextWithoutCatalog()
        {
            Assert.AreEqual("equip_weapon_rare", CharDetailBinder.DisplayEquipmentName(null, "equip_weapon_rare"));
            Assert.AreEqual("-", CharDetailBinder.DisplayEquipmentName(null, "-"));
            Assert.AreEqual(string.Empty, CharDetailBinder.DisplayEquipmentName(null, string.Empty));
            Assert.IsNull(CharDetailBinder.DisplayEquipmentName(null, null));
        }

        private static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static void Check(Type view, Dictionary<string, Type> fields)
        {
            foreach (KeyValuePair<string, Type> expected in fields)
            {
                FieldInfo field = null;
                for (Type type = view; (type != null) && (field == null); type = type.BaseType)
                {
                    field = type.GetField(expected.Key, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                }

                Assert.IsNotNull(field, view.Name + "에 " + expected.Key + " 필드가 없다");
                Assert.IsTrue(expected.Value.IsAssignableFrom(field.FieldType), view.Name + "." + expected.Key + " 형식이 다르다: " + field.FieldType.Name);
            }
        }

        private static void SetField(object owner, string name, object value)
        {
            FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(field, name);
            field.SetValue(owner, value);
        }

        private RectTransform NewWindow()
        {
            var canvas = new GameObject("TestCanvas", typeof(RectTransform), typeof(Canvas));
            _roots.Add(canvas);
            RectTransform window = NewChild(canvas.transform, "Window");
            window.sizeDelta = new Vector2(1500f, 820f);
            return window;
        }

        private static RectTransform NewChild(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Button NewButton(Transform parent, string name)
        {
            RectTransform rect = NewChild(parent, name);
            rect.gameObject.AddComponent<Image>();
            return rect.gameObject.AddComponent<Button>();
        }

        private static TMP_Text NewText(Transform parent, string name)
        {
            RectTransform rect = NewChild(parent, name);
            return rect.gameObject.AddComponent<TextMeshProUGUI>();
        }

        private static int CountNamed(Transform window, string name)
        {
            int count = 0;
            foreach (Transform child in window)
            {
                if (child.name == name)
                {
                    ++count;
                }
            }

            return count;
        }

        private static int CountDecor(Transform window)
        {
            int count = 0;
            foreach (Transform child in window.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.StartsWith(UiModernDark.DECOR_PREFIX, StringComparison.Ordinal))
                {
                    ++count;
                }
            }

            return count;
        }
    }
}
