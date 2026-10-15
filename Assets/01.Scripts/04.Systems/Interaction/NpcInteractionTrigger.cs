// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Systems
{
    /// <summary> 내 캐릭터가 가까이 있을 때 상호작용 키를 누르면 알린다. 가까움 안내 오브젝트도 함께 켜고 끈다 </summary>
    // 이 컴포넌트는 창을 열지 않고 의도만 알린다. 창을 여는 쪽(바인더)이 Interacted를 받는다.
    // 거리는 클라이언트 편의 판정이다. 입장 같은 실제 결과는 서버가 요청을 따로 검증한다.
    // 키 입력은 구 Input Manager를 쓴다. 상호작용 키는 NPC 공용이라 단축키 설정 에셋 대신 여기에 둔다
    public sealed class NpcInteractionTrigger : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private KeyCode _interactKey = KeyCode.F;

        // 수평 거리(m)
        [SerializeField, Min(0f)] private float _interactDistance = 3.5f;

        // 가까이 갔을 때만 켜는 안내(상호작용 키 표시), 비워 두면 켜고 끄지 않는다
        [SerializeField] private GameObject _promptRoot = null;

        // 개발 씬처럼 네트워크 플레이어가 없을 때 쓰는 고정 대상, 비워 두면 내 플레이어를 쓴다
        [SerializeField] private Transform _fixedPlayer = null;

        private bool _isNear = false;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsNear => _isNear;

        public KeyCode InteractKey => _interactKey;

        /// <summary> 가까움이 바뀔 때 알린다, 멀어지면 거짓이다 </summary>
        public event Action<bool> NearChanged;

        /// <summary> 가까운 채 상호작용 키를 눌렀을 때 알린다 </summary>
        public event Action Interacted;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            SetPrompt(false);
        }

        private void OnDisable()
        {
            SetNear(false);
        }

        private void Update()
        {
            Transform player = ResolvePlayer();
            SetNear((player != null) && (IsWithin(player.position)));

            if ((_isNear) && (Input.GetKeyDown(_interactKey)) && (!TextInputFocus.IsTyping()))
            {
                Interacted?.Invoke();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 위치가 수평으로 상호작용 거리 안인지 </summary>
        public bool IsWithin(Vector3 position)
        {
            Vector3 offset = position - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= (_interactDistance * _interactDistance);
        }

        private Transform ResolvePlayer()
        {
            if (_fixedPlayer != null)
            {
                return _fixedPlayer;
            }

            NetworkIdentity local = NetworkClient.localPlayer;
            return (local != null) ? local.transform : null;
        }

        private void SetNear(bool near)
        {
            if (near == _isNear)
            {
                return;
            }

            _isNear = near;
            SetPrompt(near);
            NearChanged?.Invoke(near);
        }

        private void SetPrompt(bool visible)
        {
            if (_promptRoot != null)
            {
                _promptRoot.SetActive(visible);
            }
        }
    }
}
