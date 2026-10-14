// Unity
using UnityEditor;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Editor
{
    /// <summary> 에디터 플레이에서 부팅 프리워밍을 켜고 끄는 메뉴, 기본은 반복 플레이 속도를 위해 꺼 둔다 </summary>
    public static class PrewarmEditorToggle
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_PATH = "Shinshi/Prewarm/Run In Editor";

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem(MENU_PATH)]
        private static void Toggle()
        {
            bool enabled = EditorPrefs.GetBool(ClientPrewarmGate.EDITOR_PREF_KEY, false);
            EditorPrefs.SetBool(ClientPrewarmGate.EDITOR_PREF_KEY, !enabled);
        }

        [MenuItem(MENU_PATH, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MENU_PATH, EditorPrefs.GetBool(ClientPrewarmGate.EDITOR_PREF_KEY, false));
            return true;
        }
    }
}
