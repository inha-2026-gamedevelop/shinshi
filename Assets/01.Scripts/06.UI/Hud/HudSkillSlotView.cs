// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary>
    /// 스킬 칸 하나의 아이콘, 키 글자, 쿨타임 게이지 표시. 값은 기술 상태를 가진 쪽이 Set 메서드로 넣는다
    /// </summary>
    public sealed class HudSkillSlotView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("표시")]
        [SerializeField] private Image    _icon         = null;
        [SerializeField] private Image    _cooldown     = null;
        [SerializeField] private TMP_Text _keyText      = null;
        [SerializeField] private TMP_Text _cooldownText = null;

        // 쓸 수 없는 칸은 아이콘을 이만큼 어둡게 곱한다
        private static readonly Color G_LockedTint = new Color(0.3f, 0.3f, 0.3f, 1f);

        // 남은 시간이 이보다 짧으면 소수 한 자리까지 보인다
        private const float DECIMAL_THRESHOLD  = 10f;
        private const float COOLDOWN_FONT_SIZE = 26f;

        // 소수 한 자리 올림을 위해 곱했다가 나누는 배율
        private const float TENTHS_SCALE       = 10f;

        private Color _iconColor    = Color.white;
        private bool  _hasIconColor = false;
        private bool  _isLocked     = false;

        /****************************************
        *                Methods
        ****************************************/

        private void Awake()
        {
            if (_cooldownText == null)
            {
                _cooldownText = CreateCooldownText();
            }

            SetCooldownText(0f);
        }

        // 씬 칸에 숫자 글자가 연결되지 않았으면 쿨타임 게이지 바로 위에 같은 크기로 만든다
        private TMP_Text CreateCooldownText()
        {
            if (_cooldown == null)
            {
                return null;
            }

            var go = new GameObject("CooldownText", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_cooldown.transform.parent, false);
            rect.SetSiblingIndex(_cooldown.transform.GetSiblingIndex() + 1);

            var cooldownRect = (RectTransform)_cooldown.transform;
            rect.anchorMin        = cooldownRect.anchorMin;
            rect.anchorMax        = cooldownRect.anchorMax;
            rect.pivot            = cooldownRect.pivot;
            rect.anchoredPosition = cooldownRect.anchoredPosition;
            rect.sizeDelta        = cooldownRect.sizeDelta;

            var text = go.AddComponent<TextMeshProUGUI>();
            if ((_keyText != null) && (_keyText.font != null))
            {
                text.font = _keyText.font;
            }
            text.fontSize         = COOLDOWN_FONT_SIZE;
            text.fontStyle        = FontStyles.Bold;
            text.alignment        = TextAlignmentOptions.Center;
            text.color            = Color.white;
            text.raycastTarget    = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        public void SetIcon(Sprite icon)
        {
            _icon.sprite = icon;
            _icon.enabled = (icon != null);
        }

        public void SetKeyLabel(string label) { _keyText.SetText(label); }

        // 스택 부족처럼 쿨다운과 별개로 지금 쓸 수 없는 칸을 회색으로 보인다. 값이 같으면 바로 돌아와 매 프레임 불러도 된다
        public void SetLocked(bool locked)
        {
            if ((locked == _isLocked) || (_icon == null))
            {
                return;
            }

            if (!_hasIconColor)
            {
                _iconColor    = _icon.color;
                _hasIconColor = true;
            }

            _isLocked   = locked;
            _icon.color = locked ? (_iconColor * G_LockedTint) : _iconColor;
        }

        // 쿨타임이 지날수록 게이지가 위쪽에서 시계 방향으로 차오르고, 남은 초를 가운데 숫자로 보인다.
        // 다 차서 쓸 수 있게 되면 둘 다 숨긴다. 매 프레임 불러도 할당이 없다
        public void SetCooldown(float remaining, float duration)
        {
            if ((duration <= 0f) || (remaining <= 0f))
            {
                _cooldown.enabled = false;
                SetCooldownText(0f);
                return;
            }

            _cooldown.enabled = true;
            _cooldown.fillAmount = 1f - Mathf.Clamp01(remaining / duration);
            SetCooldownText(remaining);
        }

        // 10초 이상은 올림한 정수, 그보다 짧으면 소수 한 자리로 보인다
        private void SetCooldownText(float remaining)
        {
            if (_cooldownText == null)
            {
                return;
            }

            if (remaining <= 0f)
            {
                _cooldownText.enabled = false;
                return;
            }

            _cooldownText.enabled = true;
            if (remaining >= DECIMAL_THRESHOLD)
            {
                _cooldownText.SetText("{0:0}", Mathf.Ceil(remaining));
                return;
            }

            // 끝나기 직전에 0.0이 보이지 않도록 소수 한 자리에서도 올림한다
            _cooldownText.SetText("{0:1}", Mathf.Ceil(remaining * TENTHS_SCALE) / TENTHS_SCALE);
        }
    }
}
