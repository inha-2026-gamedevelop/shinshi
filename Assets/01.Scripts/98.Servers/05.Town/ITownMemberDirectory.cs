namespace Shinshi.Network
{
    /// <summary> 마을 접속자를 찾는 창구, 직접 초대가 캐릭터 이름으로 대상을 찾는다 </summary>
    public interface ITownMemberDirectory
    {
        bool TryFindByName(string characterName, out TownMember member);
    }
}
