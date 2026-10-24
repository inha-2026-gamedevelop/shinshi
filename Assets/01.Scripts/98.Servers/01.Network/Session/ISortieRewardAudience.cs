namespace Shinshi.Network
{
    /// <summary> 던전 호스트가 참가자에게 출정 정산 보상 내역을 보내는 창구, 서버 메인 스레드에서만 부른다 </summary>
    public interface ISortieRewardAudience
    {
        /// <summary> 연결이 없으면 보내지 않는다 </summary>
        void SendReward(int participantId, in SortieRewardMessage message);
    }
}
