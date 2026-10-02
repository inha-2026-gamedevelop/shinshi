namespace Shinshi.Combat
{
    /// <summary> 참가자 재합류 자격, 재접속 유예가 끝난 참가자를 닫고 다시 들어오지 못하게 한다 </summary>
    public interface IParticipantRoster
    {
        bool IsClosed(int participantId);

        // 유예 만료나 보존 상한 초과로 상태를 버린 참가자에게 부른다. 닫힌 참가자는 인증에서 거절한다
        void Close(int participantId);
    }
}
