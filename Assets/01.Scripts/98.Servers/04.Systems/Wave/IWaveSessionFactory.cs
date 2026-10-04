namespace Shinshi.Systems
{
    // 던전 호스트는 일반전 출정을 열 때 이 창구로 웨이브 진행기를 받는다. 구현(스폰 디렉터, 적 두뇌, 네트워크 개체)은 적 담당 파트가
    // 만들고 서버 부트스트랩이 DungeonSessionHost.WaveFactory에 넣는다. 없으면 호스트가 경고를 남기고 웨이브 없는 개발 세션으로 연다.
    //
    // 구현이 지킬 것
    // - source ID는 WaveConfigSO.GetSourceId(웨이브 순서, 웨이브 안 배치 순번)다. 배치 순번은 웨이브의 Enemies 항목을 앞에서부터
    //   개수만큼 이어 센다(SortieContentRules.TryGetEnemyId). 호스트가 같은 규칙으로 드롭 명세를 미리 등록하므로 다르게 세면 처치가 버려진다
    // - 적 체력은 context.EnemyHealthScale을 EnemyServer 생성자에, 적 공격 피해는 BossDamageScaler(context.DamageScale)를 거쳐 낸다
    // - Tick은 호스트가 전투 틱 안(플레이어 피해 뒤, 종료 판정 앞)에서 부른다. 처치 통지는 그 Tick 안에서만 한다
    /// <summary> 일반전 웨이브 진행기를 만드는 주입 창구, 출정 시작 틱에 한 번 부른다 </summary>
    public interface IWaveSessionFactory
    {
        /// <summary> 이번 출정의 웨이브 진행기, 만들 수 없으면 null(호스트가 불변식 위반으로 출정을 내린다) </summary>
        IWaveSession Create(in WaveSessionContext context);
    }
}
