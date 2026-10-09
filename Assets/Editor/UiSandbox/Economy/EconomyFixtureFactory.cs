// System
using System;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.UI;

using B = Shinshi.Editor.EconomyUiBuilder;

namespace Shinshi.Editor
{
    /// <summary> 안전한 시험 씬에 View가 없을 때 실제 View 클래스와 직렬화 참조로 시험 배치를 조립한다. 꺼진 루트에서 연결을 끝낸 뒤 켜므로 Awake가 연결된 참조를 본다 </summary>
    public static class EconomyFixtureFactory
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ROOT_PREFIX    = "UiSandboxFixture_";
        public const string FIXTURE_NOTICE = "시험 배치: 이 씬에 View가 없어 시험용으로 조립한 화면입니다. 원본 씬과 프리팹은 바뀌지 않았습니다";

        private const string NOTICE_SHORT = "시험 배치";

        private const float CONTENT_TOP        = 72f;
        private const float HEADER_TITLE_WIDTH = 520f;
        private const float SMALL_BUTTON_WIDTH = 80f;
        private const float BUTTON_WIDTH       = 140f;
        private const float FIELD_WIDTH        = 120f;
        private const float STEP_WIDTH         = 40f;
        private const float STATUS_HEIGHT      = 28f;
        private const float NOTICE_Y           = 40f;
        private const float COUNT_LABEL_WIDTH  = 300f;
        private const float PAGE_LABEL_WIDTH   = 100f;
        private const float WIDE_BUTTON_HEIGHT = 40f;

        private const float MAIL_WIDTH         = 900f;
        private const float MAIL_HEIGHT        = 480f;
        private const int   MAIL_ROWS          = 6;
        private const int   MAIL_COLUMNS       = 5;
        private const float MAIL_LIST_WIDTH    = 520f;
        private const float MAIL_DETAIL_X      = 552f;
        private const float MAIL_DETAIL_WIDTH  = 332f;
        private const float MAIL_DETAIL_HEIGHT = 300f;

        private const float CHAR_WIDTH       = 760f;
        private const float CHAR_HEIGHT      = 500f;
        private const int   SLOT_COUNT       = 6;
        private const int   TRAINING_COUNT   = 5;
        private const int   STAT_COUNT       = 6;
        private const float CHAR_LINE_HEIGHT = 30f;

        private const float AUCTION_WIDTH             = 1100f;
        private const float AUCTION_HEIGHT            = 650f;
        private const float AUCTION_PAGE_TOP          = 116f;
        private const float AUCTION_PAGE_WIDTH        = 1068f;
        private const float AUCTION_PAGE_HEIGHT       = 480f;
        private const int   AUCTION_TAB_COUNT         = 4;
        private const int   AUCTION_GROUP_ROWS        = 8;
        private const int   AUCTION_LISTING_ROWS      = 6;
        private const int   AUCTION_SELLABLE_ROWS     = 8;
        private const int   AUCTION_MINE_ROWS         = 8;
        private const int   AUCTION_HISTORY_ROWS      = 8;
        private const float AUCTION_TAB_WIDTH         = 120f;
        private const float AUCTION_GROUP_WIDTH       = 620f;
        private const float AUCTION_DETAIL_X          = 640f;
        private const float AUCTION_DETAIL_WIDTH      = 428f;
        private const float AUCTION_DETAIL_HEIGHT     = 440f;
        private const float AUCTION_LIST_TOP          = 44f;
        private const float AUCTION_WIDE_ROW_WIDTH    = 900f;
        private const float AUCTION_HISTORY_ROW_WIDTH = 1000f;

        private const float TRADE_WIDTH           = 1000f;
        private const float TRADE_HEIGHT          = 640f;
        private const int   TRADE_OFFER_ROWS      = 8;
        private const int   TRADE_INVENTORY_ROWS  = 8;
        private const int   TRADE_COLUMNS         = 2;
        private const float TRADE_OFFER_WIDTH     = 300f;
        private const float TRADE_PARTNER_X       = 330f;
        private const float TRADE_INVENTORY_X     = 644f;
        private const float TRADE_INVENTORY_WIDTH = 340f;
        private const float TRADE_LIST_TOP        = 84f;

        private static bool  _testScopeActive;
        private static Scene _testScene;

        private static readonly string[] G_AuctionTabNames = { "구매", "판매", "내 등록", "기록" };
        private static readonly string[] G_CategoryNames    = { "전체", "약초", "부적", "장비" };
        private static readonly string[] G_ClassOptions     = { "직업 전체", "직업 0 (구)", "직업 1 (구)", "직업 2 (구)", "직업 3 (검사)", "직업 4 (마검사)", "직업 5 (궁수)" };
        private static readonly string[] G_GradeOptions     = { "등급 전체", "일반", "희귀", "영웅", "전설" };
        private static readonly string[] G_SortOptions      = { "개당 가격", "최신순", "수량" };
        private static readonly string[] G_StatCaptions     = { "공격력", "체력", "방어력", "치명", "기력", "이동속도" };
        private static readonly string[] G_TrainingCaptions = { "공격", "체력", "방어", "치명", "기력" };

        /****************************************
        *                Methods
        ****************************************/

        public static MailBoxView EnsureMailBox(UiSandboxContext context, out string note)
        {
            return Ensure<MailBoxView>(context, "MailBox", BuildMailBox, out note);
        }

        public static CharDetailView EnsureCharDetail(UiSandboxContext context, out string note)
        {
            return Ensure<CharDetailView>(context, "CharDetail", BuildCharDetail, out note);
        }

