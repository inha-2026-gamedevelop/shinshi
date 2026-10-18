// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary>상단을 유지하며 임무 본문을 접고 외부에서 전달한 목록 개수를 표시한다</summary>
    public sealed class HudQuestTrackerView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string EXPANDED_LABEL  = "−";
        private const string COLLAPSED_LABEL = "+";
        private const string COUNT_FORMAT    = "({0:0}/{1:0})";

        [Header("표시")]
        [SerializeField] private GameObject[] _entryRoots     = null;
        [SerializeField] private Button       _minimizeButton = null;
        [SerializeField] private TMP_Text     _minimizeLabel  = null;
        [SerializeField] private TMP_Text     _countLabel     = null;

        private bool _isCollapsed = false;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsCollapsed => _isCollapsed;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            EnsureModernLayout();
        }

        /// <summary>어두운 퀘스트 바탕을 한 번만 깐다. 입력은 막지 않는다</summary>
        public void EnsureModernLayout()
        {
            HudModernStyle.AddBackdrop(transform, UiModernDark.SPRITE_INSET, Color.white, 8f);
            ModernDarkKit.StyleText(_countLabel, 0f, UiModernDark.TextGold);
            ModernDarkKit.StyleText(_minimizeLabel, 0f, UiModernDark.TextMuted);
            ModernDarkKit.StyleButton(_minimizeButton, false);
        }

        private void OnEnable()
        {
            if (_minimizeButton != null)
            {
                _minimizeButton.onClick.AddListener(ToggleCollapsed);
            }

            ApplyCollapsed();
        }

        private void OnDisable()
        {
            if (_minimizeButton == null)
            {
                return;
            }

            _minimizeButton.onClick.RemoveListener(ToggleCollapsed);
        }

        /****************************************
        *                Methods
        ****************************************/

        public void ToggleCollapsed() { SetCollapsed(!_isCollapsed); }

        public void SetCollapsed(bool collapsed)
        {
            _isCollapsed = collapsed;
            ApplyCollapsed();
        }

        /// <summary>total의 최솟값은 0이며 current는 0부터 유효 total까지로 제한한다</summary>
        public void SetCount(int current, int total)
        {
            if (_countLabel == null)
            {
                return;
            }

            int validTotal = Mathf.Max(0, total);
            int validCurrent = Mathf.Clamp(current, 0, validTotal);
            _countLabel.SetText(COUNT_FORMAT, validCurrent, validTotal);
        }

        private void ApplyCollapsed()
        {
            ApplyEntries();
            ApplyMinimizeLabel();
        }

        private void ApplyEntries()
        {
            if (_entryRoots == null)
            {
                return;
            }

            for (int i = 0; i < _entryRoots.Length; ++i)
            {
                GameObject entry = _entryRoots[i];
                if (entry == null)
                {
                    continue;
                }

                entry.SetActive(!_isCollapsed);
            }
        }

        private void ApplyMinimizeLabel()
        {
            if (_minimizeLabel == null)
            {
                return;
            }

            _minimizeLabel.SetText(_isCollapsed ? COLLAPSED_LABEL : EXPANDED_LABEL);
        }
    }
}
