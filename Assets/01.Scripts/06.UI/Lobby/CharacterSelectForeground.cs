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
        [SerializeField] private RectTransform _foreground = null;
        [SerializeField] private float _horizontalOffset = 90f;
        [SerializeField] private float _scale = 1.12f;
        [SerializeField] private float _transitionDuration = 0.45f;

        private Tween _moveTween = null;

        protected override void OnViewAwake()
        {
            if ((_selection == null) || (_foreground == null))
            {
                Debug.LogError("[CharacterSelect] 전경 벚꽃의 선택 상태나 RectTransform이 연결되지 않았다", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if ((_selection == null) || (_foreground == null))
            {
                return;
            }

            _selection.SelectionChanged += OnSelectionChanged;
            _foreground.localScale = Vector3.one * _scale;
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
            Vector2 target = new Vector2(offset, 0f);
            _moveTween?.Kill();
            _moveTween = null;
            if (immediate)
            {
                _foreground.anchoredPosition = target;
                return;
            }

            _moveTween = DOTween.To(() => _foreground.anchoredPosition,
                value => _foreground.anchoredPosition = value, target, _transitionDuration)
                .SetEase(Ease.OutSine).SetLink(gameObject);
        }
    }
}
