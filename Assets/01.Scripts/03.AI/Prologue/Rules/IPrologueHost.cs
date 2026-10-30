namespace Shinshi.AI
{
    /// <summary> 프롤로그 진행기가 서버 세션에 시키는 일, 실제 구현은 서버 세션이 맡고 시험은 가짜로 대신한다 </summary>
    public interface IPrologueHost
    {
        void SpawnEnemy(EPrologueEnemyKind kind, int spawnIndex);

        // 임시 물약 재고를 이 수량으로 맞춘다, 0이면 제거다
        void SetPotions(int count);

        // 일반 전투 체력 0, 제자리에서 최대 체력으로 회복시키고 무적을 준다
        void ReviveToFull(float invulnerableSeconds);

        // 월백 교전이 끝났다, 어느 쪽 이유든 마무리로 이어진다
        void EndEngagement(EWolbaekEnd reason);

        // 단계가 바뀌었다, 저장할 값은 단계와 월백 진입 당시 물약 수량이다
        void ReportStage(EPrologueStage stage, int wolbaekEntryPotions);
    }
}