        public static AuctionView EnsureAuction(UiSandboxContext context, out string note)
        {
            return Ensure<AuctionView>(context, "Auction", BuildAuction, out note);
        }

        public static TradeView EnsureTrade(UiSandboxContext context, out string note)
        {
            return Ensure<TradeView>(context, "Trade", BuildTrade, out note);
        }

        /// <summary> 이 컴포넌트가 시험 배치로 조립된 것인지 루트 이름으로 판정한다 </summary>
        public static bool IsFixture(Component component)
        {
            return (component != null) && (component.transform.root.name.StartsWith(ROOT_PREFIX, StringComparison.Ordinal));
        }

        /// <summary> 연결되지 않은 참조 필드 경로를 모은다. 시험 배치가 직렬화 참조를 모두 채웠는지 확인하는 데 쓴다 </summary>
        public static List<string> FindUnwired(Component view)
        {
            List<string> missing = new List<string>();
            SerializedObject serialized = new SerializedObject(view);
            SerializedProperty iterator = serialized.GetIterator();
            bool enter = true;
            while (iterator.NextVisible(enter))
            {
                enter = true;
                if (iterator.propertyPath == "m_Script")
                {
                    continue;
                }

                if ((iterator.propertyType == SerializedPropertyType.ObjectReference) && (iterator.objectReferenceValue == null))
                {
                    missing.Add(iterator.propertyPath);
                }
                else if ((iterator.isArray) && (iterator.propertyType != SerializedPropertyType.String) && (iterator.arraySize == 0))
                {
                    missing.Add(iterator.propertyPath);
                }
            }

            return missing;
        }

        /// <summary> 시험 코드가 직접 만든 미리보기 씬 하나에만 시험 배치를 허용한다. 배치는 그 씬 안에 만들고 활성 씬에 기대지 않는다. 해제하면 다시 샌드박스 사본만 안전하다 </summary>
        public static IDisposable BeginTestScope(Scene scene)
        {
            if (!scene.IsValid())
            {
                throw new ArgumentException("유효한 씬이어야 한다", nameof(scene));
            }

            if (_testScopeActive)
            {
                throw new InvalidOperationException("시험 범위는 겹쳐 쓸 수 없다");
            }

            _testScopeActive = true;
            _testScene = scene;
            return new TestScope();
        }

        /// <summary> 샌드박스가 만든 사본(경로와 소유 라벨 일치)이거나 시험 범위로 지정한 활성 씬에서만 시험 배치를 만든다. 경로가 비었다는 사실만으로는 허용하지 않는다 </summary>
        public static bool IsSafeScene(out string reason)
        {
            if (UiSandboxSceneBuilder.TryGetActiveOwnedSandboxScene(out Scene _))
            {
                reason = null;
                return true;
            }

            if ((_testScopeActive) && (_testScene.IsValid()))
            {
                reason = null;
                return true;
            }

            Scene active = SceneManager.GetActiveScene();
            string path = UiSandboxSafetyPolicy.NormalizePath(active.path);
            reason = "열린 씬이 샌드박스가 만든 사본이 아니라 시험 배치를 만들지 않았습니다 ("
                + ((path.Length == 0) ? "저장 안 된 씬" : path) + "). 원본 씬은 바꾸지 않습니다";
            return false;
        }

        /// <summary> 시험 배치를 지운다. 루트가 시험 배치가 아니면 건드리지 않는다 </summary>
        public static void Release(UiSandboxContext context, Component view)
        {
            if ((view == null) || (!IsFixture(view)))
            {
                return;
            }

            GameObject root = view.transform.root.gameObject;
            if ((context != null) && (!context.IsDisposed))
            {
                List<GameObject> roots = new List<GameObject>(context.Roots);
                if (roots.Remove(root))
                {
                    context.Rebind(roots.ToArray());
                }
            }

            UnityEngine.Object.DestroyImmediate(root);
        }

        private static T Ensure<T>(UiSandboxContext context, string name, Func<string, GameObject> build, out string note)
            where T : Component
        {
            T existing = context.Find<T>();
            if (existing != null)
            {
                note = IsFixture(existing) ? FIXTURE_NOTICE : string.Empty;
                return existing;
            }

            if (!IsSafeScene(out string reason))
            {
                note = reason;
                return null;
            }

            Scene target = TargetScene();
            string rootName = ROOT_PREFIX + name;
            GameObject root = FindSceneRoot(target, rootName);
            if ((root != null) && (root.GetComponentInChildren<T>(true) == null))
            {
                UnityEngine.Object.DestroyImmediate(root);
                root = null;
            }

            if (root == null)
            {
                root = build(rootName);
                if (root.scene != target)
                {
                    SceneManager.MoveGameObjectToScene(root, target);
                }
            }

            List<string> unwired = FindUnwired(root.GetComponentInChildren<T>(true));
            if (unwired.Count > 0)
            {
                UnityEngine.Object.DestroyImmediate(root);
                note = "시험 배치의 직렬화 참조가 비어 활성화하지 않았습니다: " + string.Join(", ", unwired);
                return null;
            }

            B.EnsureEventSystem(root.transform);
            root.GetComponent<Canvas>().sortingOrder = TopSortingOrder(target, root);
            root.SetActive(true);
            AddRoot(context, root);
            T view = context.Find<T>();
            note = (view != null) ? FIXTURE_NOTICE : "시험 배치를 만들었지만 " + typeof(T).Name + "을(를) 찾지 못했습니다";
            return view;
        }

