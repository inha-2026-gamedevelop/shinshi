// Unity
using UnityEngine;
using UnityEngine.EventSystems;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>켜져 있는 동안 마우스 커서를 게임 커서 그림으로 바꾸고, 꺼지면 OS 기본 커서로 돌린다</summary>
    public sealed class UiCursor : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

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

        private readonly Texture2D[] _scaled = new Texture2D[3];

        private ECursorState _state        = ECursorState.None;
        private float        _blockedUntil = 0f;
        private float        _scale        = 1f;

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
            Refresh();
        }

        private void OnDisable()
        {
            CursorScale.Changed -= OnScaleChanged;
            _state = ECursorState.None;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        private void OnDestroy()
        {
            ClearScaled();
        }

        private void Update()
        {
            Refresh();
        }

        // 창 전환이나 해상도 변경 뒤 OS가 커서를 기본값으로 되돌리는 경우가 있어 다시 건다
        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                _state = ECursorState.None;
                Refresh();
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

            _state = next;
            Cursor.SetCursor(Scaled(texture, next), _hotspot * _scale, CursorMode.Auto);
        }

        private void OnScaleChanged(float scale)
        {
            _scale = scale;
            ClearScaled();
            _state = ECursorState.None;
            Refresh();
        }

        // 원본 그림은 그대로 두고 상태별로 배율을 적용한 복사본을 한 번만 만든다
        private Texture2D Scaled(Texture2D source, ECursorState state)
        {
            if (Mathf.Approximately(_scale, 1f))
            {
                return source;
            }

            int index = (int)state - 1;
            if (_scaled[index] == null)
            {
                _scaled[index] = Resize(source, Mathf.Max(1, Mathf.RoundToInt(source.width * _scale)), Mathf.Max(1, Mathf.RoundToInt(source.height * _scale)));
            }
            return _scaled[index];
        }

        private static Texture2D Resize(Texture2D source, int width, int height)
        {
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            target.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            return result;
        }

        private void ClearScaled()
        {
            for (int i = 0; i < _scaled.Length; ++i)
            {
                if (_scaled[i] != null)
                {
                    Destroy(_scaled[i]);
                    _scaled[i] = null;
                }
            }
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
