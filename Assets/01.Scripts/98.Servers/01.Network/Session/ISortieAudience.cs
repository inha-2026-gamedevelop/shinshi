namespace Shinshi.Network
{
    /// <summary> 던전 호스트가 참가자에게 출정 상태와 포기 결과를 보내는 창구, 서버 메인 스레드에서만 부른다 </summary>
    public interface ISortieAudience
    {
        /// <summary> 연결이 없으면 보내지 않는다 </summary>
        void SendStatus(int participantId, in SortieStatusMessage message);

        void SendAbandonResult(int participantId, in SortieAbandonResultMessage message);

        void SendStopVote(int participantId, in SortieStopVoteStateMessage message);
    }
}
