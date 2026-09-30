// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Editor
{
    /// <summary> Bootstrap 씬을 열고 서버 역할이나 클라이언트로 플레이를 시작한다 </summary>
    public static class ShinshiPlayMenu
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string MENU_ROOT = "Shinshi/Play/";

        /****************************************
        *                 Menu
        ****************************************/

        [MenuItem(MENU_ROOT + "Town Server")]
        private static void PlayTownServer()
        {
            Play(Constants.Network.SERVER_ROLE_TOWN);
        }

        [MenuItem(MENU_ROOT + "Dungeon Server")]
        private static void PlayDungeonServer()
        {
            Play(Constants.Network.SERVER_ROLE_DUNGEON);
        }

        [MenuItem(MENU_ROOT + "Client")]
        private static void PlayClient()
        {
            Play(string.Empty);
        }

        /****************************************
        *                Methods
        ****************************************/

        // 서버 부트스트랩이 SessionState의 역할을 한 번 읽고 지운다
        private static void Play(string role)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Play] 이미 플레이 중이다");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(ShinshiBuild.BOOTSTRAP_SCENE_PATH);

            if (string.IsNullOrEmpty(role))
            {
                SessionState.EraseString(Constants.Network.EDITOR_SERVER_ROLE_KEY);
            }
            else
            {
                SessionState.SetString(Constants.Network.EDITOR_SERVER_ROLE_KEY, role);
            }

            EditorApplication.EnterPlaymode();
        }
    }
}
