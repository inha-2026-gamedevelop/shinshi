// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Combat
{
    // 기믹을 만드는 쪽(보스별 팩토리)과 붙이는 쪽(BossServer.AttachGimmicks)을 나누는 값 묶음이다. 기믹 하나가 상한, 사망 보류,
    // 최종 성공을 함께 구현하는 일이 많지만 몸체는 구체 타입을 모르므로 역할별 창구로 따로 받는다.
    // 일부만 담아 붙이면 기믹 사이 인계가 끊겨 교착되므로, 만드는 쪽은 전부 만들었을 때만 이 묶음을 만든다
    /// <summary> 보스 하나에 붙이는 체력 기믹 묶음, 등록 순서와 역할별 창구를 담는다 </summary>
    public sealed class BossGimmickSet
    {
        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 두뇌에 더하는 순서대로의 기믹, 앞선 기믹이 먼저 Tick을 받아야 같은 틱에 인계가 이어진다 </summary>
        public IReadOnlyList<IBossGimmick> Gimmicks { get; }

        /// <summary> 모든 기믹의 상한을 합친 창구, 없으면 null이다 </summary>
        public IBossDamageCap DamageCap { get; }

        public IReadOnlyList<IBossDeathHold> DeathHolds { get; }

        /// <summary> 최종 성공 창구, 없으면 null이다 </summary>
        public IBossFinalSuccess FinalSuccess { get; }

        /// <summary> 정지와 실패 조회 창구, 없으면 null이다 </summary>
        public IBossGimmickStopSource StopSource { get; }

        /// <summary> 부활 창구, 없으면 null이다 </summary>
        public IBossGimmickReviveGate ReviveGate { get; }

        /// <summary> 참이면 50% 전환 뒤 결계 전개를 쓰지 않는다, 기믹이 그 구간을 대신한다 </summary>
        public bool ReplacesBarrier { get; }

        /****************************************
        *                Methods
        ****************************************/

        public BossGimmickSet(IReadOnlyList<IBossGimmick> gimmicks, IBossDamageCap damageCap,
            IReadOnlyList<IBossDeathHold> deathHolds, IBossFinalSuccess finalSuccess, IBossGimmickStopSource stopSource,
            IBossGimmickReviveGate reviveGate, bool replacesBarrier)
        {
            Gimmicks        = gimmicks ?? throw new ArgumentNullException(nameof(gimmicks));
            DamageCap       = damageCap;
            DeathHolds      = deathHolds ?? throw new ArgumentNullException(nameof(deathHolds));
            FinalSuccess    = finalSuccess;
            StopSource      = stopSource;
            ReviveGate      = reviveGate;
            ReplacesBarrier = replacesBarrier;
        }
    }
}
