#if SHINSHI_DEV_TEST
// System
using System.IO;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.Dev
{
    // 시험 빌드(ShinshiBuild의 windows-devtest)에서만 컴파일된다. 보스, UI 개발 씬을 한 실행 파일에서 골라 열려고 둔다.
    // 씬 오브젝트가 아니라 실행 시 스스로 만들어 씬을 바꿔도 남으므로 개발 씬 원본을 고치지 않는다.
    // UI 개발 씬은 데모 스크립트의 키로만 상태가 바뀌는데 화면에 안내가 없어 시험하지 못했으므로 씬별 조작을 함께 보인다
    /// <summary> 시험 빌드의 개발 씬 선택 창과 씬별 조작 안내, F1로 열고 닫는다 </summary>
    public sealed class DevTestScenePicker : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const KeyCode TOGGLE_KEY     = KeyCode.F1;
        private const float   WINDOW_WIDTH   = 320f;
        private const float   WINDOW_MARGIN  = 10f;
        private const float   BUTTON_HEIGHT  = 26f;
        private const float   HEADER_HEIGHT  = 30f;
        private const float   GUIDE_HEIGHT   = 150f;
        private const float   TAB_WIDTH      = 220f;
        private const float   TAB_HEIGHT     = 26f;
        private const int     WINDOW_ID      = 0x5D7E57;
        private const int     VSYNC_COUNT    = 1;
        private const string  NO_VSYNC_ARG   = "-devNoVsync";
        private const string  SCENE_ARG      = "-devScene";
        private const string  WINDOW_TITLE   = "시험 씬 (F1 열기/닫기)";
        private const string  TAB_TEXT       = "F1 씬 선택, 조작 안내";
        private const string  CURRENT_MARK   = "> ";
        private const string  NO_GUIDE       = "조작 없음 (배치 확인용 정적 화면)";

        private static readonly Color _tabBackColor = new Color(0f, 0f, 0f, 0.75f);

        // 데모 스크립트(99.Dev의 *Demo, ToastMessageTester)와 시험 장치에 정해 둔 키다. 키를 바꾸면 여기도 고친다
        private static readonly string[,] G_Guides = {
            { "Dev_BossMucheol",        "WASD 이동, 왼쪽 창 버튼으로 패턴 강제, 체력 조절, Reset" },
            { "Dev_BossMucheolPhase2",  "WASD 이동, 왼쪽 창 버튼으로 2페이즈 패턴 강제, 체력 56/54.9/26/24.9% 전환" },
            { "Dev_BossWolbaek",        "WASD 이동, 왼쪽 창 버튼으로 패턴 강제, 체력 조절, Reset" },
            { "Dev_BossWolbaekBarrier", "WASD 이동, 왼쪽 창 버튼으로 전환 직전(51%) 체력, 결계 중복 시작, 중도 취소" },
            { "Dev_CombatHud",          "B 일반/보스 전환, H 보스 피해, 1 작은 물약, 2 큰 물약, G 공명 충전\nZ X C 강림, K 쓰러짐, F 부활, T 거절 알림" },
            { "Dev_ItemSlot",           "I 인벤토리 열기/닫기, 슬롯 클릭 선택" },
            { "Dev_Result",             "1 성공, 2 시간 초과, 3 전멸, 4 포기 결과 화면" },
            { "Dev_SortiePrep",         "L 파티장/파티원 전환, 화면 버튼으로 난이도 선택, 준비" },
            { "Dev_ToastMessage",       "1 토스트, 2 토스트 연속, 3 모두 닫기\n4 5 6 알림 소/중/대, 7 8 9 확인 소/중/대" },
            { "Dev_CharSelect",         "캐릭터 슬롯 클릭으로 선택" },
            { "ShinshiTown",            "서버 없이 연 마을 배경. -devPerfTown으로 실행하면 게임 줌 시점을 돌며 Player.log에 [Perf] 비용을 남긴다" } };

        private string[] _sceneNames  = null;
        private string[] _labels      = null;
        private string[] _guides      = null;
        private string   _activeGuide = NO_GUIDE;
        private int      _activeIndex = -1;
        private bool     _isOpen      = true;
        private Vector2  _scroll      = Vector2.zero;
        private GUIStyle _guideStyle  = null;
        private GUIStyle _tabStyle    = null;

        /****************************************
        *              Unity Event
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            // 수직 동기 없이 수백 FPS로 돌면 프레임 간격이 들쭉날쭉해 끊김처럼 보이므로 시험 빌드는 모니터 주사율에 맞춘다
            // 성능 측정 때는 -devNoVsync로 끄고 프레임 상한 없이 잰다
            QualitySettings.vSyncCount = DevArgs.Has(NO_VSYNC_ARG) ? 0 : VSYNC_COUNT;

            var host = new GameObject(nameof(DevTestScenePicker));
            DontDestroyOnLoad(host);
            host.AddComponent<DevTestScenePicker>();
        }

        private void Awake()
        {
            int count = SceneManager.sceneCountInBuildSettings;
            _sceneNames = new string[count];
            _labels     = new string[count];
            _guides     = new string[count];
            for (int i = 0; i < count; ++i)
            {
                _sceneNames[i] = Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i));
                _labels[i]     = _sceneNames[i];
                _guides[i]     = FindGuide(_sceneNames[i]);
            }

            SelectActive(SceneManager.GetActiveScene().buildIndex);

            // 측정 스크립트가 버튼을 누르지 않고도 원하는 씬에서 시작하게 -devScene <씬 이름>을 받는다
            if (DevArgs.TryReadString(SCENE_ARG, out string startScene))
            {
                int index = System.Array.IndexOf(_sceneNames, startScene);
                if (index >= 0)
                {
                    SceneManager.LoadScene(index);
                }
            }
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Update()
        {
            if (Input.GetKeyDown(TOGGLE_KEY))
            {
                _isOpen = !_isOpen;
            }
        }

        // 보스 시험 장치의 GUI가 왼쪽 위를 쓰므로 오른쪽 위에 둔다
        private void OnGUI()
        {
            if (_guideStyle == null)
            {
                _guideStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
                _tabStyle   = CreateTabStyle();
            }

            if (!_isOpen)
            {
                // 닫혀 있어도 다시 여는 방법은 보이게 둔다. 씬 UI를 가리지 않게 작게 그린다
                GUI.Box(new Rect(Screen.width - TAB_WIDTH - WINDOW_MARGIN, WINDOW_MARGIN, TAB_WIDTH, TAB_HEIGHT), TAB_TEXT, _tabStyle);
                return;
            }

            float height = Mathf.Min(Screen.height - (WINDOW_MARGIN * 2f),
                HEADER_HEIGHT + GUIDE_HEIGHT + (BUTTON_HEIGHT * _sceneNames.Length));
            var   rect   = new Rect(Screen.width - WINDOW_WIDTH - WINDOW_MARGIN, WINDOW_MARGIN, WINDOW_WIDTH, height);
            GUI.Window(WINDOW_ID, rect, DrawWindow, WINDOW_TITLE);
        }

        /****************************************
        *                Methods
        ****************************************/

        // 씬을 고르면 창을 닫는다. IMGUI 창 아래의 UGUI도 같은 클릭을 받으므로 열어 두면 씬 UI 시험이 꼬인다
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            SelectActive(scene.buildIndex);
            _isOpen = false;
        }

        private void SelectActive(int buildIndex)
        {
            if ((_activeIndex >= 0) && (_activeIndex < _labels.Length))
            {
                _labels[_activeIndex] = _sceneNames[_activeIndex];
            }

            _activeIndex = buildIndex;
            _activeGuide = NO_GUIDE;
            if ((buildIndex >= 0) && (buildIndex < _labels.Length))
            {
                _labels[buildIndex] = CURRENT_MARK + _sceneNames[buildIndex];
                _activeGuide        = _guides[buildIndex];
            }
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.Label(_activeGuide, _guideStyle, GUILayout.Height(GUIDE_HEIGHT - HEADER_HEIGHT));

            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _labels.Length; ++i)
            {
                if (GUILayout.Button(_labels[i], GUILayout.Height(BUTTON_HEIGHT)))
                {
                    SceneManager.LoadScene(i);
                }
            }
            GUILayout.EndScrollView();
        }

        // 기본 상자는 반투명이라 밝은 하늘 위에서 글자가 묻히므로 어두운 바탕을 쓴다
        private static GUIStyle CreateTabStyle()
        {
            // 씬을 바꿀 때 미사용 에셋 정리가 GUIStyle만 참조하는 텍스처를 해제하므로 막아 둔다
            var background = new Texture2D(1, 1) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            background.SetPixel(0, 0, _tabBackColor);
            background.Apply();

            var style = new GUIStyle(GUI.skin.box);
            style.normal.background = background;
            style.normal.textColor  = Color.white;
            return style;
        }

        private static string FindGuide(string sceneName)
        {
            for (int i = 0; i < G_Guides.GetLength(0); ++i)
            {
                if (G_Guides[i, 0] == sceneName)
                {
                    return G_Guides[i, 1];
                }
            }

            return NO_GUIDE;
        }
    }
}
#endif
