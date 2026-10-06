#if SHINSHI_DEV_TEST
// System
using System.IO;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.Dev
{
    // 시험 빌드(ShinshiBuild의 windows-devtest)에서만 컴파일된다. 보스, UI 개발 씬을 한 실행 파일에서 골라 열려고 둔다.
    // 씬 오브젝트가 아니라 실행 시 스스로 만들어 씬을 바꿔도 남으므로 개발 씬 원본을 고치지 않는다
    /// <summary> 시험 빌드의 개발 씬 선택 창, F1로 열고 닫는다 </summary>
    public sealed class DevTestScenePicker : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const KeyCode TOGGLE_KEY    = KeyCode.F1;
        private const float   WINDOW_WIDTH  = 300f;
        private const float   WINDOW_MARGIN = 10f;
        private const float   BUTTON_HEIGHT = 28f;
        private const float   HEADER_HEIGHT = 48f;
        private const int     WINDOW_ID     = 0x5D7E57;
        private const string  WINDOW_TITLE  = "시험 씬 (F1 열기/닫기)";
        private const string  CURRENT_MARK  = "> ";

        private string[] _sceneNames = null;
        private bool     _isOpen     = true;
        private Vector2  _scroll     = Vector2.zero;

        /****************************************
        *              Unity Event
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var host = new GameObject(nameof(DevTestScenePicker));
            DontDestroyOnLoad(host);
            host.AddComponent<DevTestScenePicker>();
        }

        private void Awake()
        {
            int count = SceneManager.sceneCountInBuildSettings;
            _sceneNames = new string[count];
            for (int i = 0; i < count; ++i)
            {
                _sceneNames[i] = Path.GetFileNameWithoutExtension(SceneUtility.GetScenePathByBuildIndex(i));
            }
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
            if (!_isOpen)
            {
                return;
            }

            float height = Mathf.Min(Screen.height - (WINDOW_MARGIN * 2f), HEADER_HEIGHT + (BUTTON_HEIGHT * _sceneNames.Length));
            var   rect   = new Rect(Screen.width - WINDOW_WIDTH - WINDOW_MARGIN, WINDOW_MARGIN, WINDOW_WIDTH, height);
            GUI.Window(WINDOW_ID, rect, DrawWindow, WINDOW_TITLE);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void DrawWindow(int windowId)
        {
            int current = SceneManager.GetActiveScene().buildIndex;
            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _sceneNames.Length; ++i)
            {
                string label = (i == current) ? CURRENT_MARK + _sceneNames[i] : _sceneNames[i];
                if (GUILayout.Button(label, GUILayout.Height(BUTTON_HEIGHT)))
                {
                    SceneManager.LoadScene(i);
                }
            }
            GUILayout.EndScrollView();
        }
    }
}
#endif
