// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Combat
{
    // 전투 HUD, 결과 화면 바인더는 이 창구의 값과 이벤트만 읽는다. 클라이언트는 어떤 값도 계산하지 않고 서버가 보낸 값을 그대로 보관한다.
    // 최종, 결과 메시지는 SessionEpoch로 중복 발행을 막는다 - 재접속 중 서버가 같은 메시지를 다시 보내도 화면이 다시 뜨지 않는다.
    // 연결 종료는 실시간 값만 지우는 ClearRealtime, 클라이언트 재시작은 전체를 지우는 Clear로 나뉜다(결과 화면 유지)
    /// <summary> 클라이언트의 딜미터 수신 창구 </summary>
    public static class CombatMeterClient
    {
        /****************************************
        *                Fields
        ****************************************/

        private static CombatMeterSelfMessage      _self;
        private static CombatMeterPartyMessage     _party;
        private static CombatMeterFinalSelfMessage _finalSelf;
        private static CombatMeterResultMessage    _result;

        private static bool _hasSelf;
        private static bool _hasParty;
        private static bool _hasFinalSelf;
        private static bool _hasResult;

        private static bool _hasFinalEpoch;
        private static int  _finalEpoch;
        private static bool _hasResultEpoch;
        private static int  _resultEpoch;

        /****************************************
        *              Properties
        ****************************************/

        public static CombatMeterSelfMessage      Self      => _self;
        public static CombatMeterPartyMessage     Party     => _party;
        public static CombatMeterFinalSelfMessage FinalSelf => _finalSelf;
        public static CombatMeterResultMessage    Result    => _result;

        public static bool HasSelf      => _hasSelf;
        public static bool HasParty     => _hasParty;
        public static bool HasFinalSelf => _hasFinalSelf;
        public static bool HasResult    => _hasResult;

        public static event Action<CombatMeterSelfMessage>      SelfChanged;
        public static event Action<CombatMeterPartyMessage>     PartyChanged;
        public static event Action<CombatMeterFinalSelfMessage> FinalSelfReceived;
        public static event Action<CombatMeterResultMessage>    ResultReceived;

        // 바인더가 CombatId -> 이름 캐시처럼 실시간 값에 묶인 자기 상태를 함께 비우도록 알려준다
        /// <summary> ClearRealtime(연결 종료, 클라이언트 시작)이 실시간 값을 비울 때마다 울린다 </summary>
        public static event Action RealtimeCleared;

        /****************************************
        *                Methods
        ****************************************/

        // 일반전에는 메시지가 오지 않으므로 처리기가 없으면 Mirror 클라이언트가 모르는 메시지로 연결을 끊는다
        /// <summary> 클라이언트가 시작할 때 한 번 부른다, 이전 연결의 값을 비운다 </summary>
        public static void RegisterHandlers()
        {
            Clear();
            NetworkClient.ReplaceHandler<CombatMeterSelfMessage>(OnSelf);
            NetworkClient.ReplaceHandler<CombatMeterPartyMessage>(OnParty);
            NetworkClient.ReplaceHandler<CombatMeterFinalSelfMessage>(OnFinalSelf);
            NetworkClient.ReplaceHandler<CombatMeterResultMessage>(OnResult);
        }

        /// <summary> 클라이언트 시작 때 이전 연결의 실시간, 최종, 결과 값을 모두 비운다 </summary>
        public static void Clear()
        {
            _finalSelf = default;
            _result    = default;

            _hasFinalSelf = false;
            _hasResult    = false;

            _hasFinalEpoch  = false;
            _finalEpoch     = 0;
            _hasResultEpoch = false;
            _resultEpoch    = 0;

            // 결과를 먼저 비운 뒤 알려야 구독자가 빈 결과로 다시 그린다
            ClearRealtime();
        }

        // 연결 종료 때는 이 값만 비운다. FinalSelf/Result는 결과 화면이 계속 보여줘야 하므로 다음
        // 보스전의 새 SessionEpoch 수신(OnFinalSelf/OnResult) 때 교체된다
        /// <summary> 연결 종료 때 실시간 값(Self/Party)만 비운다, 최종·결과는 남긴다 </summary>
        public static void ClearRealtime()
        {
            _self  = default;
            _party = default;

            _hasSelf  = false;
            _hasParty = false;

            RealtimeCleared?.Invoke();
        }

        private static void OnSelf(CombatMeterSelfMessage message)
        {
            _self    = message;
            _hasSelf = true;
            SelfChanged?.Invoke(message);
        }

        private static void OnParty(CombatMeterPartyMessage message)
        {
            _party    = message;
            _hasParty = true;
            PartyChanged?.Invoke(message);
        }

        private static void OnFinalSelf(CombatMeterFinalSelfMessage message)
        {
            if ((_hasFinalEpoch) && (_finalEpoch == message.SessionEpoch))
            {
                return;
            }

            _hasFinalEpoch = true;
            _finalEpoch    = message.SessionEpoch;
            _finalSelf     = message;
            _hasFinalSelf  = true;
            FinalSelfReceived?.Invoke(message);
        }

        private static void OnResult(CombatMeterResultMessage message)
        {
            if ((_hasResultEpoch) && (_resultEpoch == message.SessionEpoch))
            {
                return;
            }

            _hasResultEpoch = true;
            _resultEpoch    = message.SessionEpoch;
            _result         = message;
            _hasResult      = true;
            ResultReceived?.Invoke(message);
        }
    }
}
