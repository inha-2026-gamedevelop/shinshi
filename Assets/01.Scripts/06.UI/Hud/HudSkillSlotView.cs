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
        [SerializeField] private Image    _icon     = null;
        [SerializeField] private Image    _cooldown = null;
        [SerializeField] private TMP_Text _keyText  = null;

        // 쓸 수 없는 칸은 아이콘을 이만큼 어둡게 곱한다
        private static readonly Color G_LockedTint = new Color(0.3f, 0.3f, 0.3f, 1f);

        private Color _iconColor    = Color.white;
        private bool  _hasIconColor = false;
        private bool  _isLocked     = false;

        /****************************************
        *                Methods
        ****************************************/

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

        // 쿨타임이 지날수록 게이지가 위쪽에서 시계 방향으로 차오르고, 다 차서 쓸 수 있게 되면 숨긴다.
        // 매 프레임 불러도 할당이 없다
        public void SetCooldown(float remaining, float duration)
        {
            if ((duration <= 0f) || (remaining <= 0f))
            {
                _cooldown.enabled = false;
                return;
            }

            _cooldown.enabled = true;
            _cooldown.fillAmount = 1f - Mathf.Clamp01(remaining / duration);
        }
    }
}
