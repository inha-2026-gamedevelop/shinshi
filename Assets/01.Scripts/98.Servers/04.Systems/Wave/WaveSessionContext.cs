// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.Systems
{
    /// <summary> 일반전 웨이브 진행기를 만들 때 넘기는 출정 값, 시작 인원 배율은 출정 시작 때 고정된 값이다 </summary>
    public readonly struct WaveSessionContext
    {
        /****************************************
        *              Properties
        ****************************************/

        public string       SessionId        { get; }

        // 출정 계약의 맵과 전투 정의 ID, 계약이 없는 개발 출정은 null이다
        public string       MapId            { get; }
        public string       BattleId         { get; }

        public int          StartingCount    { get; }

        // SessionConfigSO가 시작 인원으로 정한 일반 적 체력 배율과 적 공격 피해 배율
        public float        EnemyHealthScale { get; }
        public float        DamageScale      { get; }

        // 호스트가 source ID와 드롭을 등록한 같은 배치 계획
        public WaveConfigSO Waves            { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WaveSessionContext(string sessionId, string mapId, string battleId, int startingCount, float enemyHealthScale,
            float damageScale, WaveConfigSO waves)
        {
            SessionId        = sessionId;
            MapId            = mapId;
            BattleId         = battleId;
            StartingCount    = startingCount;
            EnemyHealthScale = enemyHealthScale;
            DamageScale      = damageScale;
            Waves            = waves;
        }
    }
}
