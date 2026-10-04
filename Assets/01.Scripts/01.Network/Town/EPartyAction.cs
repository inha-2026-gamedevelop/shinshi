namespace Shinshi.Network
{
    /// <summary> 마을 파티 요청 종류, PartyRequestMessage와 결과 메시지가 함께 쓴다. 값은 직렬화되므로 바꾸지 않는다 </summary>
    public enum EPartyAction
    {
        ListRooms     = 0,
        CreateRoom    = 1,
        JoinRoom      = 2,
        LeaveRoom     = 3,
        SetReady      = 4,
        Invite        = 5,
        AcceptInvite  = 6,
        DeclineInvite = 7,
        StartSortie   = 8,
        CancelSortie  = 9
    }
}