        // 같은 씬의 원본 자리표시 캔버스가 시험 배치를 가리거나 클릭을 막지 않도록 항상 그 위에 그린다
        private static int TopSortingOrder(Scene scene, GameObject own)
        {
            int top = B.FIXTURE_SORT_ORDER;
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; ++i)
            {
                if (roots[i] == own)
                {
                    continue;
                }

                Canvas[] canvases = roots[i].GetComponentsInChildren<Canvas>(true);
                for (int c = 0; c < canvases.Length; ++c)
                {
                    if (canvases[c].transform.root.name.StartsWith(ROOT_PREFIX, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    top = Mathf.Max(top, canvases[c].sortingOrder + 1);
                }
            }

            return top;
        }

        private sealed class TestScope : IDisposable
        {
            public void Dispose()
            {
                _testScopeActive = false;
                _testScene = default;
            }
        }

        // 시험 범위가 열려 있으면 그 씬, 아니면 샌드박스 사본인 활성 씬
        private static Scene TargetScene()
        {
            return ((_testScopeActive) && (_testScene.IsValid())) ? _testScene : SceneManager.GetActiveScene();
        }

        private static GameObject FindSceneRoot(Scene scene, string rootName)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; ++i)
            {
                if (roots[i].name == rootName)
                {
                    return roots[i];
                }
            }

