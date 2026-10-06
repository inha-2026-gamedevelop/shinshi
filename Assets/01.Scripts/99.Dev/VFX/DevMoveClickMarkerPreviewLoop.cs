#if UNITY_EDITOR || DEVELOPMENT_BUILD
// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.UI;

namespace Shinshi.Dev
{
    /// <summary> 네트워크 없이 이동 표시 후보 하나를 받침 위에서 반복 재생하고, 받침을 이동 버튼으로 누르면 그 지점에 바로 재생한다 </summary>
    public sealed class DevMoveClickMarkerPreviewLoop : MonoBehaviour
    {
        // 서버가 없어 거절도 오지 않으므로 몇 번에 한 번씩 왕복 지연만큼 뒤에 거절 색으로 바꿔 거절 모양도 같이 보여 준다

        /****************************************
        *                Fields
        ****************************************/

        private const float RAYCAST_MAX_DISTANCE = 200f; // m

        // 실제 게임처럼 여러 개를 돌려 써서 빠르게 연달아 누르면 앞 표시가 겹쳐 남는 모습도 본다
        [SerializeField] private UiMoveClickMarker[] _markers;
        [SerializeField] private Collider            _pad;

        [Header("반복")]
        [SerializeField, Min(0.1f)] private float _interval    = 1.4f;
        [SerializeField, Min(0)]    private int   _rejectEvery = 4;     // 0이면 거절을 보여 주지 않는다
        [SerializeField, Min(0f)]   private float _rejectDelay = 0.12f; // 초, 서버 거절이 돌아오는 왕복 지연 흉내

        private Sequence          _loop       = null;
        private Camera            _camera     = null;
        private Vector3           _playPoint  = Vector3.zero;
        private UiMoveClickMarker _lastMarker = null;
        private int               _nextMarker = 0;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            if ((_markers == null) || (_markers.Length == 0) || (_pad == null))
            {
                Debug.LogError("[Dev] 이동 표시 미리보기에 표시나 받침이 연결되지 않았다", this);
                enabled = false;
                return;
            }

            Bounds bounds = _pad.bounds;
            _playPoint = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            _loop      = BuildLoop();
        }

        private void Update()
        {
            if ((_loop == null) || (!Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_MOVE)))
            {
                return;
            }

            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_camera == null)
            {
                return;
            }

            Ray ray = _camera.ScreenPointToRay(Input.mousePosition);
            if (!_pad.Raycast(ray, out RaycastHit hit, RAYCAST_MAX_DISTANCE))
            {
                return;
            }

            // 반복은 원래 지점에서 계속하고, 누른 지점에는 따로 한 번 재생한다
            PlayAt(hit.point);
        }

        /****************************************
        *                Methods
        ****************************************/

        // 매 바퀴 새로 만들지 않도록 수락 여러 번과 거절 한 번을 한 시퀀스에 담아 무한 반복한다
        private Sequence BuildLoop()
        {
            Sequence loop     = DOTween.Sequence();
            int      accepted = (_rejectEvery > 0) ? (_rejectEvery - 1) : 1;
            for (int i = 0; i < accepted; ++i)
            {
                loop.AppendCallback(PlayAccepted);
                loop.AppendInterval(_interval);
            }

            if (_rejectEvery > 0)
            {
                float delay = Mathf.Min(_rejectDelay, _interval);
                loop.AppendCallback(PlayAccepted);
                loop.AppendInterval(delay);
                loop.AppendCallback(MarkLastRejected);
                loop.AppendInterval(_interval - delay);
            }

            return loop.SetLoops(-1).SetLink(gameObject);
        }

        private void PlayAccepted()
        {
            PlayAt(_playPoint);
        }

        private void PlayAt(Vector3 point)
        {
            UiMoveClickMarker marker = _markers[_nextMarker];
            _nextMarker = (_nextMarker + 1) % _markers.Length;
            if (marker == null)
            {
                return;
            }

            _lastMarker = marker;
            marker.Play(point);
        }

        private void MarkLastRejected()
        {
            if (_lastMarker != null)
            {
                _lastMarker.MarkRejected();
            }
        }
    }
}
#endif
