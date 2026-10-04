// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Combat
{
    // 물약 HUD(UI 담당)는 이 창구의 상태와 이벤트만 읽는다. 사용 요청은 PlayerInputState.RequestPotion으로 보내고 판정은 서버가 한다
    /// <summary> 클라이언트의 물약 상태 수신 창구 </summary>
    public static class PotionClient
    {
        /****************************************
        *                Fields
        ****************************************/

        private static PotionStateMessage _current;
        private static bool               _hasState;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 마지막으로 받은 물약 상태, HasState가 거짓이면 기본값이다 </summary>
        public static PotionStateMessage Current  => _current;
        public static bool               HasState => _hasState;

        public static event Action<PotionStateMessage> StateChanged;

        /****************************************
        *                Methods
        ****************************************/

        // 서버는 출정 중 물약 상태를 소유자에게 보낸다. 처리기가 없으면 Mirror 클라이언트가 모르는 메시지로 연결을 끊는다
        /// <summary> 클라이언트가 시작할 때 한 번 부른다, 이전 연결의 상태를 비운다 </summary>
        public static void RegisterHandlers()
        {
            _current  = default;
            _hasState = false;
            NetworkClient.ReplaceHandler<PotionStateMessage>(OnState);
        }

        private static void OnState(PotionStateMessage message)
        {
            _current  = message;
            _hasState = true;
            StateChanged?.Invoke(message);
        }
    }
}
