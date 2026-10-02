namespace Shinshi.Network
{
    /// <summary> 던전 서버가 영속 저장에 넘기는 세션 사실의 종류 </summary>
    public enum ESessionEventType : byte
    {
        ParticipantJoined,
        ParticipantLeft,
        ParticipantSettled,
        EnemyKilled,
        WaveCleared,
        SessionEnded,

        // 정산 전 추가 정기 적립, API 출정 원장에 쌓여 서비스 장애 취소의 지급 기준이 된다(50% 내림)
        EssenceAccrued,

        // 완료 웨이브의 기본 정기 적립, API 출정 원장에 쌓여 서비스 장애 취소 때 전액 지급된다
        BaseEssenceAccrued
    }
}
