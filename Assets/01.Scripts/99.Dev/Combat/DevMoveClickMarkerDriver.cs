// Unity
using UnityEngine;

// 프로젝트
using Shinshi.UI;

namespace Shinshi.Dev
{
    /// <summary> 시험 플레이어가 이동을 새로 찍을 때마다 그 지점에 이동 표시를 재생하는 시험용 연결부 </summary>
    public sealed class DevMoveClickMarkerDriver : MonoBehaviour
    {
        // 실제로는 이동 요청을 서버로 보낸 뒤 UiMoveClickMarkerBinder가 재생한다. 서버가 없는 시험 씬에서는 플레이어의 클릭 알림을 그 대역으로 쓴다.
        // 같은 표시 여러 개를 돌려 써서 연달아 찍어도 앞 표시가 끊기지 않고 사라지는 동안 새 표시가 겹쳐 보이게 한다

        /****************************************
        *                Fields
        ****************************************/

        [Header("참조")]
        [SerializeField] private DevHitFeedbackPlayer _player = null;
        [SerializeField] private UiMoveClickMarker[]  _markers = null;

        private int _nextMarker = 0;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if ((_player == null) || (_markers == null) || (_markers.Length == 0))
            {
                Debug.LogError("[DevMarker] 플레이어나 이동 표시가 연결되지 않았다", this);
                enabled = false;
                return;
            }

            _player.MoveClicked += OnMoveClicked;
        }

        private void OnDisable()
        {
            if (_player != null)
            {
                _player.MoveClicked -= OnMoveClicked;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnMoveClicked(Vector3 point)
        {
            UiMoveClickMarker marker = _markers[_nextMarker];
            _nextMarker = (_nextMarker + 1) % _markers.Length;
            if (marker != null)
            {
                marker.Play(point);
            }
        }
    }
}
