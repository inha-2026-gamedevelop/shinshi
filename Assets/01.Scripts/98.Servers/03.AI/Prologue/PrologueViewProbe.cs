#if UNITY_EDITOR
// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.AI
{
    /// <summary> 실제 플레이어가 있는 씬에서 F8로 부적 조사 화면을 띄워, 화면을 닫는 클릭이 이동이나 공격으로 새는지 눈으로 확인하는 에디터 전용 도구 </summary>
    public sealed class PrologueViewProbe : MonoBehaviour
    {
        // DungeonForest를 단독 Play하면 솔로 호스트가 실제 플레이어(입력 읽기 포함)를 만들어 준다. 그 상태에서 F8을 눌러 화면을 띄우고
        // 클릭으로 닫은 순간 캐릭터가 걷기 시작하거나 공격하는지 본다. 프로젝트의 씬 파일은 고치지 않는다.
        // 이 도구는 누수 여부를 판정하지 않고 확인할 기회만 준다. 판정은 사람이 보고 한다

        /****************************************
        *                Fields
        ****************************************/

        private const KeyCode PROBE_KEY      = KeyCode.F8;
        private const string  SCENE_FOREST   = "DungeonForest";
        private const string  SCENE_MAP      = "Dev_PrologueMap";
        private const string  OBJECT_NAME    = "PrologueViewProbe";
        private const string  CATALOG_PATH   = "Assets/09.Data/AI/Prologue/PrologueIllustrationCatalog.asset";

        private IllustrationView              _view    = null;
        private PrologueIllustrationCatalogSO _catalog = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if ((!Input.GetKeyDown(PROBE_KEY)) || ((_view != null) && (_view.IsShowing)))
            {
                return;
            }

            if (_catalog == null)
            {
                _catalog = AssetDatabase.LoadAssetAtPath<PrologueIllustrationCatalogSO>(CATALOG_PATH);
            }

            if (_catalog == null)
            {
                Debug.LogWarning($"[PrologueProbe] 일러스트 카탈로그를 찾지 못했다: {CATALOG_PATH}");
                return;
            }

            if (_view == null)
            {
                _view = IllustrationView.Create();
                PrologueDevFont.Apply(_view);
                _view.SetTransitionMaterial(_catalog.InkWipeMaterial);
                _view.Finished += OnFinished;
            }

            _view.Show(_catalog.TalismanPages, true, false);
            Debug.Log("[PrologueProbe] 부적 조사 화면을 띄웠다. 클릭으로 닫은 뒤 캐릭터가 걷거나 공격하는지 보세요");
        }

        private void OnDestroy()
        {
            if (_view != null)
            {
                _view.Finished -= OnFinished;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnFinished()
        {
            _view.Close();
            Debug.Log("[PrologueProbe] 화면이 닫혔다. 지금 캐릭터가 움직이거나 공격했다면 닫는 클릭이 새 것이다");
        }

        /****************************************
        *                 Boot
        ****************************************/

        // 확인에 쓰는 두 씬에서만 붙는다. 다른 팀원의 Play에는 아무것도 붙이지 않는다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            if ((sceneName != SCENE_FOREST) && (sceneName != SCENE_MAP))
            {
                return;
            }

            new GameObject(OBJECT_NAME).AddComponent<PrologueViewProbe>();
        }
    }
}
#endif
