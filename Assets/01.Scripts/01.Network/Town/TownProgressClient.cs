// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // TODO(진욱/기웅): 신시 안내(프롤로그, 튜토리얼)를 끝낸 시점에 CompleteIntro를 부른다(docs/05-implementation-roadmap.md U18,
    // docs/02-game-design.md 8장 "신시 안내 -> 숲 성공"). 안내 완료가 기록돼야 숲(region_forest) 출정이 열린다
    /// <summary> 클라이언트의 마을 계정 진행 기록 송신과 결과 수신 창구. 연결되지 않았으면 Guid.Empty나 거짓을 돌려준다 </summary>
    public static class TownProgressClient
    {
        /****************************************
        *              Properties
        ****************************************/

        public static event Action<IntroCompleteResultMessage> IntroResultReceived;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 클라이언트가 시작할 때 한 번 부른다 </summary>
        public static void RegisterHandlers()
        {
            NetworkClient.ReplaceHandler<IntroCompleteResultMessage>(OnIntroResult);
        }

        /// <summary> 새 operationId를 만들어 보낸다. 결과를 받지 못해 다시 보낼 때는 RetryCompleteIntro에 그 값을 넣는다 </summary>
        public static Guid CompleteIntro()
        {
            Guid operationId = Guid.NewGuid();
            return RetryCompleteIntro(operationId) ? operationId : Guid.Empty;
        }

        public static bool RetryCompleteIntro(Guid operationId)
        {
            if ((operationId == Guid.Empty) || (!NetworkClient.isConnected))
            {
                return false;
            }

            NetworkClient.Send(new IntroCompleteRequestMessage(operationId));
            return true;
        }

        private static void OnIntroResult(IntroCompleteResultMessage message)
        {
            IntroResultReceived?.Invoke(message);
        }
    }
}
