// Unity
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 서드파티
using DG.Tweening;

namespace Shinshi.UI
{
    /// <summary>마우스를 올리면 밝아지고, 누르는 동안 가운데를 기준으로 조금 작아지며 어두워진다. Button의 색 전환 대신 쓴다</summary>
    public sealed class UiButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        // 상태가 바뀔 때마다 지금 보이는 값에서 새 목표로 다시 출발해야 빠르게 오가도 튀지 않는다.
        // 비율 하나를 0에서 1로 돌리고 출발값과 목표값을 섞어 쓰는 이유는 크기와 색을 한 트윈으로 묶기 위해서다.
        // 배치 JSON의 pivot은 왼쪽 위라 그대로 줄이면 한쪽으로 쏠리므로, 크기에 맞춰 위치를 옮겨 가운데를 고정한다

        /****************************************
        *                Fields
        ****************************************/

        private const float FULL_PROGRESS = 1f;

        [Header("대상")]
        [SerializeField] private Graphic _graphic = null;

        [Header("크기")]
        [SerializeField] private float _pressedScale = 0.97f;

        [Header("색")]
        [SerializeField] private Color _normalColor  = new Color(0.88f, 0.88f, 0.88f, 1f);
        [SerializeField] private Color _hoverColor   = Color.white;
        [SerializeField] private Color _pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);

        [Header("시간")]
        [SerializeField] private float _duration = 0.1f;

        private RectTransform _rect         = null;
        private Vector2       _basePosition = Vector2.zero;
        private Selectable    _selectable   = null;
        private Tweener       _tween        = null;
        private float         _progress     = 0f;
        private float         _fromScale    = 1f;
        private float         _toScale      = 1f;
        private Color         _fromColor    = Color.white;
        private Color         _toColor      = Color.white;
        private bool          _isHovered    = false;
        private bool          _isPressed    = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _rect         = (RectTransform)transform;
            _basePosition = _rect.anchoredPosition;
            _selectable   = GetComponent<Selectable>();
            if (_graphic == null)
            {
                _graphic = GetComponent<Graphic>();
            }

            _tween = DOTween.To(() => _progress, SetProgress, FULL_PROGRESS, _duration)
                .SetUpdate(true)
                .SetAutoKill(false)
                .Pause()
                .SetLink(gameObject);
            ApplyImmediate();
        }

        private void OnEnable()
        {
            _isHovered = false;
            _isPressed = false;
            ApplyImmediate();
        }

        private void OnDisable()
        {
            _isHovered = false;
            _isPressed = false;
            ApplyImmediate();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovered = true;
            Transition();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isHovered = false;
            Transition();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _isPressed = true;
            Transition();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _isPressed = false;
            Transition();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Transition()
        {
            if (_tween == null)
            {
                return;
            }

            _fromScale = _rect.localScale.x;
            _fromColor = (_graphic != null) ? _graphic.color : Color.white;
            GetTarget(out _toScale, out _toColor);

            _progress = 0f;
            _tween.ChangeEndValue(FULL_PROGRESS, 0f, true);
            _tween.Restart();
        }

        private void ApplyImmediate()
        {
            if (_tween == null)
            {
                return;
            }

            _tween.Pause();
            GetTarget(out _toScale, out _toColor);
            _fromScale = _toScale;
            _fromColor = _toColor;
            SetProgress(FULL_PROGRESS);
        }

        // 눌린 상태가 올린 상태보다 앞서며, 사용할 수 없는 버튼은 반응하지 않는다
        private void GetTarget(out float scale, out Color color)
        {
            bool isActive = (_selectable == null) || _selectable.IsInteractable();
            if (isActive && _isPressed)
            {
                scale = _pressedScale;
                color = _pressedColor;
                return;
            }

            if (isActive && _isHovered)
            {
                scale = 1f;
                color = _hoverColor;
                return;
            }

            scale = 1f;
            color = _normalColor;
        }

        private void SetProgress(float progress)
        {
            _progress = progress;
            float scale = Mathf.Lerp(_fromScale, _toScale, progress);
            _rect.localScale = new Vector3(scale, scale, 1f);

            // 크기가 s일 때 가운데가 (0.5 - pivot) * size * s만큼 움직이므로 그만큼 되돌려 가운데를 고정한다
            Vector2 centerOffset = (new Vector2(0.5f, 0.5f) - _rect.pivot) * _rect.rect.size;
            _rect.anchoredPosition = _basePosition + (centerOffset * (1f - scale));

            if (_graphic != null)
            {
                _graphic.color = Color.Lerp(_fromColor, _toColor, progress);
            }
        }
    }
}
