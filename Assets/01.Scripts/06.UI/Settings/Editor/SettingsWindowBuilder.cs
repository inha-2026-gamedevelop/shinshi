#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Unity
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI.Editor
{
    /// <summary>설정 배치 JSON을 기능이 연결된 개발 씬과 재사용 프리팹으로 만든다</summary>
    public static class SettingsWindowBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SCENE_PATH = "Assets/00.Scenes/Dev_HUD/Dev_Setting.unity";
        private const string PREFAB_PATH = "Assets/02.Prefab/UI/Settings/PF_SettingsWindow.prefab";
        private const string FONT_FOLDER = "Assets/01.Scripts/06.UI/Settings/Fonts";
        private const string SOURCE_FONT_FOLDER = "Assets/01.Scripts/06.UI/Hud/Fonts/NanumGothic/Fonts";
        private const string SKIN_FOLDER = "Assets/01.Scripts/05.Art/UI/GameStyle/Textures/";
        private const float WINDOW_WIDTH  = 1200f;
        private const float WINDOW_HEIGHT = 780f;
        private const float ROW_WIDTH     = 780f;
        private const float ROW_HEIGHT    = 62f;

        // 게임 탭은 창 높이(430)를 넘어 스크롤한다. 안내 문구는 맨 아래에 둔다
        private const float GAME_CONTENT_HEIGHT     = 620f;
        private const float GAME_NOTICE_Y           = 576f;
        private const float GAME_SCROLL_SENSITIVITY = 40f;

        private static readonly Color G_Window  = new Color(0.065f, 0.083f, 0.102f, 1f);
        private static readonly Color G_Sidebar = new Color(0.048f, 0.067f, 0.082f, 1f);
        private static readonly Color G_Row     = new Color(0.095f, 0.118f, 0.14f, 1f);
        private static readonly Color G_Control = new Color(0.055f, 0.075f, 0.095f, 1f);
        private static readonly Color G_Border  = new Color(0.29f, 0.32f, 0.34f, 1f);
        private static readonly Color G_Gold    = new Color(0.68f, 0.6f, 0.43f, 1f);
        private static readonly Color G_Accent    = new Color(0.84f, 0.75f, 0.57f, 1f);
        private static readonly Color G_Text    = new Color(0.9f, 0.92f, 0.92f, 1f);
        private static readonly Color G_Muted   = new Color(0.64f, 0.69f, 0.71f, 1f);

        private static readonly Dictionary<string, RectTransform> G_Nodes = new Dictionary<string, RectTransform>();
        private static TMP_FontAsset G_Regular;
        private static TMP_FontAsset G_Bold;
        private static SettingsView G_View;

        /****************************************
        *                 Menu
        ****************************************/

        [MenuItem("Shinshi/UI/설정 창 만들기")]
        private static void Open()
        {
            string path = EditorUtility.OpenFilePanel("설정 창 배치 JSON", string.Empty, "json");
            if (!string.IsNullOrEmpty(path))
            {
                Build(path);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>Dev_Setting 씬에서만 JSON을 읽어 설정 창을 구성한다</summary>
        public static string Build(string jsonPath)
        {
            if (EditorApplication.isPlaying)
            {
                throw new InvalidOperationException("설정 창 제작은 편집 모드에서 실행합니다");
            }
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != SCENE_PATH)
            {
                throw new InvalidOperationException("Dev_Setting 씬을 열고 실행합니다");
            }
            string json = File.ReadAllText(jsonPath);
            UiLayoutImporter.Inspect(json);
            EnsureFolder(FONT_FOLDER);
            EnsureFolder(Path.GetDirectoryName(PREFAB_PATH).Replace('\\', '/'));
            G_Regular = EnsureFont("Regular");
            G_Bold = EnsureFont("Bold");

            GameObject existing = GameObject.Find("SettingsCanvas");
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }
            RectTransform canvas = UiLayoutImporter.CreateCanvas("SettingsCanvas");
            canvas.GetComponent<Canvas>().sortingOrder = 100;
            UiLayoutImporter.Report report = UiLayoutImporter.Apply(json, canvas, new UiLayoutImporter.Options());
            RectTransform root = (RectTransform)report.Root.transform;
            G_Nodes.Clear();
            foreach (UiLayoutNote note in root.GetComponentsInChildren<UiLayoutNote>(true))
            {
                G_Nodes[note.LayoutId] = (RectTransform)note.transform;
            }
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                image.color = Color.clear;
                image.raycastTarget = false;
            }
            G_View = root.gameObject.AddComponent<SettingsView>();
            Image shade = Panel(root, "ModalShade", 0f, 0f, 0f, 0f, new Color(0f, 0f, 0f, 0.6f));
            Stretch(shade.rectTransform);
            shade.raycastTarget = true;
            shade.transform.SetAsFirstSibling();

            BuildFrame();
            BuildNavigation();
            BuildGraphics();
            BuildAudio();
            BuildGame();
            BuildFooter();
            PreloadCharacters(root);

            PrefabUtility.SaveAsPrefabAsset(canvas.gameObject, PREFAB_PATH);
            BuildPreviewHelpers(G_View);
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssetIfDirty(G_Regular);
            AssetDatabase.SaveAssetIfDirty(G_Bold);
            Selection.activeGameObject = G_View.gameObject;
            return $"설정 창 제작 완료: {report.Created}개 배치 요소, {PREFAB_PATH}";
        }

        private static void BuildFrame()
        {
            RectTransform window = Node("win");
            window.sizeDelta = new Vector2(WINDOW_WIDTH, WINDOW_HEIGHT);
            SetRect(Node("bg"), 0f, 0f, WINDOW_WIDTH, WINDOW_HEIGHT);
            Node("left").sizeDelta = new Vector2(274f, 610f);
            Node("right").sizeDelta = new Vector2(860f, 610f);
            SetRect(Node("hint"), 320f, 620f, 860f, 92f);
            SetRect(Node("bottom"), 24f, 722f, 1152f, 44f);
            Image shadow = Panel(window, "WindowShadow", -12f, -12f, WINDOW_WIDTH + 24f, WINDOW_HEIGHT + 24f, new Color(0f, 0f, 0f, 0.35f));
            shadow.transform.SetAsFirstSibling();
            Image background = Fill("bg", G_Window);
            background.raycastTarget = true;
            Skin(background, "T_UiWindow_Frame", Color.white);
            Image header = Panel(window, "HeaderSurface", 1f, 1f, WINDOW_WIDTH - 2f, 97f, Color.white);
            Skin(header, "T_UiWindow_TitleBar", new Color(0.7f, 0.8f, 0.9f, 0.8f));
            header.transform.SetSiblingIndex(2);
            Text(Node("ttl"), "환경설정", 32f, G_Text, true);
            Fill("ttl-deco", Color.clear);
            Panel(Node("ttl-deco"), "LeftLine", 0f, 4f, 28f, 1f, G_Gold);
            Glyph(Node("ttl-deco"), "TitleDiamond", 36f, 0f, 8f, SettingsGlyph.EShape.Diamond, G_Gold);
            Panel(Node("ttl-deco"), "RightLine", 52f, 4f, 28f, 1f, G_Gold);
            Button close = Button("close", string.Empty, G_Control);
            Glyph(Node("close"), "CloseGlyph", 8f, 8f, 14f, SettingsGlyph.EShape.Close, G_Muted);
            Link("_close", close);

            Image dragSurface = Panel(Node("titlebar"), "DragSurface", 0f, 0f, 1140f, 50f, Color.clear);
            dragSurface.raycastTarget = true;
            dragSurface.transform.SetAsFirstSibling();
            UiWindowDragHandle drag = dragSurface.gameObject.AddComponent<UiWindowDragHandle>();
            SetReference(drag, "_window", window);

            Fill("tab-line", new Color(G_Gold.r, G_Gold.g, G_Gold.b, 0.24f));
            Fill("tab-mark", G_Accent);
            SetRect(Node("tab-mark"), 435f, 42f, 140f, 2f);
            SetRect(Node("tab-opt"), 425f, 0f, 160f, 44f);
            SetRect(Node("tab-sys"), 615f, 4f, 160f, 38f);
            Hide("tab-guide");
            Text(Node("tab-opt"), "옵션", 24f, G_Accent, true);
            Text(Node("tab-sys"), "시스템", 19f, new Color(0.4f, 0.45f, 0.48f, 1f));
            Hide("key-q");
            Hide("key-e");

            Image sidebar = Panel(Node("left"), "SidebarSurface", 0f, 0f, 274f, 610f, G_Sidebar);
            Skin(sidebar, "T_UiPanel_Inset", new Color(0.8f, 0.9f, 1f, 1f));
            sidebar.transform.SetAsFirstSibling();
            Border(sidebar.rectTransform, new Color(G_Border.r, G_Border.g, G_Border.b, 0.5f), 1f);
            Fill("fav", Color.clear);
            Text(Node("fav"), "옵션 분류", 16f, G_Gold, true, TextAlignmentOptions.Left);
            Panel(Node("fav"), "NavigationLine", 16f, 41f, 242f, 1f, G_Border);
            Hide("catscroll");
            Hide("cat-play");
            SetRect(Node("catlist"), 12f, 58f, 250f, 600f);
            SetRect(Node("cat-gfx"), 0f, 0f, 250f, 52f);
            SetRect(Node("sub-screen"), 0f, 60f, 250f, 40f);
            SetRect(Node("sub-frame"), 0f, 104f, 250f, 40f);
            SetRect(Node("cat-aud"), 0f, 162f, 250f, 52f);
            RectTransform gameNode = Rect(Node("catlist"), "GameCategory", 0f, 222f, 250f, 52f);
            G_Nodes["cat-game"] = gameNode;
            Hide("cat-gfx-a");
            Hide("cat-aud-a");
            Node("cat-gfx-t").gameObject.SetActive(false);
            Node("cat-aud-t").gameObject.SetActive(false);
            Node("cat-gfx-bg").gameObject.SetActive(false);
            Node("cat-aud-bg").gameObject.SetActive(false);
            Button graphics = Button("cat-gfx", "그래픽", G_Control, TextAlignmentOptions.Left);
            Button audio = Button("cat-aud", "오디오", G_Control, TextAlignmentOptions.Left);
            Button game = Button("cat-game", "게임", G_Control, TextAlignmentOptions.Left);
            TMP_Text graphicsLabel = graphics.GetComponentInChildren<TMP_Text>();
            TMP_Text audioLabel = audio.GetComponentInChildren<TMP_Text>();
            graphicsLabel.margin = new Vector4(56f, 0f, 0f, 0f);
            audioLabel.margin = new Vector4(56f, 0f, 0f, 0f);
            TMP_Text gameLabel = game.GetComponentInChildren<TMP_Text>();
            gameLabel.margin = new Vector4(56f, 0f, 0f, 0f);
            Glyph(Node("cat-gfx"), "MonitorGlyph", 17f, 14f, 24f, SettingsGlyph.EShape.Monitor, G_Accent);
            Glyph(Node("cat-aud"), "AudioGlyph", 17f, 14f, 24f, SettingsGlyph.EShape.Speaker, G_Accent);
            Glyph(Node("cat-game"), "GameGlyph", 17f, 14f, 24f, SettingsGlyph.EShape.Gamepad, G_Accent);
            Image gameEdge = Panel(Node("cat-game"), "SelectionEdge", 0f, 0f, 2f, 52f, G_Accent);
            gameEdge.gameObject.SetActive(false);
            Image graphicsEdge = Panel(Node("cat-gfx"), "SelectionEdge", 0f, 0f, 2f, 52f, G_Accent);
            Image audioEdge = Panel(Node("cat-aud"), "SelectionEdge", 0f, 0f, 2f, 52f, G_Accent);
            audioEdge.gameObject.SetActive(false);
            Link("_graphicsIndicator", graphicsEdge.gameObject);
            Link("_audioIndicator", audioEdge.gameObject);
            Link("_gameIndicator", gameEdge.gameObject);
            LinkArray("_graphicsLabels", new UnityEngine.Object[] { graphicsLabel });
            LinkArray("_audioLabels", new UnityEngine.Object[] { audioLabel });
            LinkArray("_gameLabels", new UnityEngine.Object[] { gameLabel });
            Hide("sub-screen-bg");
            Hide("sub-frame-bg");
            Text(Node("sub-screen-t"), "화면 설정", 17f, G_Muted, false, TextAlignmentOptions.Left);
            Text(Node("sub-frame-t"), "프레임 설정", 17f, G_Muted, false, TextAlignmentOptions.Left);
            UnityEventTools.AddPersistentListener(Button("sub-screen", string.Empty, Color.clear).onClick, G_View.ShowGraphics);
            UnityEventTools.AddPersistentListener(Button("sub-frame", string.Empty, Color.clear).onClick, G_View.ShowGraphics);
            LinkArray("_graphicsNavigation", new UnityEngine.Object[] { graphics });
            LinkArray("_audioNavigation", new UnityEngine.Object[] { audio });
            LinkArray("_gameNavigation", new UnityEngine.Object[] { game });
            Link("_graphicsHighlight", graphics.GetComponent<Image>());
            Link("_audioHighlight", audio.GetComponent<Image>());
            Link("_gameHighlight", game.GetComponent<Image>());
            Panel(Node("catlist"), "TreeGuide", 16f, 66f, 1f, 68f, new Color(G_Accent.r, G_Accent.g, G_Accent.b, 0.25f));
            Panel(Node("left"), "SidebarFooterRule", 20f, 460f, 234f, 1f, G_Border);
            Label(Node("left"), "SidebarFooterTitle", "설정 변경", 20f, 482f, 234f, 24f, 16f, G_Accent, true, TextAlignmentOptions.Left);
            Label(Node("left"), "SidebarFooterBody", "변경한 설정은 적용 버튼을\n눌러 저장합니다.", 20f, 516f, 234f, 52f, 15f, G_Muted, false, TextAlignmentOptions.TopLeft);
            Label(Node("left"), "KeyboardHint", "Q / E  분류 전환", 20f, 578f, 234f, 24f, 14f, G_Muted, false, TextAlignmentOptions.Left);
        }

        private static void BuildNavigation()
        {
            TMP_Text title = Text(Node("r-title"), "그래픽", 28f, G_Text, true, TextAlignmentOptions.Left);
            Link("_categoryTitle", title);
            Link("_categoryDetail", Label(Node("right"), "CategoryDescription", "내 화면에 맞는 플레이 환경을 선택합니다", 4f, 38f, 650f, 25f, 15f, G_Muted, false, TextAlignmentOptions.Left));
            SetRect(Node("r-div"), 4f, 68f, 796f, 1f);
            Fill("r-div", G_Border);
            Button reset = Button("btn-reset", "초기화", G_Control);
            SetRect(Node("btn-reset"), 690f, 8f, 110f, 36f);
            Link("_reset", reset);

            RectTransform viewport = Node("scroll");
            SetRect(viewport, 4f, 80f, 796f, 430f);
            viewport.gameObject.AddComponent<RectMask2D>();
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.viewport = viewport;
            RectTransform content = Rect(viewport, "SettingsContent", 0f, 0f, 796f, 430f);
            scroll.content = content;
            RectTransform graphics = Rect(content, "GraphicsPage", 0f, 0f, 796f, 430f);
            RectTransform audio = Rect(content, "AudioPage", 0f, 0f, 796f, 430f);
            RectTransform game = Rect(content, "GamePage", 0f, 0f, 796f, 430f);
            foreach (Transform child in new List<Transform>(Children(viewport)))
            {
                if (child != content)
                {
                    child.SetParent(graphics, false);
                }
            }
            Link("_graphicsBody", graphics.gameObject);
            Link("_audioBody", audio.gameObject);
            Link("_gameBody", game.gameObject);
            G_Nodes["audio-page"] = audio;
            G_Nodes["game-page"] = game;
            audio.gameObject.SetActive(false);
            game.gameObject.SetActive(false);
            // 현재 항목 수는 한 화면에 들어가므로 장식 스크롤바 대신 실제 스크롤 영역만 둔다
            Hide("rscroll");
        }

        private static void BuildGraphics()
        {
            Section("sec-screen", "화면 설정", 16f);
            Section("sec-frame", "프레임 설정", 216f);
            SetRect(Node("div-1"), 16f, 204f, 780f, 1f);
            Fill("div-1", new Color(G_Border.r, G_Border.g, G_Border.b, 0.5f));
            Row("row-mode", "rbg-mode", "l-mode", "화면 모드", 62f,
                "게임 화면의 표시 방식을 선택합니다. 전체 화면, 테두리 없는 창, 창 모드를 지원합니다.");
            Row("row-res", "rbg-res", "l-res", "해상도", 132f,
                "모니터가 지원하는 해상도를 선택합니다. 주사율이 다른 같은 크기는 하나로 표시합니다.");
            Row("row-vsync", "rbg-vsync", "l-vsync", "수직 동기화", 256f,
                "모니터 주사율에 프레임을 맞춰 화면이 찢어지는 현상을 줄입니다. 켜면 최대 프레임 제한은 사용하지 않습니다.");
            Row("row-cap", "rbg-cap", "l-cap", "최대 프레임", 330f,
                "수직 동기화가 꺼져 있을 때 최대 프레임을 제한합니다. 제한 없음은 기기가 낼 수 있는 프레임을 사용합니다.");
            TMP_Dropdown mode = Dropdown("dd-mode", new[] { "전체 화면", "테두리 없는 창", "창 모드" });
            TMP_Dropdown resolution = Dropdown("dd-res", new[] { "현재 해상도" });
            TMP_Dropdown cap = Dropdown("dd-cap", new[] { "30 FPS", "60 FPS", "120 FPS", "144 FPS", "제한 없음" });
            cap.SetValueWithoutNotify(4);
            cap.interactable = false;
            SetRect(Node("dd-mode"), 420f, 10f, 344f, 42f);
            SetRect(Node("dd-res"), 420f, 10f, 344f, 42f);
            SetRect(Node("dd-cap"), 420f, 10f, 344f, 42f);
            CanvasGroup capGroup = Node("dd-cap").gameObject.AddComponent<CanvasGroup>();
            capGroup.alpha = 0.4f;
            Link("_frameCapGroup", capGroup);
            Link("_screenMode", mode);
            Link("_resolution", resolution);
            Link("_frameCap", cap);
            Toggle vsync = Checkbox(Node("row-vsync"), "VSyncToggle", 420f, 16f);
            vsync.isOn = true;
            Hide("c-vsync");
            Link("_vsync", vsync);
            Link("_vsyncState", Label(Node("row-vsync"), "VSyncState", "켜기", 466f, 13f, 110f, 36f, 21f, G_Text, false, TextAlignmentOptions.Left));
            SetRect(Node("note"), 36f, 404f, 740f, 26f);
            Text(Node("note"), "수직 동기화를 끄면 최대 프레임을 선택할 수 있습니다", 15f, G_Gold, false, TextAlignmentOptions.Left);
            Link("_vsyncNotice", Node("note").gameObject);
        }

        private static void BuildAudio()
        {
            RectTransform page = Node("audio-page");
            Label(page, "AudioSection", "볼륨 설정", 62f, 16f, 300f, 34f, 20f, G_Gold, true, TextAlignmentOptions.Left);
            Glyph(page, "AudioSectionMark", 20f, 25f, 12f, SettingsGlyph.EShape.Diamond, G_Gold);
            Slider master = VolumeRow(page, "Master", "마스터 볼륨", 62f, "모든 소리의 전체 크기를 조절합니다. BGM과 효과음의 상대적인 비율은 유지합니다.", out TMP_Text masterValue);
            Slider bgm = VolumeRow(page, "Bgm", "BGM 볼륨", 132f, "배경 음악의 크기를 조절합니다. 마스터 볼륨의 영향을 함께 받습니다.", out TMP_Text bgmValue);
            Slider sfx = VolumeRow(page, "Sfx", "SFX 볼륨", 202f, "효과음의 크기를 조절합니다. 마스터 볼륨의 영향을 함께 받습니다.", out TMP_Text sfxValue);
            Panel(page, "AudioSeparator", 16f, 280f, 780f, 1f, G_Border);
            RectTransform muteRow = Rect(page, "MuteRow", 16f, 300f, ROW_WIDTH, ROW_HEIGHT);
            Image muteBackground = Panel(muteRow, "RowBackground", 0f, 0f, ROW_WIDTH, ROW_HEIGHT, G_Row);
            Label(muteRow, "Label", "음소거", 20f, 13f, 250f, 36f, 21f, G_Text, false, TextAlignmentOptions.Left);
            Hint(muteRow, muteBackground, "음소거", "모든 소리를 잠시 끕니다. 볼륨 값은 그대로 유지되어 음소거를 해제하면 원래 크기로 돌아옵니다.");
            Toggle mute = Checkbox(muteRow, "MuteToggle", 420f, 16f);
            Link("_muteState", Label(muteRow, "MuteState", "끄기", 466f, 13f, 110f, 36f, 21f, G_Text, false, TextAlignmentOptions.Left));
            Label(page, "AudioNotice", "볼륨 변경은 바로 들을 수 있으며, 취소하면 이전 값으로 돌아갑니다", 36f, 396f, 740f, 32f, 15f, G_Gold, false, TextAlignmentOptions.Left);
            Link("_master", master);
            Link("_bgm", bgm);
            Link("_sfx", sfx);
            Link("_mute", mute);
            Link("_masterValue", masterValue);
            Link("_bgmValue", bgmValue);
            Link("_sfxValue", sfxValue);
        }

        private static void BuildGame()
        {
            RectTransform page = Node("game-page");
            Label(page, "GameSection", "마우스 커서", 62f, 16f, 300f, 34f, 20f, G_Gold, true, TextAlignmentOptions.Left);
            Glyph(page, "GameSectionMark", 20f, 25f, 12f, SettingsGlyph.EShape.Diamond, G_Gold);
            RectTransform row = Rect(page, "CursorSizeRow", 16f, 62f, ROW_WIDTH, ROW_HEIGHT);
            Image background = Panel(row, "RowBackground", 0f, 0f, ROW_WIDTH, ROW_HEIGHT, G_Row);
            Label(row, "Label", "커서 크기", 20f, 13f, 290f, 36f, 21f, G_Text, false, TextAlignmentOptions.Left);
            Hint(row, background, "커서 크기", "게임 안에서 보이는 마우스 커서의 크기를 선택합니다. 선택하면 바로 미리 볼 수 있고, 취소하면 이전 크기로 돌아옵니다.");
            RectTransform slot = Rect(row, "CursorSizeSlot", 420f, 10f, 344f, 42f);
            G_Nodes["dd-cursor"] = slot;
            Link("_cursorSize", Dropdown("dd-cursor", new[] { "중 (100%)" }));
            BuildVfxSection(page);
            BuildCameraSection(page);
            Label(page, "GameNotice", "커서 크기는 화면이 아닌 게임 커서 그림에만 적용됩니다", 36f, GAME_NOTICE_Y, 740f, 32f, 15f, G_Gold, false, TextAlignmentOptions.Left);
            EnsureGameScroll(page);
        }

        // 게임 탭 맨 아래에 카메라 각도와 거리 슬라이더를 둔다. 슬라이더 0.5가 줌 단계 그대로다
        private static void BuildCameraSection(RectTransform page)
        {
            Panel(page, "CameraSeparator", 16f, 364f, 780f, 1f, G_Border);
            Label(page, "CameraSection", "카메라 구도", 62f, 378f, 300f, 34f, 20f, G_Gold, true, TextAlignmentOptions.Left);
            Glyph(page, "CameraSectionMark", 20f, 387f, 12f, SettingsGlyph.EShape.Diamond, G_Gold);
            Slider pitch = VolumeRow(page, "CameraPitch", "카메라 각도", 424f,
                "카메라가 캐릭터를 내려다보는 각도를 조절합니다. 오른쪽으로 올리면 더 위에서 내려다보고 왼쪽으로 내리면 더 눕혀서 봅니다. 마우스 휠 줌 단계마다 기본 각도가 다르며 그 위에 이 값이 더해집니다.", out TMP_Text pitchValue);
            Slider distance = VolumeRow(page, "CameraDistance", "카메라 거리", 494f,
                "카메라가 캐릭터에서 떨어진 거리를 조절합니다. 오른쪽으로 올리면 더 멀리서 넓게 보이고 왼쪽으로 내리면 더 가까이서 봅니다. 마우스 휠 줌 단계의 거리에 비율로 적용됩니다.", out TMP_Text distanceValue);
            pitch.value = CameraComposition.DEFAULT_RATE;
            distance.value = CameraComposition.DEFAULT_RATE;
            Link("_cameraPitch", pitch);
            Link("_cameraDistance", distance);
            Link("_cameraPitchValue", pitchValue);
            Link("_cameraDistanceValue", distanceValue);
        }

        // 항목이 창 높이보다 길어져 게임 탭을 세로 스크롤로 만든다. 이미 만든 탭이면 내용 영역만 돌려준다
        private static RectTransform EnsureGameScroll(RectTransform page)
        {
            Transform existing = page.Find("GameContent");
            if (existing != null)
            {
                return (RectTransform)existing;
            }

            RectTransform content = Rect(page, "GameContent", 0f, 0f, page.rect.width, GAME_CONTENT_HEIGHT);
            List<Transform> children = new List<Transform>();
            foreach (Transform child in page)
            {
                if (child != content)
                {
                    children.Add(child);
                }
            }
            foreach (Transform child in children)
            {
                child.SetParent(content, false);
            }

            page.gameObject.AddComponent<RectMask2D>();
            ScrollRect scroll = page.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = page;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = GAME_SCROLL_SENSITIVITY;
            return content;
        }

        [MenuItem("Shinshi/UI/설정 창 카메라 구도 추가")]
        private static void PatchCameraRows()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            try
            {
                G_View = root.GetComponentInChildren<SettingsView>(true);
                Transform page = FindDeep(root.transform, "GamePage");
                if ((G_View == null) || (page == null))
                {
                    throw new InvalidOperationException("설정 창 프리팹에서 SettingsView 또는 GamePage를 찾을 수 없습니다");
                }
                G_Nodes.Clear();
                G_Regular = EnsureFont("Regular");
                G_Bold = EnsureFont("Bold");
                RectTransform content = EnsureGameScroll((RectTransform)page);
                foreach (string stale in new[] { "CameraSeparator", "CameraSection", "CameraSectionMark", "CameraPitchRow", "CameraDistanceRow" })
                {
                    Transform old = content.Find(stale);
                    if (old != null)
                    {
                        UnityEngine.Object.DestroyImmediate(old.gameObject);
                    }
                }
                BuildCameraSection(content);
                Transform notice = content.Find("GameNotice");
                if (notice != null)
                {
                    SetRect((RectTransform)notice, 36f, GAME_NOTICE_Y, 740f, 32f);
                }
                PreloadCharacters((RectTransform)G_View.transform);
                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
                AssetDatabase.SaveAssetIfDirty(G_Regular);
                AssetDatabase.SaveAssetIfDirty(G_Bold);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // 게임 탭 아래쪽에 내 캐릭터와 파티원의 이펙트 투명도 슬라이더 두 줄을 둔다
        private static void BuildVfxSection(RectTransform page)
        {
            Panel(page, "GameSeparator", 16f, 148f, 780f, 1f, G_Border);
            Label(page, "VfxSection", "이펙트 투명도", 62f, 162f, 300f, 34f, 20f, G_Gold, true, TextAlignmentOptions.Left);
            Glyph(page, "VfxSectionMark", 20f, 171f, 12f, SettingsGlyph.EShape.Diamond, G_Gold);
            Slider own = VolumeRow(page, "VfxOwn", "내 이펙트 투명도", 208f,
                "내 캐릭터가 쓰는 스킬 이펙트의 투명도를 조절합니다. 0%로 두면 내 이펙트를 그리지 않습니다. 설정은 내 화면에만 적용되며 전투 판정에는 영향이 없습니다.", out TMP_Text ownValue);
            Slider party = VolumeRow(page, "VfxParty", "파티원 이펙트 투명도", 278f,
                "파티원이 쓰는 스킬 이펙트의 투명도를 조절합니다. 낮추면 파티 전투에서 화면이 덜 복잡해지고 0%로 두면 파티원 이펙트를 그리지 않습니다. 몬스터의 공격 표시와 내 피격 이펙트는 항상 보입니다.", out TMP_Text partyValue);
            Link("_vfxOwn", own);
            Link("_vfxParty", party);
            Link("_vfxOwnValue", ownValue);
            Link("_vfxPartyValue", partyValue);
        }

        [MenuItem("Shinshi/UI/설정 창 이펙트 투명도 추가")]
        private static void PatchVfxRows()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PREFAB_PATH);
            try
            {
                G_View = root.GetComponentInChildren<SettingsView>(true);
                Transform page = FindDeep(root.transform, "GamePage");
                if ((G_View == null) || (page == null))
                {
                    throw new InvalidOperationException("설정 창 프리팹에서 SettingsView 또는 GamePage를 찾을 수 없습니다");
                }
                G_Nodes.Clear();
                G_Regular = EnsureFont("Regular");
                G_Bold = EnsureFont("Bold");
                Transform vfxParent = page.Find("GameContent") ?? page;
                foreach (string stale in new[] { "GameSeparator", "VfxSection", "VfxSectionMark", "VfxVisibilityRow", "VfxOwnRow", "VfxPartyRow" })
                {
                    Transform old = vfxParent.Find(stale);
                    if (old != null)
                    {
                        UnityEngine.Object.DestroyImmediate(old.gameObject);
                    }
                }
                BuildVfxSection((RectTransform)vfxParent);
                PreloadCharacters((RectTransform)G_View.transform);
                PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
                AssetDatabase.SaveAssetIfDirty(G_Regular);
                AssetDatabase.SaveAssetIfDirty(G_Bold);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                {
                    return child;
                }
                Transform found = FindDeep(child, name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private static void BuildFooter()
        {
            Image hint = Fill("hint-bg", new Color(0.052f, 0.072f, 0.088f, 1f));
            Skin(hint, "T_UiPanel_Inset", new Color(0.8f, 0.95f, 1f, 1f));
            Border(hint.rectTransform, new Color(G_Border.r, G_Border.g, G_Border.b, 0.6f), 1f);
            TMP_Text hintTitle = Text(Node("hint-title"), "설정 안내", 17f, G_Accent, true, TextAlignmentOptions.Left);
            TMP_Text hintBody = Text(Node("hint-body"), "항목에 마우스를 올리면 자세한 설명을 확인할 수 있습니다.", 16f, G_Muted, false, TextAlignmentOptions.TopLeft);
            Fill("hint-band", new Color(G_Border.r, G_Border.g, G_Border.b, 0.4f));
            Link("_hintTitle", hintTitle);
            Link("_hintBody", hintBody);
            Panel(Node("win"), "FooterRule", 24f, 720f, 1152f, 1f, new Color(G_Gold.r, G_Gold.g, G_Gold.b, 0.22f));
            Link("_resetAll", Button("btn-resetall", "전체 초기화", G_Control));
            Link("_apply", Button("btn-apply", "적용", G_Control));
            Node("btn-apply").GetComponent<Button>().interactable = false;
            Link("_confirm", Button("btn-ok", "확인", new Color(0.18f, 0.31f, 0.32f, 1f)));
            Link("_cancel", Button("btn-cancel", "취소", G_Control));
            foreach (string id in new[] { "btn-resetall", "btn-reset", "btn-apply", "btn-cancel" })
            {
                Skin(Node(id).GetComponent<Image>(), "T_UiButton_Secondary", new Color(0.85f, 0.95f, 1f, 1f));
            }
            Skin(Node("btn-ok").GetComponent<Image>(), "T_UiButton_Secondary", new Color(1.7f, 1.45f, 1.05f, 1f));
            TMP_Text status = Label(Node("bottom"), "Status", "모든 설정이 저장되어 있습니다", 164f, 8f, 625f, 28f, 14f, G_Muted, false, TextAlignmentOptions.Left);
            Link("_status", status);
        }

        private static Slider VolumeRow(RectTransform page, string id, string title, float y, string description, out TMP_Text value)
        {
            RectTransform row = Rect(page, id + "Row", 16f, y, ROW_WIDTH, ROW_HEIGHT);
            Image background = Panel(row, "RowBackground", 0f, 0f, ROW_WIDTH, ROW_HEIGHT, G_Row);
            Label(row, "Label", title, 20f, 13f, 250f, 36f, 21f, G_Text, false, TextAlignmentOptions.Left);
            Hint(row, background, title, description);
            RectTransform track = Rect(row, "VolumeSlider", 420f, 16f, 216f, 30f);
            Slider slider = track.gameObject.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            Image rail = Panel(track, "Rail", 0f, 12f, 216f, 6f, G_Control);
            rail.raycastTarget = true;
            Border(rail.rectTransform, G_Border, 1f);
            RectTransform fillArea = Rect(track, "FillArea", 0f, 12f, 216f, 6f);
            Image fill = Panel(fillArea, "Fill", 0f, 0f, 216f, 6f, G_Accent);
            Stretch(fill.rectTransform);
            slider.fillRect = fill.rectTransform;
            RectTransform handleArea = Rect(track, "HandleArea", 0f, 0f, 216f, 30f);
            Image handle = Panel(handleArea, "Handle", 0f, 4f, 10f, 22f, G_Text);
            handle.raycastTarget = true;
            handle.rectTransform.anchorMin = Vector2.zero;
            handle.rectTransform.anchorMax = Vector2.up;
            handle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            handle.rectTransform.sizeDelta = new Vector2(10f, -8f);
            handle.rectTransform.anchoredPosition = Vector2.zero;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.value = 1f;
            CopyHint(track.gameObject, row);
            Button minus = MakeButton(row, "Minus", "-", 382f, 16f, 28f, 30f, G_Control);
            Button plus = MakeButton(row, "Plus", "+", 646f, 16f, 28f, 30f, G_Control);
            CopyHint(minus.gameObject, row);
            CopyHint(plus.gameObject, row);
            SettingsVolumeStep minusStep = minus.gameObject.AddComponent<SettingsVolumeStep>();
            SetReference(minusStep, "_slider", slider);
            SetFloat(minusStep, "_step", -0.05f);
            UnityEventTools.AddPersistentListener(minus.onClick, minusStep.Step);
            SettingsVolumeStep plusStep = plus.gameObject.AddComponent<SettingsVolumeStep>();
            SetReference(plusStep, "_slider", slider);
            SetFloat(plusStep, "_step", 0.05f);
            UnityEventTools.AddPersistentListener(plus.onClick, plusStep.Step);
            value = Label(row, "Value", "100%", 692f, 13f, 72f, 36f, 21f, G_Accent, true, TextAlignmentOptions.Right);
            return slider;
        }

        private static void Section(string id, string title, float y)
        {
            SetRect(Node(id), 16f, y, 780f, 34f);
            string prefix = id == "sec-screen" ? "sec-screen" : "sec-frame";
            Hide(prefix + "-s");
            Fill(prefix + "-b", new Color(G_Gold.r, G_Gold.g, G_Gold.b, 0.5f));
            Text(Node(prefix + "-t"), title, 20f, G_Gold, true, TextAlignmentOptions.Left);
            Glyph(Node(id), "SectionDiamond", 8f, 10f, 12f, SettingsGlyph.EShape.Diamond, G_Gold);
        }

        private static void Row(string id, string backgroundId, string labelId, string title, float y, string description)
        {
            SetRect(Node(id), 16f, y, ROW_WIDTH, ROW_HEIGHT);
            SetRect(Node(backgroundId), 0f, 0f, ROW_WIDTH, ROW_HEIGHT);
            SetRect(Node(labelId), 20f, 13f, 290f, 36f);
            Image background = Fill(backgroundId, G_Row);
            background.raycastTarget = true;
            Text(Node(labelId), title, 21f, G_Text, false, TextAlignmentOptions.Left);
            Hint(Node(id), background, title, description);
        }

        private static void Hint(RectTransform row, Image background, string title, string description)
        {
            background.raycastTarget = true;
            SettingsHintTarget target = row.gameObject.AddComponent<SettingsHintTarget>();
            SetReference(target, "_view", G_View);
            SetReference(target, "_background", background);
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty("_title").stringValue = title;
            serialized.FindProperty("_description").stringValue = description;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static TMP_Dropdown Dropdown(string id, string[] options)
        {
            RectTransform node = Node(id);
            foreach (Transform child in node)
            {
                child.gameObject.SetActive(false);
            }
            GameObject control = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
            control.name = "Dropdown";
            control.transform.SetParent(node, false);
            Stretch((RectTransform)control.transform);
            TMP_Dropdown dropdown = control.GetComponent<TMP_Dropdown>();
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>(options));
            foreach (TMP_Text label in control.GetComponentsInChildren<TMP_Text>(true))
            {
                label.font = G_Regular;
                label.fontSize = 21f;
                label.color = G_Text;
                label.textWrappingMode = TextWrappingModes.NoWrap;
            }
            Image surface = control.GetComponent<Image>();
            surface.color = G_Control;
            Skin(surface, "T_UiButton_Secondary", new Color(0.5f, 0.65f, 0.75f, 1f));
            Border((RectTransform)control.transform, G_Border, 1f);
            dropdown.captionText.rectTransform.offsetMin = new Vector2(16f, 0f);
            dropdown.captionText.rectTransform.offsetMax = new Vector2(-44f, 0f);
            dropdown.captionText.alignment = TextAlignmentOptions.MidlineLeft;
            dropdown.captionText.font = G_Bold;
            Transform arrow = control.transform.Find("Arrow");
            if (arrow != null)
            {
                arrow.gameObject.SetActive(false);
            }
            RectTransform glyphAnchor = Rect((RectTransform)control.transform, "ArrowAnchor", 0f, 0f, 24f, 24f);
            glyphAnchor.anchorMin = new Vector2(1f, 0.5f);
            glyphAnchor.anchorMax = new Vector2(1f, 0.5f);
            glyphAnchor.pivot = new Vector2(1f, 0.5f);
            glyphAnchor.anchoredPosition = new Vector2(-12f, 0f);
            SettingsGlyph glyph = glyphAnchor.gameObject.AddComponent<SettingsGlyph>();
            glyph.SetShape(SettingsGlyph.EShape.Chevron);
            glyph.color = G_Gold;
            glyph.raycastTarget = false;
            RectTransform template = dropdown.template;
            template.sizeDelta = new Vector2(0f, 250f);
            template.GetComponent<Image>().color = G_Control;
            Border(template, G_Gold, 1f);
            Toggle item = template.GetComponentInChildren<Toggle>(true);
            RectTransform itemRect = (RectTransform)item.transform;
            itemRect.sizeDelta = new Vector2(0f, 46f);
            item.targetGraphic.color = new Color(0.16f, 0.15f, 0.13f, 1f);
            if (item.graphic != null)
            {
                item.graphic.gameObject.SetActive(false);
            }
            SettingsGlyph marker = Glyph(itemRect, "SelectedMark", 12f, 18f, 10f, SettingsGlyph.EShape.Diamond, G_Accent);
            item.graphic = marker;
            dropdown.itemText.rectTransform.offsetMin = new Vector2(34f, 0f);
            dropdown.itemText.rectTransform.offsetMax = new Vector2(-8f, 0f);
            ColorBlock colors = dropdown.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = Color.white;
            dropdown.colors = colors;
            CopyHint(control, node.parent);
            return dropdown;
        }

        private static Toggle Checkbox(RectTransform parent, string name, float x, float y)
        {
            RectTransform rect = Rect(parent, name, x, y, 30f, 30f);
            Image background = rect.gameObject.AddComponent<Image>();
            background.color = G_Control;
            Border(rect, G_Gold, 1f);
            Toggle toggle = rect.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = Glyph(rect, "Checkmark", 4f, 4f, 22f, SettingsGlyph.EShape.Check, G_Accent);
            CopyHint(rect.gameObject, parent);
            return toggle;
        }

        private static void CopyHint(GameObject target, Transform source)
        {
            if (!source.TryGetComponent(out SettingsHintTarget original))
            {
                return;
            }
            SerializedObject originalData = new SerializedObject(original);
            SettingsHintTarget hint = target.AddComponent<SettingsHintTarget>();
            SetReference(hint, "_view", G_View);
            SetReference(hint, "_background", originalData.FindProperty("_background").objectReferenceValue);
            SerializedObject data = new SerializedObject(hint);
            data.FindProperty("_title").stringValue = originalData.FindProperty("_title").stringValue;
            data.FindProperty("_description").stringValue = originalData.FindProperty("_description").stringValue;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Button Button(string id, string title, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            Image image = Fill(id, color);
            image.raycastTarget = true;
            Button button = Node(id).gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.55f);
            button.colors = colors;
            if (!string.IsNullOrEmpty(title))
            {
                Text(Node(id), title, 21f, G_Text, false, alignment);
            }
            return button;
        }

        private static Button MakeButton(RectTransform parent, string name, string title, float x, float y, float width, float height, Color color)
        {
            RectTransform rect = Rect(parent, name, x, y, width, height);
            string id = name + Guid.NewGuid().ToString("N");
            G_Nodes[id] = rect;
            Button button = Button(id, title, color);
            Border(rect, G_Border, 1f);
            return button;
        }

        private static TMP_Text Text(RectTransform parent, string value, float size, Color color, bool bold = false, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            RectTransform rect = Rect(parent, "Text", 0f, 0f, 0f, 0f);
            Stretch(rect);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = bold ? G_Bold : G_Regular;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        private static TMP_Text Label(RectTransform parent, string name, string value, float x, float y, float width, float height, float size, Color color, bool bold = false, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            return Text(Rect(parent, name, x, y, width, height), value, size, color, bold, alignment);
        }

        private static SettingsGlyph Glyph(RectTransform parent, string name, float x, float y, float size, SettingsGlyph.EShape shape, Color color)
        {
            SettingsGlyph glyph = Rect(parent, name, x, y, size, size).gameObject.AddComponent<SettingsGlyph>();
            glyph.SetShape(shape);
            glyph.color = color;
            glyph.raycastTarget = false;
            return glyph;
        }

        private static Image Panel(RectTransform parent, string name, float x, float y, float width, float height, Color color)
        {
            Image image = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Skin(Image image, string name, Color tint)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SKIN_FOLDER + name + ".png");
            if (sprite == null)
            {
                throw new InvalidOperationException("설정 창 스킨을 찾을 수 없습니다: " + name);
            }
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = tint;
        }

        private static void Border(RectTransform parent, Color color, float width)
        {
            RectTransform border = Rect(parent, "Border", 0f, 0f, 0f, 0f);
            Stretch(border);
            RectTransform top = Panel(border, "Top", 0f, 0f, 0f, width, color).rectTransform;
            top.anchorMax = Vector2.one;
            top.offsetMin = new Vector2(0f, -width);
            top.offsetMax = Vector2.zero;
            RectTransform bottom = Panel(border, "Bottom", 0f, 0f, 0f, width, color).rectTransform;
            bottom.anchorMin = Vector2.zero;
            bottom.anchorMax = Vector2.right;
            bottom.offsetMin = Vector2.zero;
            bottom.offsetMax = new Vector2(0f, width);
            RectTransform left = Panel(border, "Left", 0f, 0f, width, 0f, color).rectTransform;
            left.anchorMin = Vector2.zero;
            left.anchorMax = Vector2.up;
            left.offsetMin = Vector2.zero;
            left.offsetMax = new Vector2(width, 0f);
            RectTransform right = Panel(border, "Right", 0f, 0f, width, 0f, color).rectTransform;
            right.anchorMin = Vector2.right;
            right.anchorMax = Vector2.one;
            right.offsetMin = new Vector2(-width, 0f);
            right.offsetMax = Vector2.zero;
        }

        private static RectTransform Rect(RectTransform parent, string name, float x, float y, float width, float height)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.layer = LayerMask.NameToLayer("UI");
            RectTransform rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            SetRect(rect, x, y, width, height);
            return rect;
        }

        private static void SetRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = Vector2.up;
            rect.anchorMax = Vector2.up;
            rect.pivot = Vector2.up;
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static RectTransform Node(string id) { return G_Nodes[id]; }
        private static void Hide(string id) { Node(id).gameObject.SetActive(false); }

        private static Image Fill(string id, Color color)
        {
            RectTransform rect = Node(id);
            if (!rect.TryGetComponent(out Image image))
            {
                image = rect.gameObject.AddComponent<Image>();
            }
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static IEnumerable<Transform> Children(Transform parent)
        {
            foreach (Transform child in parent)
            {
                yield return child;
            }
        }

        private static void Link(string name, UnityEngine.Object value) { SetReference(G_View, name, value); }

        private static void SetReference(UnityEngine.Object target, string name, UnityEngine.Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(name).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(UnityEngine.Object target, string name, float value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(name).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void LinkArray(string name, UnityEngine.Object[] values)
        {
            SerializedObject serialized = new SerializedObject(G_View);
            SerializedProperty property = serialized.FindProperty(name);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; ++i)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static TMP_FontAsset EnsureFont(string weight)
        {
            string path = FONT_FOLDER + "/SettingsKorean-" + weight + ".asset";
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null)
            {
                return existing;
            }
            Font source = AssetDatabase.LoadAssetAtPath<Font>(SOURCE_FONT_FOLDER + "/NanumGothic-" + weight + ".ttf");
            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(source, 48, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            font.name = "SettingsKorean-" + weight;
            AssetDatabase.CreateAsset(font, path);
            AssetDatabase.AddObjectToAsset(font.material, font);
            AssetDatabase.AddObjectToAsset(font.atlasTextures[0], font);
            return font;
        }

        private static void PreloadCharacters(RectTransform root)
        {
            StringBuilder characters = new StringBuilder("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789%+-/ .,:()전체 화면테두리 없는 창창 모드제한 없음켜기끄기게임마우스 커서커서 크기소중대적용하지 않은 변경 사항이 있습니다설정을 적용했습니다모든 설정이 저장되어 있습니다소리의 크기와 균형을 조절합니다");
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                characters.Append(text.text);
            }
            foreach (SettingsHintTarget hint in root.GetComponentsInChildren<SettingsHintTarget>(true))
            {
                SerializedObject serialized = new SerializedObject(hint);
                characters.Append(serialized.FindProperty("_title").stringValue);
                characters.Append(serialized.FindProperty("_description").stringValue);
            }
            G_Regular.TryAddCharacters(characters.ToString());
            G_Bold.TryAddCharacters(characters.ToString());
            EditorUtility.SetDirty(G_Regular);
            EditorUtility.SetDirty(G_Bold);
        }

        private static void BuildPreviewHelpers(SettingsView view)
        {
            if (GameObject.Find("EventSystem") == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }
            if (Camera.main != null)
            {
                Camera.main.clearFlags = CameraClearFlags.SolidColor;
                Camera.main.backgroundColor = new Color(0.035f, 0.052f, 0.068f, 1f);
            }
            GameObject existing = GameObject.Find("SettingsPreviewCanvas");
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }
            RectTransform canvas = UiLayoutImporter.CreateCanvas("SettingsPreviewCanvas");
            Button open = MakeButton(canvas, "OpenSettings", "환경설정 열기", 40f, 40f, 190f, 46f, G_Control);
            UnityEventTools.AddPersistentListener(open.onClick, view.Show);
            GameObject audio = GameObject.Find("SettingsAudioPreview");
            if (audio == null)
            {
                audio = new GameObject("SettingsAudioPreview");
                foreach (SettingsAudioChannel.EChannel channel in Enum.GetValues(typeof(SettingsAudioChannel.EChannel)))
                {
                    GameObject child = new GameObject(channel.ToString(), typeof(AudioSource), typeof(SettingsAudioChannel));
                    child.transform.SetParent(audio.transform);
                    AudioSource source = child.GetComponent<AudioSource>();
                    source.playOnAwake = false;
                    SettingsAudioChannel binding = child.GetComponent<SettingsAudioChannel>();
                    SetReference(binding, "_source", source);
                    SerializedObject serialized = new SerializedObject(binding);
                    serialized.FindProperty("_channel").enumValueIndex = (int)channel;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }
    }
}
#endif