            return null;
        }

        private static void AddRoot(UiSandboxContext context, GameObject root)
        {
            List<GameObject> roots = new List<GameObject>(context.Roots);
            if (roots.Contains(root))
            {
                context.InvalidateCache();
                return;
            }

            roots.Add(root);
            context.Rebind(roots.ToArray());
        }

        // 창 제목, 시험 배치 표시, 닫기 단추를 만든다
        private static Button AddHeader(Transform window, string title, float width)
        {
            B.CreateLabel(window, "Title", title, B.MARGIN, B.GAP, HEADER_TITLE_WIDTH, B.LABEL_HEIGHT + B.GAP, B.TITLE_FONT);
            TMP_Text notice = B.CreateLabel(window, "FixtureNotice", NOTICE_SHORT, B.MARGIN, NOTICE_Y, HEADER_TITLE_WIDTH, B.LABEL_HEIGHT, B.SMALL_FONT);
            notice.color = B.MarkColor;
            return B.CreateButton(window, "CloseButton", "닫기", width - B.MARGIN - SMALL_BUTTON_WIDTH, B.GAP, SMALL_BUTTON_WIDTH, B.BUTTON_HEIGHT);
        }

        private static TMP_Text AddStatus(Transform window, float windowHeight, float width)
        {
            return B.CreateLabel(window, "StatusLabel", string.Empty, B.MARGIN, windowHeight - B.MARGIN - STATUS_HEIGHT,
                width - (B.MARGIN * 2f), STATUS_HEIGHT, B.SMALL_FONT);
        }

        private static void AddPager(Transform parent, float x, float y, out Button prev, out TMP_Text label, out Button next)
        {
            prev  = B.CreateButton(parent, "PrevButton", "이전", x, y, SMALL_BUTTON_WIDTH, B.BUTTON_HEIGHT);
            label = B.CreateLabel(parent, "PageLabel", string.Empty, x + SMALL_BUTTON_WIDTH + B.GAP, y, PAGE_LABEL_WIDTH, B.BUTTON_HEIGHT,
                B.FONT_SIZE, TextAlignmentOptions.Center);
            next  = B.CreateButton(parent, "NextButton", "다음", x + SMALL_BUTTON_WIDTH + PAGE_LABEL_WIDTH + (B.GAP * 2f), y,
                SMALL_BUTTON_WIDTH, B.BUTTON_HEIGHT);
        }

        private static float RowStride()
        {
            return B.ROW_HEIGHT + B.GAP;
        }

        /****************************************
        *                MailBox
        ****************************************/

        private static GameObject BuildMailBox(string rootName)
        {
            GameObject root = B.CreateCanvasRoot(rootName);
            Image window = B.CreateWindow(root.transform, "MailBoxWindow", MAIL_WIDTH, MAIL_HEIGHT);
            Transform t = window.transform;
            Button close = AddHeader(t, "우편함", MAIL_WIDTH);
            TMP_Text status = AddStatus(t, MAIL_HEIGHT, MAIL_WIDTH);

            ListRowView[] rows = B.CreateRows(t, "Row", MAIL_ROWS, MAIL_COLUMNS, B.MARGIN, CONTENT_TOP, MAIL_LIST_WIDTH, B.ROW_HEIGHT, B.GAP);
            GameObject emptyMark = B.CreateMark(t, "EmptyMark", string.Empty, B.MARGIN, CONTENT_TOP, MAIL_LIST_WIDTH, B.ROW_HEIGHT);
            float navY = CONTENT_TOP + (RowStride() * MAIL_ROWS) + B.GAP;
            AddPager(t, B.MARGIN, navY, out Button prev, out TMP_Text pageLabel, out Button next);

            GameObject detailPanel = B.CreateSection(t, "DetailPanel", MAIL_DETAIL_X, CONTENT_TOP, MAIL_DETAIL_WIDTH, MAIL_DETAIL_HEIGHT).gameObject;
            float inner = MAIL_DETAIL_WIDTH - (B.MARGIN * 2f);
            TMP_Text detailTitle  = B.CreateLabel(detailPanel.transform, "DetailTitle", string.Empty, B.MARGIN, B.GAP, inner, B.LABEL_HEIGHT + B.GAP, B.TITLE_FONT);
            TMP_Text detailSender = B.CreateLabel(detailPanel.transform, "DetailSender", string.Empty, B.MARGIN, 44f, inner, B.LABEL_HEIGHT);
            TMP_Text detailDate   = B.CreateLabel(detailPanel.transform, "DetailDate", string.Empty, B.MARGIN, 72f, inner, B.LABEL_HEIGHT, B.SMALL_FONT);
            TMP_Text detailBody   = B.CreateLabel(detailPanel.transform, "DetailBody", string.Empty, B.MARGIN, 104f, inner, 180f,
                B.FONT_SIZE, TextAlignmentOptions.TopLeft);
            GameObject hint = B.CreateMark(t, "DetailHint", "우편을 고르세요", MAIL_DETAIL_X, CONTENT_TOP, MAIL_DETAIL_WIDTH, B.ROW_HEIGHT);
            Button claim = B.CreateButton(t, "ClaimButton", "수령", MAIL_DETAIL_X, CONTENT_TOP + MAIL_DETAIL_HEIGHT + B.MARGIN, MAIL_DETAIL_WIDTH, WIDE_BUTTON_HEIGHT);

            MailBoxView view = window.gameObject.AddComponent<MailBoxView>();
            B.SetRef(view, "_closeButton", close);
            B.SetRef(view, "_statusLabel", status);
            B.SetRefs<ListRowView>(view, "_rows", rows);
            B.SetRef(view, "_emptyMark", emptyMark);
            B.SetRef(view, "_prevButton", prev);
            B.SetRef(view, "_nextButton", next);
            B.SetRef(view, "_pageLabel", pageLabel);
            B.SetRef(view, "_detailPanel", detailPanel);
            B.SetRef(view, "_detailHintMark", hint);
            B.SetRef(view, "_detailTitle", detailTitle);
            B.SetRef(view, "_detailSender", detailSender);
            B.SetRef(view, "_detailDate", detailDate);
            B.SetRef(view, "_detailBody", detailBody);
            B.SetRef(view, "_claimButton", claim);
            return root;
        }

        /****************************************
        *               CharDetail
        ****************************************/

        private static GameObject BuildCharDetail(string rootName)
        {
            GameObject root = B.CreateCanvasRoot(rootName);
            Image window = B.CreateWindow(root.transform, "CharDetailWindow", CHAR_WIDTH, CHAR_HEIGHT);
            Transform t = window.transform;
            Button close = AddHeader(t, "캐릭터 상세", CHAR_WIDTH);

            TMP_Text nameLabel  = B.CreateLabel(t, "NameLabel", string.Empty, 300f, B.GAP, 200f, B.LABEL_HEIGHT + B.GAP, B.FONT_SIZE);
            TMP_Text classLabel = B.CreateLabel(t, "ClassLabel", string.Empty, 300f, NOTICE_Y, 200f, B.LABEL_HEIGHT, B.SMALL_FONT);

            TMP_Text[] statLabels = new TMP_Text[STAT_COUNT];
            for (int i = 0; i < STAT_COUNT; ++i)
            {
                float y = CONTENT_TOP + (CHAR_LINE_HEIGHT * i);
                B.CreateLabel(t, "StatCaption" + i, G_StatCaptions[i], B.MARGIN, y, 90f, B.LABEL_HEIGHT, B.SMALL_FONT);
                statLabels[i] = B.CreateLabel(t, "StatValue" + i, string.Empty, 110f, y, 150f, B.LABEL_HEIGHT);
            }

            TMP_Text[] slotNames = new TMP_Text[SLOT_COUNT];
            TMP_Text[] slotEnhances = new TMP_Text[SLOT_COUNT];
            for (int i = 0; i < SLOT_COUNT; ++i)
            {
                float y = CONTENT_TOP + (CHAR_LINE_HEIGHT * i);
                slotNames[i]    = B.CreateLabel(t, "SlotName" + i, string.Empty, 300f, y, 260f, B.LABEL_HEIGHT);
                slotEnhances[i] = B.CreateLabel(t, "SlotEnhance" + i, string.Empty, 568f, y, 170f, B.LABEL_HEIGHT);
            }

            float trainingTop = CONTENT_TOP + (CHAR_LINE_HEIGHT * SLOT_COUNT) + B.MARGIN;
            TMP_Text[] training = new TMP_Text[TRAINING_COUNT];
            for (int i = 0; i < TRAINING_COUNT; ++i)
            {
                float y = trainingTop + (CHAR_LINE_HEIGHT * i);
                B.CreateLabel(t, "TrainingCaption" + i, G_TrainingCaptions[i], B.MARGIN, y, 90f, B.LABEL_HEIGHT, B.SMALL_FONT);
                training[i] = B.CreateLabel(t, "TrainingValue" + i, string.Empty, 110f, y, 400f, B.LABEL_HEIGHT);
            }

            float talismanY = trainingTop + (CHAR_LINE_HEIGHT * TRAINING_COUNT) + B.MARGIN;
            TMP_Text talisman = B.CreateLabel(t, "TalismanLabel", string.Empty, B.MARGIN, talismanY, CHAR_WIDTH - (B.MARGIN * 2f), B.LABEL_HEIGHT);
            GameObject preview = B.CreateRect(t, "PreviewStage", 520f, trainingTop, B.MARGIN, B.MARGIN).gameObject;

            CharDetailView view = window.gameObject.AddComponent<CharDetailView>();
            B.SetRef(view, "_closeButton", close);
            B.SetRef(view, "_nameLabel", nameLabel);
            B.SetRef(view, "_classLabel", classLabel);
            B.SetRef(view, "_attackLabel", statLabels[0]);
            B.SetRef(view, "_healthLabel", statLabels[1]);
            B.SetRef(view, "_defenseLabel", statLabels[2]);
            B.SetRef(view, "_critLabel", statLabels[3]);
            B.SetRef(view, "_energyLabel", statLabels[4]);
            B.SetRef(view, "_speedLabel", statLabels[5]);
            B.SetRefs<TMP_Text>(view, "_slotNameLabels", slotNames);
            B.SetRefs<TMP_Text>(view, "_slotEnhanceLabels", slotEnhances);
            B.SetRefs<TMP_Text>(view, "_trainingLabels", training);
            B.SetRef(view, "_talismanLabel", talisman);
            B.SetRef(view, "_previewStage", preview);
            return root;
        }

        /****************************************
        *                Auction
        ****************************************/

        private static GameObject BuildAuction(string rootName)
        {
            GameObject root = B.CreateCanvasRoot(rootName);
            Image window = B.CreateWindow(root.transform, "AuctionWindow", AUCTION_WIDTH, AUCTION_HEIGHT);
            Transform t = window.transform;
            Button close = AddHeader(t, "경매장", AUCTION_WIDTH);
            TMP_Text status = AddStatus(t, AUCTION_HEIGHT, AUCTION_WIDTH);
            TMP_Text yeopjeon = B.CreateLabel(t, "YeopjeonLabel", string.Empty, 600f, B.GAP, 300f, B.LABEL_HEIGHT + B.GAP,
                B.FONT_SIZE, TextAlignmentOptions.MidlineRight);

            RectTransform tabRoot = B.CreateRect(t, "Tabs", B.MARGIN, CONTENT_TOP, AUCTION_TAB_WIDTH * AUCTION_TAB_COUNT, B.BUTTON_HEIGHT);
            ToggleGroup tabGroup = tabRoot.gameObject.AddComponent<ToggleGroup>();
            Toggle[] tabs = new Toggle[AUCTION_TAB_COUNT];
            GameObject[] pages = new GameObject[AUCTION_TAB_COUNT];
            for (int i = 0; i < AUCTION_TAB_COUNT; ++i)
            {
                tabs[i] = B.CreateToggle(tabRoot, "Tab" + i, G_AuctionTabNames[i], AUCTION_TAB_WIDTH * i, 0f, AUCTION_TAB_WIDTH - B.GAP, B.BUTTON_HEIGHT, tabGroup);
                pages[i] = B.CreateRect(t, "Page" + i, B.MARGIN, AUCTION_PAGE_TOP, AUCTION_PAGE_WIDTH, AUCTION_PAGE_HEIGHT).gameObject;
            }

            tabs[0].SetIsOnWithoutNotify(true);

            AuctionView view = window.gameObject.AddComponent<AuctionView>();
            B.SetRef(view, "_closeButton", close);
            B.SetRef(view, "_statusLabel", status);
            B.SetRef(view, "_yeopjeonLabel", yeopjeon);
            B.SetRefs<Toggle>(view, "_tabs", tabs);
            B.SetRefs<GameObject>(view, "_pages", pages);
            BuildAuctionBuyPage(view, pages[0].transform);
            BuildAuctionSellPage(view, pages[1].transform);
            BuildAuctionMinePage(view, pages[2].transform);
            BuildAuctionHistoryPage(view, pages[3].transform);
            return root;
        }

        private static void BuildAuctionBuyPage(AuctionView view, Transform page)
        {
            TMP_InputField search = B.CreateInput(page, "SearchField", "품목 이름", 0f, 0f, 240f, B.BUTTON_HEIGHT);
            Button searchButton = B.CreateButton(page, "SearchButton", "검색", 248f, 0f, SMALL_BUTTON_WIDTH, B.BUTTON_HEIGHT);

            RectTransform categoryRoot = B.CreateRect(page, "Categories", 336f, 0f, 280f, B.BUTTON_HEIGHT);
            ToggleGroup categoryGroup = categoryRoot.gameObject.AddComponent<ToggleGroup>();
            Toggle[] categories = new Toggle[G_CategoryNames.Length];
            for (int i = 0; i < categories.Length; ++i)
            {
                categories[i] = B.CreateToggle(categoryRoot, "Category" + i, G_CategoryNames[i], 70f * i, 0f, 66f, B.BUTTON_HEIGHT, categoryGroup);
            }

            categories[0].SetIsOnWithoutNotify(true);
            TMP_Dropdown classDropdown = B.CreateDropdown(page, "ClassDropdown", G_ClassOptions, 624f, 0f, 100f, B.BUTTON_HEIGHT);
            TMP_Dropdown gradeDropdown = B.CreateDropdown(page, "GradeDropdown", G_GradeOptions, 730f, 0f, 100f, B.BUTTON_HEIGHT);
            TMP_Dropdown sortDropdown  = B.CreateDropdown(page, "SortDropdown", G_SortOptions, 836f, 0f, 110f, B.BUTTON_HEIGHT);
            Button order = B.CreateButton(page, "OrderButton", "정렬", 952f, 0f, 60f, B.BUTTON_HEIGHT);
            GameObject ascending  = B.CreateMark(page, "AscendingMark", "오름", 1016f, 0f, 52f, B.BUTTON_HEIGHT);
            GameObject descending = B.CreateMark(page, "DescendingMark", "내림", 1016f, 0f, 52f, B.BUTTON_HEIGHT);

            ListRowView[] groups = B.CreateRows(page, "GroupRow", AUCTION_GROUP_ROWS, 5, 0f, AUCTION_LIST_TOP, AUCTION_GROUP_WIDTH, B.ROW_HEIGHT, B.GAP);
            GameObject groupsEmpty = B.CreateMark(page, "GroupsEmptyMark", string.Empty, 0f, AUCTION_LIST_TOP, AUCTION_GROUP_WIDTH, B.ROW_HEIGHT);
            float below = AUCTION_LIST_TOP + (RowStride() * AUCTION_GROUP_ROWS);
            TMP_Text resultCount = B.CreateLabel(page, "ResultCountLabel", string.Empty, 0f, below, COUNT_LABEL_WIDTH, B.LABEL_HEIGHT, B.SMALL_FONT);
            AddPager(page, 0f, below + B.LABEL_HEIGHT + B.GAP, out Button prev, out TMP_Text pageLabel, out Button next);

            GameObject detail = B.CreateSection(page, "DetailPanel", AUCTION_DETAIL_X, AUCTION_LIST_TOP, AUCTION_DETAIL_WIDTH, AUCTION_DETAIL_HEIGHT).gameObject;
            float inner = AUCTION_DETAIL_WIDTH - (B.MARGIN * 2f);
            TMP_Text detailTitle = B.CreateLabel(detail.transform, "DetailTitle", string.Empty, B.MARGIN, B.GAP, inner, B.LABEL_HEIGHT + B.GAP, B.FONT_SIZE);
            TMP_Text average = B.CreateLabel(detail.transform, "AveragePrice", string.Empty, B.MARGIN, 40f, inner, B.LABEL_HEIGHT, B.SMALL_FONT);
            ListRowView[] listings = B.CreateRows(detail.transform, "ListingRow", AUCTION_LISTING_ROWS, 3, B.MARGIN, 72f, inner, B.ROW_HEIGHT, B.GAP);
            float buyY = 72f + (RowStride() * AUCTION_LISTING_ROWS) + B.GAP;
            Button buyMinus = B.CreateButton(detail.transform, "BuyMinus", "-", B.MARGIN, buyY, STEP_WIDTH, B.BUTTON_HEIGHT);
            TMP_InputField buyQuantity = B.CreateInput(detail.transform, "BuyQuantity", "수량", B.MARGIN + STEP_WIDTH + B.GAP, buyY, FIELD_WIDTH, B.BUTTON_HEIGHT);
            Button buyPlus = B.CreateButton(detail.transform, "BuyPlus", "+", B.MARGIN + STEP_WIDTH + FIELD_WIDTH + (B.GAP * 2f), buyY, STEP_WIDTH, B.BUTTON_HEIGHT);
            TMP_Text buyTotal = B.CreateLabel(detail.transform, "BuyTotal", string.Empty, 224f, buyY, 190f, B.BUTTON_HEIGHT);
            Button buy = B.CreateButton(detail.transform, "BuyButton", "구매", B.MARGIN, buyY + B.BUTTON_HEIGHT + B.GAP, inner, WIDE_BUTTON_HEIGHT);

            B.SetRef(view, "_searchField", search);
            B.SetRef(view, "_searchButton", searchButton);
            B.SetRefs<Toggle>(view, "_categoryToggles", categories);
            B.SetRef(view, "_classDropdown", classDropdown);
            B.SetRef(view, "_gradeDropdown", gradeDropdown);
            B.SetRef(view, "_sortDropdown", sortDropdown);
            B.SetRef(view, "_orderButton", order);
            B.SetRef(view, "_ascendingMark", ascending);
            B.SetRef(view, "_descendingMark", descending);
            B.SetRefs<ListRowView>(view, "_groupRows", groups);
            B.SetRef(view, "_resultCountLabel", resultCount);
            B.SetRef(view, "_groupsEmptyMark", groupsEmpty);
            B.SetRef(view, "_prevPageButton", prev);
            B.SetRef(view, "_nextPageButton", next);
            B.SetRef(view, "_pageLabel", pageLabel);
            B.SetRef(view, "_detailPanel", detail);
            B.SetRef(view, "_detailTitleLabel", detailTitle);
            B.SetRef(view, "_averagePriceLabel", average);
            B.SetRefs<ListRowView>(view, "_listingRows", listings);
            B.SetRef(view, "_buyQuantityField", buyQuantity);
            B.SetRef(view, "_buyQuantityMinus", buyMinus);
            B.SetRef(view, "_buyQuantityPlus", buyPlus);
            B.SetRef(view, "_buyTotalLabel", buyTotal);
            B.SetRef(view, "_buyButton", buy);
        }

        private static void BuildAuctionSellPage(AuctionView view, Transform page)
        {
            ListRowView[] sellables = B.CreateRows(page, "SellableRow", AUCTION_SELLABLE_ROWS, 2, 0f, 0f, 420f, B.ROW_HEIGHT, B.GAP);
            float x = 440f;
            TMP_Text sellItem = B.CreateLabel(page, "SellItemLabel", string.Empty, x, 0f, 500f, B.LABEL_HEIGHT + B.GAP, B.FONT_SIZE);
            Button minus = B.CreateButton(page, "SellMinus", "-", x, 44f, STEP_WIDTH, B.BUTTON_HEIGHT);
            TMP_InputField quantity = B.CreateInput(page, "SellQuantity", "수량", x + STEP_WIDTH + B.GAP, 44f, FIELD_WIDTH, B.BUTTON_HEIGHT);
            Button plus = B.CreateButton(page, "SellPlus", "+", x + STEP_WIDTH + FIELD_WIDTH + (B.GAP * 2f), 44f, STEP_WIDTH, B.BUTTON_HEIGHT);
            Button max = B.CreateButton(page, "SellMax", "최대", x + (STEP_WIDTH * 2f) + FIELD_WIDTH + (B.GAP * 3f), 44f, SMALL_BUTTON_WIDTH, B.BUTTON_HEIGHT);
            TMP_InputField price = B.CreateInput(page, "SellPrice", "개당 가격", x, 90f, 240f, B.BUTTON_HEIGHT);
            TMP_Text gross    = B.CreateLabel(page, "GrossLabel", string.Empty, x, 136f, 400f, B.LABEL_HEIGHT);
            TMP_Text fee      = B.CreateLabel(page, "FeeLabel", string.Empty, x, 166f, 400f, B.LABEL_HEIGHT);
            TMP_Text proceeds = B.CreateLabel(page, "ProceedsLabel", string.Empty, x, 196f, 400f, B.LABEL_HEIGHT);
            TMP_Text formError = B.CreateLabel(page, "FormErrorLabel", string.Empty, x, 230f, 600f, B.LABEL_HEIGHT, B.SMALL_FONT);
            formError.color = B.MarkColor;
            Button register = B.CreateButton(page, "RegisterButton", "등록", x, 268f, 240f, WIDE_BUTTON_HEIGHT);

            B.SetRefs<ListRowView>(view, "_sellableRows", sellables);
            B.SetRef(view, "_sellItemLabel", sellItem);
            B.SetRef(view, "_sellQuantityField", quantity);
            B.SetRef(view, "_sellQuantityMinus", minus);
            B.SetRef(view, "_sellQuantityPlus", plus);
            B.SetRef(view, "_sellQuantityMax", max);
            B.SetRef(view, "_sellPriceField", price);
            B.SetRef(view, "_grossLabel", gross);
            B.SetRef(view, "_feeLabel", fee);
            B.SetRef(view, "_proceedsLabel", proceeds);
            B.SetRef(view, "_formErrorLabel", formError);
            B.SetRef(view, "_registerButton", register);
        }

        private static void BuildAuctionMinePage(AuctionView view, Transform page)
        {
            ListRowView[] mine = B.CreateRows(page, "MineRow", AUCTION_MINE_ROWS, 5, 0f, 0f, AUCTION_WIDE_ROW_WIDTH, B.ROW_HEIGHT, B.GAP);
            Button[] cancels = new Button[AUCTION_MINE_ROWS];
            for (int i = 0; i < AUCTION_MINE_ROWS; ++i)
            {
                cancels[i] = B.CreateButton(page, "CancelButton" + i, "취소", AUCTION_WIDE_ROW_WIDTH + B.GAP, RowStride() * i, 100f, B.ROW_HEIGHT);
            }

            GameObject empty = B.CreateMark(page, "MineEmptyMark", string.Empty, 0f, 0f, AUCTION_WIDE_ROW_WIDTH, B.ROW_HEIGHT);
            Button ack = B.CreateButton(page, "AckButton", "확인", 0f, (RowStride() * AUCTION_MINE_ROWS) + B.GAP, BUTTON_WIDTH, B.BUTTON_HEIGHT);

            B.SetRefs<ListRowView>(view, "_mineRows", mine);
            B.SetRefs<Button>(view, "_cancelButtons", cancels);
            B.SetRef(view, "_ackButton", ack);
            B.SetRef(view, "_mineEmptyMark", empty);
        }

        private static void BuildAuctionHistoryPage(AuctionView view, Transform page)
        {
            ListRowView[] history = B.CreateRows(page, "HistoryRow", AUCTION_HISTORY_ROWS, 6, 0f, 0f, AUCTION_HISTORY_ROW_WIDTH, B.ROW_HEIGHT, B.GAP);
            GameObject empty = B.CreateMark(page, "HistoryEmptyMark", string.Empty, 0f, 0f, AUCTION_HISTORY_ROW_WIDTH, B.ROW_HEIGHT);
            AddPager(page, 0f, (RowStride() * AUCTION_HISTORY_ROWS) + B.GAP, out Button prev, out TMP_Text pageLabel, out Button next);

            B.SetRefs<ListRowView>(view, "_historyRows", history);
            B.SetRef(view, "_historyEmptyMark", empty);
            B.SetRef(view, "_historyPrevButton", prev);
            B.SetRef(view, "_historyNextButton", next);
            B.SetRef(view, "_historyPageLabel", pageLabel);
        }

        /****************************************
        *                 Trade
        ****************************************/

        private static GameObject BuildTrade(string rootName)
        {
            GameObject root = B.CreateCanvasRoot(rootName);
            Image window = B.CreateWindow(root.transform, "TradeWindow", TRADE_WIDTH, TRADE_HEIGHT);
            Transform t = window.transform;
            Button close = AddHeader(t, "1:1 거래", TRADE_WIDTH);
            TMP_Text status = AddStatus(t, TRADE_HEIGHT, TRADE_WIDTH);
            TMP_Text partner = B.CreateLabel(t, "PartnerName", string.Empty, 420f, B.GAP, 300f, B.LABEL_HEIGHT + B.GAP);
            TMP_Text countdown = B.CreateLabel(t, "Countdown", string.Empty, 730f, B.GAP, 150f, B.LABEL_HEIGHT + B.GAP);
            GameObject busy = B.CreateMark(t, "BusyMark", "처리 중", 730f, TRADE_HEIGHT - B.MARGIN - STATUS_HEIGHT, 200f, STATUS_HEIGHT);

            B.CreateLabel(t, "MyCaption", "내 제안", B.MARGIN, CONTENT_TOP, TRADE_OFFER_WIDTH, B.LABEL_HEIGHT, B.SMALL_FONT);
            B.CreateLabel(t, "PartnerCaption", "상대 제안", TRADE_PARTNER_X, CONTENT_TOP, TRADE_OFFER_WIDTH, B.LABEL_HEIGHT, B.SMALL_FONT);
            B.CreateLabel(t, "InventoryCaption", "보관함", TRADE_INVENTORY_X, CONTENT_TOP, TRADE_INVENTORY_WIDTH, B.LABEL_HEIGHT, B.SMALL_FONT);

            ListRowView[] mine = B.CreateRows(t, "MyRow", TRADE_OFFER_ROWS, TRADE_COLUMNS, B.MARGIN, TRADE_LIST_TOP, TRADE_OFFER_WIDTH, B.ROW_HEIGHT, B.GAP);
            float below = TRADE_LIST_TOP + (RowStride() * TRADE_OFFER_ROWS) + B.GAP;
            TMP_InputField currency = B.CreateInput(t, "MyCurrency", "엽전", B.MARGIN, below, 200f, B.BUTTON_HEIGHT);
            GameObject myLocked = B.CreateMark(t, "MyLockedMark", "내 확정", B.MARGIN, below + B.BUTTON_HEIGHT + B.GAP, 90f, B.LABEL_HEIGHT);
            GameObject myAccepted = B.CreateMark(t, "MyAcceptedMark", "내 수락", B.MARGIN + 100f, below + B.BUTTON_HEIGHT + B.GAP, 90f, B.LABEL_HEIGHT);

            ListRowView[] partnerRows = B.CreateRows(t, "PartnerRow", TRADE_OFFER_ROWS, TRADE_COLUMNS, TRADE_PARTNER_X, TRADE_LIST_TOP, TRADE_OFFER_WIDTH, B.ROW_HEIGHT, B.GAP);
            TMP_Text partnerCurrency = B.CreateLabel(t, "PartnerCurrency", string.Empty, TRADE_PARTNER_X, below, 200f, B.BUTTON_HEIGHT);
            GameObject partnerLocked = B.CreateMark(t, "PartnerLockedMark", "상대 확정", TRADE_PARTNER_X, below + B.BUTTON_HEIGHT + B.GAP, 90f, B.LABEL_HEIGHT);
            GameObject partnerAccepted = B.CreateMark(t, "PartnerAcceptedMark", "상대 수락", TRADE_PARTNER_X + 100f, below + B.BUTTON_HEIGHT + B.GAP, 90f, B.LABEL_HEIGHT);

            ListRowView[] inventory = B.CreateRows(t, "InventoryRow", TRADE_INVENTORY_ROWS, TRADE_COLUMNS, TRADE_INVENTORY_X, TRADE_LIST_TOP, TRADE_INVENTORY_WIDTH, B.ROW_HEIGHT, B.GAP);
            TMP_Text yeopjeon = B.CreateLabel(t, "InventoryYeopjeon", string.Empty, TRADE_INVENTORY_X, TRADE_LIST_TOP + (RowStride() * TRADE_INVENTORY_ROWS) + B.GAP,
                TRADE_INVENTORY_WIDTH, B.LABEL_HEIGHT);

            float buttonY = TRADE_HEIGHT - B.MARGIN - STATUS_HEIGHT - B.GAP - WIDE_BUTTON_HEIGHT;
            Button lockButton = B.CreateButton(t, "LockButton", "확정", B.MARGIN, buttonY, BUTTON_WIDTH, WIDE_BUTTON_HEIGHT);
            Button accept = B.CreateButton(t, "AcceptButton", "수락", B.MARGIN + BUTTON_WIDTH + B.GAP, buttonY, BUTTON_WIDTH, WIDE_BUTTON_HEIGHT);
            Button cancel = B.CreateButton(t, "CancelButton", "취소", B.MARGIN + ((BUTTON_WIDTH + B.GAP) * 2f), buttonY, BUTTON_WIDTH, WIDE_BUTTON_HEIGHT);

            TradeView view = window.gameObject.AddComponent<TradeView>();
            B.SetRef(view, "_closeButton", close);
            B.SetRef(view, "_partnerNameLabel", partner);
            B.SetRef(view, "_statusLabel", status);
            B.SetRef(view, "_countdownLabel", countdown);
            B.SetRef(view, "_busyMark", busy);
            B.SetRefs<ListRowView>(view, "_myRows", mine);
            B.SetRef(view, "_myCurrencyField", currency);
            B.SetRef(view, "_myLockedMark", myLocked);
            B.SetRef(view, "_myAcceptedMark", myAccepted);
            B.SetRefs<ListRowView>(view, "_partnerRows", partnerRows);
            B.SetRef(view, "_partnerCurrencyLabel", partnerCurrency);
            B.SetRef(view, "_partnerLockedMark", partnerLocked);
            B.SetRef(view, "_partnerAcceptedMark", partnerAccepted);
            B.SetRefs<ListRowView>(view, "_inventoryRows", inventory);
            B.SetRef(view, "_yeopjeonLabel", yeopjeon);
            B.SetRef(view, "_lockButton", lockButton);
            B.SetRef(view, "_lockButtonLabel", lockButton.GetComponentInChildren<TMP_Text>(true));
            B.SetRef(view, "_acceptButton", accept);
            B.SetRef(view, "_cancelButton", cancel);
            B.SetRef(view, "_cancelButtonLabel", cancel.GetComponentInChildren<TMP_Text>(true));
            return root;
        }
    }
}
