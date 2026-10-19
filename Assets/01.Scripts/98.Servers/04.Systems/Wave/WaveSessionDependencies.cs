// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Data;

namespace Shinshi.Systems
{
    // 팩토리가 출정마다 한 번 모아 세션에 넘긴다. Resolver는 이미 시작 인원 피해 배율을 곱한 판정기다
    /// <summary> 웨이브 세션이 쓰는 서버 협력 객체와 지역 기준값 묶음 </summary>
    public sealed class WaveSessionDependencies
    {
        /****************************************
        *              Properties
        ****************************************/

        public CombatTargetRegistry  Registry         { get; }
        public IDamageResolver       Resolver         { get; }
        public IEnemyObjectHost      Objects          { get; }
        public IWaveSpawnPointSource Spawns           { get; }
        public EnemyCatalogSO        Enemies          { get; }
        public IBossMovement         Movement         { get; }
        public IBossWallQuery        Walls            { get; }
        public IWaveKillerSource     Killers          { get; }
        public float                 ReferenceAttack  { get; }
        public float                 ReferenceHealth  { get; }
        public float                 BodyRadius       { get; }
        public float                 SeparationRadius { get; }
        public IEnemyNavigation      Navigation       { get; }
        public IBossParticipantConnection Connections { get; }

        /****************************************
        *                Methods
        ****************************************/

        // walls와 killers는 null을 허용한다(벽 보정 없음, 처치자 빈 문자열).
        // navigation이 null이면 적은 직선으로 접근하고, connections가 null이면 모든 생존자를 접속 중으로 본다
        public WaveSessionDependencies(CombatTargetRegistry registry, IDamageResolver resolver, IEnemyObjectHost objects,
            IWaveSpawnPointSource spawns, EnemyCatalogSO enemies, IBossMovement movement, IBossWallQuery walls,
            IWaveKillerSource killers, float referenceAttack, float referenceHealth, float bodyRadius, float separationRadius,
            IEnemyNavigation navigation = null, IBossParticipantConnection connections = null)
        {
            Navigation       = navigation;
            Connections      = connections;
            Registry         = registry;
            Resolver         = resolver;
            Objects          = objects;
            Spawns           = spawns;
            Enemies          = enemies;
            Movement         = movement;
            Walls            = walls;
            Killers          = killers;
            ReferenceAttack  = referenceAttack;
            ReferenceHealth  = referenceHealth;
            BodyRadius       = bodyRadius;
            SeparationRadius = separationRadius;
        }
    }
}
