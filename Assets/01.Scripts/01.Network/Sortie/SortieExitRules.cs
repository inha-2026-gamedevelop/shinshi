namespace Shinshi.Network
{
    /// <summary> 던전 연결을 떠날 때(메뉴의 캐릭터 선택, 서버의 연결 끊김) 마을 귀환, 포기, 이탈 저장 대기를 정한다 </summary>
    public static class SortieExitRules
    {
        // 던전은 진행 중에 끊긴 참가자를 재접속 유예 동안 서버에 남기고 계정 접속 임대도 그 던전이 쥔다. 클라이언트에는 던전 복귀
        // 경로가 없어 그대로 끊으면 유예가 끝날 때까지 마을, 프롤로그 입장이 이미 접속 중으로 거절된다. 그래서 메뉴로 나갈 때는
        // 포기를 먼저 보내고 이탈 기록이 저장됐다는 상태(API가 같은 트랜잭션에서 임대를 푼다)를 받은 뒤 끊는다.
        // 출정 결과가 저장돼 귀환할 수 있는 던전은 정산 뒤 결과 창 대기 시간이 지나면 서버가 연결을 닫는다. 그 끊김은 장애가 아니라
        // 마을로 돌아갈 차례이므로 캐릭터 선택 대신 마을 귀환 흐름으로 보낸다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 던전 연결이 끊겼을 때 캐릭터 선택 대신 마을로 돌아갈지, 마지막 출정 상태가 귀환 가능이면 참 </summary>
        public static bool ShouldReturnToTownOnDisconnect(bool isDungeonRole, bool hasStatus, in SortieStatusMessage status)
        {
            return (isDungeonRole) && (hasStatus) && (status.CanReturn);
        }

        /// <summary> 메뉴로 나가기 전에 포기를 보내야 하는지, 전투가 진행 중이고 내 종료가 아직 없으면 참 </summary>
        public static bool ShouldAbandonBeforeLeave(bool isDungeonRole, bool hasStatus, in SortieStatusMessage status)
        {
            return (isDungeonRole) && (hasStatus) && (status.Phase == ESortiePhase.Running) && (!status.HasPersonalClose);
        }

        /// <summary> 메뉴로 나가기 전에 이탈 또는 종료 저장을 기다려야 하는지, 귀환 가능 전이면 참 </summary>
        public static bool ShouldWaitBeforeLeave(bool isDungeonRole, bool hasStatus, in SortieStatusMessage status)
        {
            if ((!isDungeonRole) || (!hasStatus) || (status.CanReturn))
            {
                return false;
            }

            // 시작 전(Waiting)은 서버가 포기를 받지 않고, 닫힌 출정(Closed)은 곧 서버가 내려가 기다릴 신호가 없다
            return (status.Phase == ESortiePhase.Running) || (status.Phase == ESortiePhase.Ended)
                || (status.Phase == ESortiePhase.Auction);
        }

        /// <summary> 기다리던 이탈 또는 종료 저장이 끝났는지, 출정 상태가 없으면 기다릴 것이 없어 참 </summary>
        public static bool IsLeaveReady(bool hasStatus, in SortieStatusMessage status)
        {
            return (!hasStatus) || (status.CanReturn);
        }
    }
}
