namespace Shinshi.AI
{
    /// <summary> 일반 적 효과 네트워크 개체가 그리는 표현 종류 </summary>
    public enum EEnemyEffectKind : byte
    {
        // 비어 있는 풀 개체, 아무것도 그리지 않는다
        None         = 0,
        // 적이 나타날 자리를 미리 보여 주는 바닥 표시
        SpawnPreview = 1,
        // 원거리 적이 쏜 투사체 한 발
        Projectile   = 2
    }
}
