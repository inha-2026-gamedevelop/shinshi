// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> 이 클라이언트의 플레이어가 이동 요청을 보내면 찍은 지점에 이동 표시를 재생한다, 마을과 사냥터에서 같이 쓴다 </summary>
    public sealed class UiMoveClickMarkerBinder : ClientView
    {
        // 플레이어 프리팹은 네트워크로 생성되어 씬의 표시가 직접 참조할 수 없다. 로컬 플레이어는 스폰 뒤에 정해지고
        // 재접속하면 새 개체로 바뀌므로 바뀌었을 때만 다시 묶는다.
        // 클릭 판정은 PlayerInputReader가 하고, 여기서는 실제로 보낸 이동(MoveSent)만 받는다. 창 위 클릭이나
        // 지면을 못 찾은 클릭은 보내지 않으므로 표시도 뜨지 않는다

        /****************************************
        *                Fields
        ****************************************/

        // 직전 이동을 보낸 직후의 클릭은 재경로 간격만큼 기다렸다 나간다. 그 사이 버튼을 떼도 같은 클릭으로 본다
        private const double PRESS_MATCH_SECONDS = Constants.Player.MOVE_REPATH_MIN_INTERVAL_SECONDS * 2.0;

        // 같은 표시 여러 개를 돌려 쓴다. 연달아 찍어도 앞 표시가 끊기지 않고 사라지는 동안 새 표시가 겹쳐 보인다
        [SerializeField] private UiMoveClickMarker[] _markers;

        private NetworkIdentity   _boundIdentity = null;
        private PlayerInputState  _input         = null;
        private bool              _hasPress      = false;
        private double            _pressTime     = 0.0;
        private UiMoveClickMarker _shownMarker   = null;
        private uint              _shownSequence = 0;
        private int               _shownFrame    = -1;
        private int               _nextMarker    = 0;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnDisable()
        {
            Unbind();
        }

        private void Update()
        {
            if ((_markers == null) || (_markers.Length == 0))
            {
                return;
            }

            // 누른 프레임에 이미 표시했으면 같은 누름을 다시 기다리지 않는다
            if ((Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_MOVE)) && (Time.frameCount != _shownFrame))
            {
                _hasPress  = true;
                _pressTime = Time.unscaledTimeAsDouble;
            }

            NetworkIdentity localPlayer = NetworkClient.localPlayer;
            if (localPlayer == _boundIdentity)
            {
                return;
            }

            Unbind();
            Bind(localPlayer);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Bind(NetworkIdentity localPlayer)
        {
            if (localPlayer == null)
            {
                return;
            }

            _boundIdentity = localPlayer;
            if (!localPlayer.TryGetComponent(out _input))
            {
                return;
            }

            _input.MoveSent      += OnMoveSent;
            _input.InputRejected += OnInputRejected;
        }

        private void Unbind()
        {
            if (_input != null)
            {
                _input.MoveSent      -= OnMoveSent;
                _input.InputRejected -= OnInputRejected;
            }

            _boundIdentity = null;
            _input         = null;
            _shownMarker   = null;
        }

        // 누르고 있는 동안 커서를 따라 다시 보내는 이동에는 표시를 띄우지 않고, 새로 누른 클릭에만 띄운다
        private void OnMoveSent(uint sequence, Vector3 destination)
        {
            // 이 컴포넌트와 PlayerInputState의 Update 순서는 정해져 있지 않아, 누른 프레임에 보낸 이동은 여기서 직접 확인한다
            bool isNewPress = (Input.GetMouseButtonDown(Constants.Player.MOUSE_BUTTON_MOVE))
                || ((_hasPress) && ((Time.unscaledTimeAsDouble - _pressTime) <= PRESS_MATCH_SECONDS));
            _hasPress = false;
            if (!isNewPress)
            {
                return;
            }

            UiMoveClickMarker marker = _markers[_nextMarker];
            _nextMarker = (_nextMarker + 1) % _markers.Length;
            if (marker == null)
            {
                return;
            }

            _shownMarker   = marker;
            _shownSequence = sequence;
            _shownFrame    = Time.frameCount;
            marker.Play(destination);
        }

        // 클릭 지점 근처에 걸을 수 있는 땅이 없으면 서버가 거절한다. 그 클릭의 표시만 거절 색으로 바꾼다
        private void OnInputRejected(uint sequence, ERejectReason reason)
        {
            if ((_shownMarker == null) || (sequence != _shownSequence))
            {
                return;
            }

            _shownMarker.MarkRejected();
        }
    }
}
