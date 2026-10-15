// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 늦게 접속하거나 재접속한 클라이언트가 지금 단계를 복원하는 데 쓴다. 복제 계층이 어떻게 전달할지는 이 값이 정하지 않는다
    /// <summary> 여우불 유도와 봉인 핵 기믹의 현재 진행 값, 진행 중과 반격 중에만 만들어진다 </summary>
    public readonly struct WolbaekSealSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public EWolbaekSealState State { get; }

        public EWolbaekSealStep Step { get; }

        public WolbaekSealStart Start { get; }

        /// <summary> 지금 차례, 반격 중에는 의미가 없다 </summary>
        public WolbaekSealTurn Turn { get; }

        /// <summary> 지금까지 누적한 성공 횟수 </summary>
        public int SuccessCount { get; }

        /// <summary> 여우불 낙하 중심, 차례의 여우불 순서와 같다. 추적 중이면 지금 위치이고 고정된 뒤에는 고정 위치다 </summary>
        public IReadOnlyList<Vector3> FoxfirePositions { get; }

        /// <summary> 대상을 놓쳐 마지막 위치에 머문 여우불, 여우불 순서와 같다 </summary>
        public IReadOnlyList<bool> FoxfireDetached { get; }

        /// <summary> 낙하 위치가 고정됐다 </summary>
        public bool FoxfiresLocked { get; }

        /// <summary> 예고했지만 아직 판정되지 않은 휩쓸기가 있다 </summary>
        public bool HasPendingSweep { get; }

        public WolbaekSealSweep PendingSweep { get; }

        /// <summary> 반격이 끝나는 서버 시각, 반격 중이 아니면 0이다 </summary>
        public double CounterEndServerTime { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekSealSnapshot(EWolbaekSealState state, EWolbaekSealStep step, in WolbaekSealStart start,
            in WolbaekSealTurn turn, int successCount, IReadOnlyList<Vector3> foxfirePositions,
            IReadOnlyList<bool> foxfireDetached, bool foxfiresLocked, bool hasPendingSweep, in WolbaekSealSweep pendingSweep,
            double counterEndServerTime)
        {
            State                = state;
            Step                 = step;
            Start                = start;
            Turn                 = turn;
            SuccessCount         = successCount;
            FoxfirePositions     = foxfirePositions;
            FoxfireDetached      = foxfireDetached;
            FoxfiresLocked       = foxfiresLocked;
            HasPendingSweep      = hasPendingSweep;
            PendingSweep         = pendingSweep;
            CounterEndServerTime = counterEndServerTime;
        }
    }
}
