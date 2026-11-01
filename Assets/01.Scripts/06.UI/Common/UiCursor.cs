// Unity
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>켜져 있는 동안 OS 커서를 숨기고 게임 커서 그림을 마우스 위치에 그린다. 꺼지면 OS 기본 커서로 돌린다</summary>
    public sealed class UiCursor : MonoBehaviour
    {
        // Windows는 하드웨어 커서를 32x32로 줄여 크기 설정이 보이지 않는다. 그래서 가장 위 캔버스에 그림을 직접 그려 크기를 정한다

        /****************************************
        *                Fields
        ****************************************/

        private const int OVERLAY_SORTING_ORDER = short.MaxValue;

        private enum ECursorState
        {
            None,
            Default,
            Pressed,
            Blocked
        }

        [Header("커서 그림")]
        [SerializeField] private Texture2D _defaultTexture = null;
        [SerializeField] private Texture2D _pressedTexture = null;
        [SerializeField] private Texture2D _blockedTexture = null;
        [Tooltip("그림 왼쪽 위 기준 픽셀 좌표. 클릭이 실제로 찍히는 화살 끝 위치")]
        [SerializeField] private Vector2   _hotspot        = Vector2.zero;

        [Header("불가 대상 감지")]
        [Tooltip("비워 두면 시작할 때 Main Camera를 쓴다")]
        [SerializeField] private Camera    _camera     = null;
        [SerializeField] private LayerMask _targetMask = ~0;

        [Header("불가 표시")]
        [Tooltip("FlashBlocked로 불가 커서를 띄워 두는 시간(초)")]
        [SerializeField] private float _flashDuration = 0.4f;

        private ECursorState _state        = ECursorState.None;
        private float        _blockedUntil = 0f;
        private float        _scale        = 1f;

        private GameObject    _overlay   = null;
        private RawImage      _image     = null;
        private RectTransform _imageRect = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            // 쿼터뷰 씬마다 출력 카메라가 Main Camera 하나라 연결을 빠뜨린 경우에만 한 번 찾는다
            if (_camera == null)
            {
                _camera = Camera.main;
            }
        }

        private void OnEnable()
        {
            _state = ECursorState.None;
            _scale = CursorScale.Current;
            CursorScale.Changed += OnScaleChanged;
            EnsureOverlay();
            _overlay.SetActive(true);
            Cursor.visible = false;
            Refresh();
            FollowMouse();
        }

        private void OnDisable()
        {
            CursorScale.Changed -= OnScaleChanged;
            _state = ECursorState.None;
            if (_overlay != null)
            {
                _overlay.SetActive(false);
            }
            Cursor.visible = true;
        }

        private void OnDestroy()
        {
            if (_overlay != null)
            {
                Destroy(_overlay);
            }
        }

        private void Update()
        {
            Refresh();
        }

        private void LateUpdate()
        {
            FollowMouse();
        }

        // 창 전환 뒤 OS가 커서를 다시 보이게 하는 경우가 있어 포커스를 얻을 때 다시 숨긴다
        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                Cursor.visible = false;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>할 수 없는 동작을 시도했을 때 잠깐 불가 커서를 띄운다</summary>
        public void FlashBlocked()
        {
            _blockedUntil = Time.unscaledTime + _flashDuration;
            Refresh();
        }

        private void EnsureOverlay()
        {
            if (_overlay != null)
            {
                return;
            }

            // 다른 캔버스의 영향을 받지 않도록 부모 없이 만든다
            _overlay = new GameObject("UiCursorOverlay", typeof(RectTransform), typeof(Canvas));
            var canvas = _overlay.GetComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = OVERLAY_SORTING_ORDER;

            var imageObject = new GameObject("Cursor", typeof(RectTransform), typeof(RawImage));
            imageObject.transform.SetParent(_overlay.transform, false);
            _imageRect = (RectTransform)imageObject.transform;
            _imageRect.anchorMin = Vector2.zero;
            _imageRect.anchorMax = Vector2.zero;
            _imageRect.pivot     = new Vector2(0f, 1f);

            _image = imageObject.GetComponent<RawImage>();
            _image.raycastTarget = false;
        }

        private void Refresh()
        {
            ECursorState next = ResolveState();
            if (next == _state)
            {
                return;
            }

            Texture2D texture = GetTexture(next);
            if (texture == null)
            {
                return;
            }

            _state         = next;
            _image.texture = texture;
            ApplySize(texture);
        }

        private void OnScaleChanged(float scale)
        {
            _scale = scale;
            if (_image.texture != null)
            {
                ApplySize((Texture2D)_image.texture);
            }
        }

        // 그림 크기에 배율을 곱한 픽셀 크기로 그린다
        private void ApplySize(Texture2D texture)
        {
            _imageRect.sizeDelta = new Vector2(texture.width * _scale, texture.height * _scale);
        }

        // 클릭이 찍히는 화살 끝이 마우스 위치에 오도록 핫스팟만큼 당겨서 놓는다
        private void FollowMouse()
        {
            if (_image.texture == null)
            {
                return;
            }

            Vector3 mouse = Input.mousePosition;
            bool inside = (mouse.x >= 0f) && (mouse.y >= 0f) && (mouse.x <= Screen.width) && (mouse.y <= Screen.height);
            if (_image.enabled != inside)
            {
                _image.enabled = inside;
            }

            _imageRect.anchoredPosition = new Vector2(mouse.x - (_hotspot.x * _scale), mouse.y + (_hotspot.y * _scale));
        }

        private ECursorState ResolveState()
        {
            if ((Time.unscaledTime < _blockedUntil) || (IsOverBlockedTarget()))
            {
                return ECursorState.Blocked;
            }

            if ((Input.GetMouseButton(Constants.Player.MOUSE_BUTTON_MOVE)) || (Input.GetMouseButton(Constants.Player.MOUSE_BUTTON_ATTACK)))
            {
                return ECursorState.Pressed;
            }

            return ECursorState.Default;
        }

        private bool IsOverBlockedTarget()
        {
            if (_camera == null)
            {
                return false;
            }

            // HUD 위에서는 뒤에 가려진 월드 대상을 불가로 표시하지 않는다
            EventSystem eventSystem = EventSystem.current;
            if ((eventSystem != null) && (eventSystem.IsPointerOverGameObject()))
            {
                return false;
            }

            Ray ray = _camera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, Constants.Player.CURSOR_RAYCAST_MAX_DISTANCE, _targetMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            return UiCursorBlockedTarget.Contains(hit.collider);
        }

        private Texture2D GetTexture(ECursorState state)
        {
            switch (state)
            {
                case ECursorState.Pressed:
                    return (_pressedTexture != null) ? _pressedTexture : _defaultTexture;
                case ECursorState.Blocked:
                    return (_blockedTexture != null) ? _blockedTexture : _defaultTexture;
                default:
                    return _defaultTexture;
            }
        }
    }
}
