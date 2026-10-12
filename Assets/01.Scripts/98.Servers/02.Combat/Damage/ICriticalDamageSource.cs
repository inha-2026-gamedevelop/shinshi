namespace Shinshi.Combat
{
    /// <summary> 서버 판정기가 타격 시점의 플레이어 또는 보존된 기술 주인 치명 확률을 읽는다 </summary>
    public interface ICriticalDamageSource
    {
        float CritChance { get; }
    }
}
