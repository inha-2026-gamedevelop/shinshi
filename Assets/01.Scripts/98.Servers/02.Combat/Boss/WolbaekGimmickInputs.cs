// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Combat
{
    // 값을 담기만 하고 검증하지 않는다. 빈 값은 WolbaekGimmickFactory.TryCreate가 걸러 이유와 함께 거절한다
    /// <summary> 월백 체력 기믹 묶음을 만드는 데 필요한 입력 </summary>
    public sealed class WolbaekGimmickInputs
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 기믹을 붙일 보스 몸체, 전투를 열기 전이어야 한다 </summary>
        public BossServer Boss { get; }

        public EBossDifficulty Difficulty { get; }

        /// <summary> 지역 기준 체력(Href), 보스 패턴과 같은 값이다 </summary>
        public float ReferenceHealth { get; }

        /// <summary> 보스 패턴과 같은 시작 인원 피해 배율 적용 판정기, 부적진이 쓴다 </summary>
        public IDamageResolver ScaledResolver { get; }

        /// <summary> 배율을 곱하기 전의 판정기, 최대 체력 비율 피해를 내는 색 결계와 봉인과 최후의 영역이 쓴다 </summary>
        public IDamageResolver UnscaledResolver { get; }

        public CombatTargetRegistry Registry { get; }

        public IBossRandom Random { get; }

        /// <summary> 전투 런타임이 가진 참가자 목록 그대로다, 복사하지 않는다 </summary>
        public List<PlayerServer> Players { get; }

        public ParticipantRoster Roster { get; }

        /// <summary> 남은 공용 부활 횟수를 돌려준다 </summary>
        public Func<int> RemainingRevives { get; }

        /// <summary> 보스 정의의 2페이즈 전환 체력 비율, 부적진 반격 중 보스가 더 내려가지 못하는 다음 경계다 </summary>
        public float PhaseTransitionHealthRate { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekGimmickInputs(BossServer boss, EBossDifficulty difficulty, float referenceHealth,
            IDamageResolver scaledResolver, IDamageResolver unscaledResolver, CombatTargetRegistry registry,
            IBossRandom random, List<PlayerServer> players, ParticipantRoster roster, Func<int> remainingRevives,
            float phaseTransitionHealthRate)
        {
            Boss                      = boss;
            Difficulty                = difficulty;
            ReferenceHealth           = referenceHealth;
            ScaledResolver            = scaledResolver;
            UnscaledResolver          = unscaledResolver;
            Registry                  = registry;
            Random                    = random;
            Players                   = players;
            Roster                    = roster;
            RemainingRevives          = remainingRevives;
            PhaseTransitionHealthRate = phaseTransitionHealthRate;
        }
    }
}
