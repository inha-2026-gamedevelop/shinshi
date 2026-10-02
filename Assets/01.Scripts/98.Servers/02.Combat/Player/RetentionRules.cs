namespace Shinshi.Combat
{
    /// <summary> 연결이 끊긴 참가자의 보존 기록이 만료됐을 때 명단까지 닫을지 정한다 </summary>
    public static class RetentionRules
    {
        // 명단을 닫으면 개인 실패 정산과 재합류 금지로 이어진다. 그래서 정산할 출정이 진행 중인 던전에서만 닫는다.
        // 마을은 출정이 없고, 던전 시작 전 끊김은 예약 만료까지 기다리므로 두 경우 모두 보존 상태만 버린다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 보존 기록 만료로 명단을 닫아야 하면 참, isDungeon은 출정 호스트가 있는 던전 역할인지 </summary>
        public static bool ClosesRosterOnExpiry(bool isDungeon, bool sessionStarted)
        {
            return (isDungeon) && (sessionStarted);
        }
    }
}
