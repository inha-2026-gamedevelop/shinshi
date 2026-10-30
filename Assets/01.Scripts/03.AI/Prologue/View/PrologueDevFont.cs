#if UNITY_EDITOR
// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using TMPro;

namespace Shinshi.AI
{
    /// <summary> 에디터 시험 장치가 일러스트 화면에 프로젝트 한글 글꼴을 입히는 도우미, 빌드에는 들어가지 않는다 </summary>
    public static class PrologueDevFont
    {
        // 시험 장치가 있는 서버 어셈블리는 TextMeshPro를 참조하지 않아서 글꼴 타입을 이쪽에서 다룬다

        /****************************************
        *                Fields
        ****************************************/

        private const string FONT_PATH = "Assets/01.Scripts/06.UI/Hud/Fonts/HudQuestKorean-Regular.asset";

        /****************************************
        *                Methods
        ****************************************/

        public static void Apply(IllustrationView view)
        {
            if (view == null)
            {
                return;
            }

            view.SetFont(Load());
        }

        public static void Apply(PrologueHintView view)
        {
            if (view == null)
            {
                return;
            }

            view.SetFont(Load());
        }

        public static void Apply(PrologueChoiceView view)
        {
            if (view == null)
            {
                return;
            }

            view.SetFont(Load());
        }

        private static TMP_FontAsset Load()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT_PATH);
            if (font == null)
            {
                Debug.LogWarning($"[PrologueDevFont] 한글 글꼴을 찾지 못했다: {FONT_PATH}");
            }

            return font;
        }
    }
}
#endif
