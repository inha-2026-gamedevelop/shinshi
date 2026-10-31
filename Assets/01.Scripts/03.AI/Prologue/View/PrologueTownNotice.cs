// Unity
using UnityEngine;

// 서드파티
using TMPro;

namespace Shinshi.AI
{
    /// <summary> 마을에 도착하고 완료 기록을 확인한 뒤 완료 안내를 한 번 띄우는 도우미, 마을 쪽에서 한 줄로 부른다 </summary>
    public static class PrologueTownNotice
    {
        // 프롤로그 연결부가 없는 마을 씬에서 쓴다. 임시 안내 화면을 만들어 알림이 끝나면 스스로 사라진다.
        // 그림 종료만으로 완료 안내를 먼저 띄우지 않는다. 호출하는 쪽이 실제 도착과 완료 기록 확인 뒤에 부른다

        /****************************************
        *                Fields
        ****************************************/

        public const float SECONDS = 4f;

        private const float EXTRA_LIFETIME = 0.5f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 카탈로그의 글꼴로 완료 안내를 띄운다 </summary>
        public static void ShowCompletedFromCatalog(PrologueIllustrationCatalogSO catalog)
        {
            ShowCompleted((catalog != null) ? catalog.Font : null);
        }

        /// <summary> 완료 안내를 띄운다, font는 프롤로그 카탈로그의 글꼴을 넘기면 된다 </summary>
        public static void ShowCompleted(TMP_FontAsset font)
        {
            PrologueHintView view = PrologueHintView.Create();
            view.SetFont(font);
            view.ShowNotice(PrologueHintTexts.PROLOGUE_COMPLETED, SECONDS);
            Object.Destroy(view.gameObject, SECONDS + EXTRA_LIFETIME);
        }
    }
}
