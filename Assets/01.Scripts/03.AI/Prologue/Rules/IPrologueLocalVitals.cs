namespace Shinshi.AI
{
    /// <summary> 내 캐릭터의 체력이 줄었는지 읽는 창구, 물약 안내를 띄울 기준이다 </summary>
    public interface IPrologueLocalVitals
    {
        bool IsDamaged { get; }
    }
}
