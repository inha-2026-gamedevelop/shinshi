namespace Shinshi.Network
{
    /// <summary> 던전 호스트가 참가자에게 일반전 웨이브 진행을 보내는 창구, 서버 메인 스레드에서만 부른다 </summary>
    public interface IWaveProgressAudience
    {
        /// <summary> 연결이 없으면 보내지 않는다 </summary>
        void SendWaveProgress(int participantId, in WaveProgressMessage message);
    }
}
