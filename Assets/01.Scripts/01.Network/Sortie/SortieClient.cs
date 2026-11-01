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
        private static WaveProgressMessage _waveProgress;
        private static bool                _hasWaveProgress;
        private static SortieRewardMessage _reward;
        private static bool                _hasReward;
        private static SortieStopVoteStateMessage _stopVote;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 마지막으로 받은 출정 상태, HasStatus가 거짓이면 기본값이다 </summary>
        public static SortieStatusMessage Current   => _current;
        public static bool                HasStatus => _hasStatus;

        /// <summary> 마지막으로 받은 일반전 웨이브 진행, 보스전에는 오지 않는다 </summary>
        public static WaveProgressMessage WaveProgress    => _waveProgress;
        public static bool                HasWaveProgress => _hasWaveProgress;

        /// <summary> 마지막으로 받은 정산 보상 내역, 출정이 끝나 정산안이 만들어진 뒤에 온다 </summary>
        public static SortieRewardMessage Reward    => _reward;
        public static bool                HasReward => _hasReward;

        // 결과를 아직 받지 못한 포기 요청이 있다
        public static bool IsAbandonPending => _pendingAbandon != Guid.Empty;

        public static event Action<SortieStatusMessage>        StatusChanged;
        public static event Action<SortieAbandonResultMessage> AbandonResult;
        public static event Action<SortieRewardMessage>        RewardReceived;
        public static event Action<SortieStopVoteStateMessage> StopVoteChanged;

        /// <summary> 마지막으로 받은 토벌 중단 투표 상태, 받은 적이 없으면 None이다 </summary>
        public static SortieStopVoteStateMessage StopVote => _stopVote;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 클라이언트가 시작할 때 한 번 부른다, 이전 연결의 상태를 비운다 </summary>
        public static void RegisterHandlers()
        {
            _current        = default;
            _hasStatus      = false;
            _pendingAbandon = Guid.Empty;
            _waveProgress    = default;
            _hasWaveProgress = false;
            _reward          = default;
            _hasReward       = false;
            _stopVote        = default;
            NetworkClient.ReplaceHandler<SortieStatusMessage>(OnStatus);
            NetworkClient.ReplaceHandler<SortieAbandonResultMessage>(OnAbandonResult);
            NetworkClient.ReplaceHandler<WaveProgressMessage>(OnWaveProgress);
            NetworkClient.ReplaceHandler<SortieRewardMessage>(OnReward);
            NetworkClient.ReplaceHandler<SortieStopVoteStateMessage>(OnStopVote);
        }

        /// <summary> 토벌 중단 표를 보낸다, 열린 투표가 없을 때 찬성하면 새 투표를 연다. 연결되지 않았으면 거짓이다 </summary>
        public static bool RequestStopVote(bool agree)
        {
            if (!NetworkClient.isConnected)
            {
                return false;
            }

            NetworkClient.Send(new SortieStopVoteRequestMessage(agree));
            return true;
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

        private static void OnWaveProgress(WaveProgressMessage message)
        {
            _waveProgress    = message;
            _hasWaveProgress = true;
        }

        private static void OnReward(SortieRewardMessage message)
        {
            _reward    = message;
            _hasReward = true;
            RewardReceived?.Invoke(message);
        }

        private static void OnStopVote(SortieStopVoteStateMessage message)
        {
            _stopVote = message;
            StopVoteChanged?.Invoke(message);
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
