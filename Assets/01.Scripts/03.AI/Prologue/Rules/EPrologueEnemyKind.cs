namespace Shinshi.AI
{
    /// <summary> 프롤로그 숲에서 소환을 요청하는 산귀의 종류, 실제 에셋은 서버 쪽이 정한다 </summary>
    public enum EPrologueEnemyKind
    {
        // 기본 공격 위주의 일반 산귀, 첫 전투
        Normal    = 0,
        // 공격 예고가 명확한 산귀, 두 번째 전투
        Telegraph = 1
    }
}
