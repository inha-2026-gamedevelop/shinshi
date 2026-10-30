// System
using System.Collections;

// Unity
using UnityEngine;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    /// <summary> 숲의 조작 안내와 월백 목표 문구를 작게 띄우는 화면, 입력을 받지 않아 이동과 전투를 막지 않는다 </summary>
    public sealed class PrologueHintView : ClientView
    {
        // 프리팹 없이 코드로 만든다. 클릭을 받는 요소가 없고 레이캐스트도 끄므로 아래 게임 입력을 가로채지 않는다.
        // 정렬 순서는 일러스트 화면보다 낮아 일러스트가 뜨면 가려진다

        /****************************************
        *                Fields
        ****************************************/

        private const int    SORTING_ORDER      = 30000;
        private const float  HINT_FONT_SIZE     = 30f;
        private const float  OBJECTIVE_FONT_SIZE = 34f;
        private const float  HINT_BOTTOM_OFFSET = 220f;
        private const float  OBJECTIVE_TOP_OFFSET = 90f;
        private const float  NOTICE_FONT_SIZE   = 30f;
        private const float  NOTICE_TOP_OFFSET  = 170f;
        private const float  NOTICE_HEIGHT      = 100f;
        private const float  TEXT_WIDTH         = 900f;
        private const float  TEXT_HEIGHT        = 60f;
        private const string ROOT_OBJECT_NAME   = "PrologueHint";

        private static readonly Color HINT_COLOR      = new Color(1f, 1f, 1f, 0.95f);
        private static readonly Color OBJECTIVE_COLOR = new Color(1f, 0.92f, 0.65f, 1f);
        private static readonly Color NOTICE_COLOR    = new Color(0.78f, 0.95f, 1f, 1f);

        private TextMeshProUGUI _hintText      = null;
        private TextMeshProUGUI _objectiveText = null;
        private TextMeshProUGUI _noticeText    = null;
        private Coroutine       _noticeRoutine = null;
        private string          _hint          = string.Empty;
        private string          _objective     = string.Empty;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 코드로 계층을 만든 화면을 돌려준다, 서버 빌드에서는 객체만 만들어지고 아무것도 그리지 않는다 </summary>
        public static PrologueHintView Create()
        {
            var root = new GameObject(ROOT_OBJECT_NAME);
            return root.AddComponent<PrologueHintView>();
        }

        public void SetFont(TMP_FontAsset font)
        {
            if ((font == null) || (_hintText == null))
            {
                return;
            }

            _hintText.font      = font;
            _objectiveText.font = font;
            _noticeText.font    = font;
        }

        /// <summary> 회복, 귀환, 재개 같은 짧은 알림을 seconds 동안 보인다, 새 알림이 오면 이전 것을 바꾼다. 줄바꿈 문자로 두 줄까지 쓴다 </summary>
        public void ShowNotice(string text, float seconds)
        {
            if (_noticeText == null)
            {
                return;
            }

            if (_noticeRoutine != null)
            {
                StopCoroutine(_noticeRoutine);
                _noticeRoutine = null;
            }

            _noticeText.SetText(text);
            _noticeText.gameObject.SetActive(!string.IsNullOrEmpty(text));

            if ((!string.IsNullOrEmpty(text)) && (gameObject.activeInHierarchy))
            {
                _noticeRoutine = StartCoroutine(CoHideNotice(seconds));
            }
        }

        /// <summary> 알림을 바로 내린다 </summary>
        public void HideNotice()
        {
            if (_noticeRoutine != null)
            {
                StopCoroutine(_noticeRoutine);
                _noticeRoutine = null;
            }

            if (_noticeText != null)
            {
                _noticeText.gameObject.SetActive(false);
            }
        }

        // 일시정지 중에도 흘러야 하므로 실제 시간으로 센다
        private IEnumerator CoHideNotice(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            _noticeRoutine = null;
            _noticeText.gameObject.SetActive(false);
        }

        /// <summary> 조작 안내, 빈 문자열이면 숨긴다. 값이 바뀔 때만 다시 쓴다 </summary>
        public void SetHint(string text)
        {
            if ((_hintText == null) || (text == _hint))
            {
                return;
            }

            _hint = text;
            _hintText.SetText(text);
            _hintText.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        /// <summary> 화면 위쪽의 목표 문구, 빈 문자열이면 숨긴다 </summary>
        public void SetObjective(string text)
        {
            if ((_objectiveText == null) || (text == _objective))
            {
                return;
            }

            _objective = text;
            _objectiveText.SetText(text);
            _objectiveText.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        public void Hide()
        {
            Destroy(gameObject);
        }

        protected override void OnViewAwake()
        {
            // 레이캐스터를 붙이지 않는다. 클릭을 받는 요소가 없어 아래 게임 입력을 가로채지 않는다
            PrologueUi.AddCanvas(gameObject, SORTING_ORDER, false);

            _hintText = CreateText("Hint", HINT_FONT_SIZE, HINT_COLOR, 0f, HINT_BOTTOM_OFFSET);
            _objectiveText = CreateText("Objective", OBJECTIVE_FONT_SIZE, OBJECTIVE_COLOR, 1f, -OBJECTIVE_TOP_OFFSET);

            _noticeText = CreateText("Notice", NOTICE_FONT_SIZE, NOTICE_COLOR, 1f, -NOTICE_TOP_OFFSET);
            _noticeText.textWrappingMode = TextWrappingModes.Normal;
            _noticeText.rectTransform.sizeDelta = new Vector2(TEXT_WIDTH, NOTICE_HEIGHT);

            _hintText.gameObject.SetActive(false);
            _objectiveText.gameObject.SetActive(false);
            _noticeText.gameObject.SetActive(false);
        }

        // anchorY가 0이면 아래, 1이면 위에 붙이고 offsetY는 그 가장자리에서의 거리다
        private TextMeshProUGUI CreateText(string objectName, float fontSize, Color color, float anchorY, float offsetY)
        {
            TextMeshProUGUI text = PrologueUi.CreateText(objectName, transform, fontSize, color, TextAlignmentOptions.Center);
            text.textWrappingMode = TextWrappingModes.NoWrap;

            RectTransform rect = text.rectTransform;
            rect.anchorMin        = new Vector2(0.5f, anchorY);
            rect.anchorMax        = new Vector2(0.5f, anchorY);
            rect.pivot            = new Vector2(0.5f, anchorY);
            rect.anchoredPosition = new Vector2(0f, offsetY);
            rect.sizeDelta        = new Vector2(TEXT_WIDTH, TEXT_HEIGHT);
            return text;
        }
    }
}
