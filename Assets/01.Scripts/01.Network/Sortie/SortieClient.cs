// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 출정 화면(UI 담당)은 이 창구의 이벤트만 구독하고 포기는 RequestAbandon으로 보낸다. 판정은 모두 서버가 한다.
    // 귀환 가능(CanReturn)을 받은 뒤 마을로 옮기는 흐름은 SortieReturnFlow가 맡는다
    /// <summary> 클라이언트의 출정 상태 수신과 포기 송신 창구 </summary>
    public static class SortieClient
    {
        /****************************************
        *                Fields
        ****************************************/

        private static SortieStatusMessage _current;
        private static bool                _hasStatus;
        private static Guid                _pendingAbandon;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 마지막으로 받은 출정 상태, HasStatus가 거짓이면 기본값이다 </summary>
        public static SortieStatusMessage Current   => _current;
        public static bool                HasStatus => _hasStatus;

        // 결과를 아직 받지 못한 포기 요청이 있다
        public static bool IsAbandonPending => _pendingAbandon != Guid.Empty;

        public static event Action<SortieStatusMessage>        StatusChanged;
        public static event Action<SortieAbandonResultMessage> AbandonResult;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 클라이언트가 시작할 때 한 번 부른다, 이전 연결의 상태를 비운다 </summary>
        public static void RegisterHandlers()
        {
            _current        = default;
            _hasStatus      = false;
            _pendingAbandon = Guid.Empty;
            NetworkClient.ReplaceHandler<SortieStatusMessage>(OnStatus);
            NetworkClient.ReplaceHandler<SortieAbandonResultMessage>(OnAbandonResult);
        }

        /// <summary> 포기를 보낸다, 결과 전에 다시 누르면 같은 작업 ID로 다시 보낸다. 연결되지 않았으면 거짓이다 </summary>
        public static bool RequestAbandon()
        {
            if (!NetworkClient.isConnected)
            {
                return false;
            }

            if (_pendingAbandon == Guid.Empty)
            {
                _pendingAbandon = Guid.NewGuid();
            }

            NetworkClient.Send(new SortieAbandonRequestMessage(_pendingAbandon));
            return true;
        }

        private static void OnStatus(SortieStatusMessage message)
        {
            _current   = message;
            _hasStatus = true;
            StatusChanged?.Invoke(message);
        }

        private static void OnAbandonResult(SortieAbandonResultMessage message)
        {
            if (message.OperationId == _pendingAbandon)
            {
                _pendingAbandon = Guid.Empty;
            }

            AbandonResult?.Invoke(message);
        }
    }
}
