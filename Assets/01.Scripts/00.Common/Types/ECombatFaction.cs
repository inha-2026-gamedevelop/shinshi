namespace Shinshi.Common
{
    /// <summary> 전투 대상의 소속, 소환물은 주인의 소속을 따르고 소환물 여부는 따로 표시한다 </summary>
    public enum ECombatFaction
    {
        // 참가 플레이어와 그 소환물
        Player = 0,
        // 산귀와 보스
        Enemy  = 1
    }
}
