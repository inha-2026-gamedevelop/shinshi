// System
using System;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.AI
{
    /// <summary> 보스 두뇌가 쓰는 외부 연결 묶음, 구현체는 서버 부트스트랩이나 테스트가 넣는다 </summary>
    public sealed class BossBrainDependencies
    {
        /****************************************
        *              Properties
        ****************************************/

        public IBossPatternCatalog  Catalog     { get; set; }

        /// <summary> 보스 자신, 위치와 정면을 읽는 용도다 </summary>
        public ICombatTarget        Self        { get; set; }

        public IBossVitals          Vitals      { get; set; }
        public ICombatTargetQuery   Targets     { get; set; }
        public IDamageResolver      Damage      { get; set; }
        public IBossPatternFactory  Patterns    { get; set; }
        public IBossEffectLifetime  Effects     { get; set; }
        public IBossControlSource   Control     { get; set; }
        public IBossRandom          Random      { get; set; }

        /// <summary> 비워 두면 체력 0에서 바로 사망을 확정하는 임시 기본 정책을 쓴다 </summary>
        public IBossDeathPolicy     DeathPolicy { get; set; }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 필수 연결이 비었으면 예외를 던진다, 비어 있는 채로 조용히 진행하지 않는다 </summary>
        public void Validate()
        {
            RequireNotNull(Catalog, nameof(Catalog));
            RequireNotNull(Self, nameof(Self));
            RequireNotNull(Vitals, nameof(Vitals));
            RequireNotNull(Targets, nameof(Targets));
            RequireNotNull(Damage, nameof(Damage));
            RequireNotNull(Patterns, nameof(Patterns));
            RequireNotNull(Effects, nameof(Effects));
            RequireNotNull(Control, nameof(Control));
            RequireNotNull(Random, nameof(Random));

            if (DeathPolicy == null)
            {
                DeathPolicy = new ImmediateBossDeathPolicy();
            }
        }

        private static void RequireNotNull(object value, string name)
        {
            if (value == null)
            {
                throw new InvalidOperationException($"BossBrainDependencies.{name}이 비어 있다");
            }
        }
    }
}
