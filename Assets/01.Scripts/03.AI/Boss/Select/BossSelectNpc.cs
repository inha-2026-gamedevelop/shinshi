// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 가까이에서 F 키를 누르면 보스 선택 창을 여는 임시 NPC. 보스방 입장은 하지 않는다 </summary>
    public sealed class BossSelectNpc : MonoBehaviour
    {
        // 플레이어는 마을에서 네트워크로 생성되어 프리팹이 씬 오브젝트를 직접 잡을 수 없다. 씬 배치 쪽이 SetPlayer로 넘긴다
        // 키 입력은 구 Input Manager를 쓰며, F는 이 NPC 전용이라 공용 Constants에 올리지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const KeyCode KEY_INTERACT = KeyCode.F;

        [SerializeField] private Transform        _player = null;
        [SerializeField] private BossSelectPanel  _panel  = null;
        [SerializeField, Min(0f)] private float   _interactDistance = 3f; // m, 수평 거리

        private bool _isNear = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if ((_player == null) || (_panel == null))
            {
                return;
            }

            bool isNear = IsPlayerNear();

            if (isNear != _isNear)
            {
                _isNear = isNear;
                OnNearChanged();
            }

            if ((_isNear) && (!_panel.IsOpen) && (Input.GetKeyDown(KEY_INTERACT)))
            {
                _panel.Open();
            }
        }

        private void OnDisable()
        {
            if ((_panel != null) && (_isNear))
            {
                _panel.SetPromptVisible(false);
            }

            _isNear = false;
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SetPlayer(Transform player)
        {
            _player = player;
        }

        public void SetPanel(BossSelectPanel panel)
        {
            _panel = panel;
        }

        private bool IsPlayerNear()
        {
            Vector3 offset = _player.position - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= (_interactDistance * _interactDistance);
        }

        private void OnNearChanged()
        {
            if (_isNear)
            {
                _panel.SetPromptVisible(!_panel.IsOpen);
                return;
            }

            _panel.SetPromptVisible(false);

            // 멀어지면 창도 닫는다. 이동 입력이 막힌 채 창만 남는 일을 막는다
            if (_panel.IsOpen)
            {
                _panel.Close();
            }
        }
    }
}
