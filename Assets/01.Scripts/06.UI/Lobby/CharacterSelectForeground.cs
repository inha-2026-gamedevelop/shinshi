// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> 선택한 캐릭터에 맞춰 화면 앞 벚꽃의 구도를 바꾼다 </summary>
    public sealed class CharacterSelectForeground : ClientView
    {
        [SerializeField] private CharacterSelectSelection _selection = null;
        [SerializeField] private RectTransform _leftBlossoms = null;
        [SerializeField] private RectTransform _rightBlossoms = null;
        [SerializeField] private float _horizontalOffset = 32f;
        [SerializeField] private float _transitionDuration = 0.45f;

        private Tween _moveTween = null;
        private float _currentOffset = 0f;

        protected override void OnViewAwake()
        {
            if ((_selection == null) || (_leftBlossoms == null) || (_rightBlossoms == null))
            {
                Debug.LogError("[CharacterSelect] 전경 벚꽃의 선택 상태나 RectTransform이 연결되지 않았다", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if ((_selection == null) || (_leftBlossoms == null) || (_rightBlossoms == null))
            {
                return;
            }

            _selection.SelectionChanged += OnSelectionChanged;
            SetPosition(_selection.SelectedSlot, true);
        }

        private void OnDisable()
        {
            if (_selection != null)
            {
                _selection.SelectionChanged -= OnSelectionChanged;
            }

            _moveTween?.Kill();
            _moveTween = null;
        }

        private void OnSelectionChanged(int slot) { SetPosition(slot, false); }

        private void SetPosition(int slot, bool immediate)
        {
            float offset = (slot == 0) ? _horizontalOffset : (slot == 2) ? -_horizontalOffset : 0f;
            _moveTween?.Kill();
            _moveTween = null;
            if (immediate)
            {
                ApplyOffset(offset);
                return;
            }

            _moveTween = DOTween.To(() => _currentOffset, ApplyOffset, offset, _transitionDuration)
                .SetEase(Ease.OutSine).SetLink(gameObject);
        }

        private void ApplyOffset(float offset)
        {
            _currentOffset = offset;
            _leftBlossoms.anchoredPosition = new Vector2(Mathf.Min(offset, 0f), 0f);
            _rightBlossoms.anchoredPosition = new Vector2(Mathf.Max(offset, 0f), 0f);
        }
    }
}
