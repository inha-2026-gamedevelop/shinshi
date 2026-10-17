namespace Shinshi.AI
{
    // 규칙 스냅샷(WolbaekTalismanSnapshot)에 없는 값만 더한다: 성공 시각(반격 동작의 경과)과 마지막 유지 초기화.
    // 시각을 모르는 값은 double.NaN이다
    /// <summary> 늦은 접속, 재접속에 넘기는 부적진의 현재 상태 </summary>
    public struct BossWolbaekTalismanRestore
    {
        /****************************************
        *              Properties
        ****************************************/

        public WolbaekTalismanSnapshot Snapshot { get; set; }

        /// <summary> 자리 유지에 성공한 서버 시각, 반격 중이 아니면 NaN이다 </summary>
        public double SuccessServerTime { get; set; }

        /// <summary> 유지가 초기화된 적이 있고 그 뒤로 다시 유지를 시작하지 않았다 </summary>
        public bool HasLastReset { get; set; }

        public double LastResetServerTime { get; set; }

        public EWolbaekHoldReset LastResetReason { get; set; }

        /// <summary> 처음 자리를 벗어난 참가자의 전투 번호 </summary>
        public int LastResetCombatId { get; set; }
    }
}
