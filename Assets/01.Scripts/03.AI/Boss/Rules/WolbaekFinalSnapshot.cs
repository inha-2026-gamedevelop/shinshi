// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 늦게 접속하거나 재접속한 클라이언트가 지금 단계를 복원하는 데 쓴다. 복제 계층이 어떻게 전달할지는 이 값이 정하지 않는다
    /// <summary> 최후의 영역의 현재 진행 값, 진행 중일 때만 만들어진다 </summary>
    public readonly struct WolbaekFinalSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public EWolbaekFinalStep Step { get; }

        public WolbaekFinalStart Start { get; }

        /// <summary> 외곽 바닥이 이미 사라졌다 </summary>
        public bool OuterFloorRemoved { get; }

        /// <summary> 노란 여우불, 바닥 제거 전에는 비어 있다 </summary>
        public IReadOnlyList<WolbaekFinalYellow> Yellows { get; }

        /// <summary> 노란 여우불별 남은 체력, Yellows와 같은 순서다 </summary>
        public IReadOnlyList<float> YellowHealth { get; }

        /// <summary> 지금 묶음이 있다 </summary>
        public bool HasBundle { get; }

        public WolbaekFinalBundle Bundle { get; }

        /// <summary> 빨간 여우불 낙하 중심, 묶음의 여우불 순서와 같다. 추적 중이면 지금 위치이고 고정된 뒤에는 고정 위치다 </summary>
        public IReadOnlyList<Vector3> RedPositions { get; }

        /// <summary> 대상을 놓쳐 마지막 위치에 머문 빨간 여우불 </summary>
        public IReadOnlyList<bool> RedDetached { get; }

        /// <summary> 빨간 여우불 낙하 위치가 고정됐다 </summary>
        public bool RedsLocked { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekFinalSnapshot(EWolbaekFinalStep step, in WolbaekFinalStart start, bool outerFloorRemoved,
            IReadOnlyList<WolbaekFinalYellow> yellows, IReadOnlyList<float> yellowHealth, bool hasBundle,
            in WolbaekFinalBundle bundle, IReadOnlyList<Vector3> redPositions, IReadOnlyList<bool> redDetached, bool redsLocked)
        {
            Step              = step;
            Start             = start;
            OuterFloorRemoved = outerFloorRemoved;
            Yellows           = yellows;
            YellowHealth      = yellowHealth;
            HasBundle         = hasBundle;
            Bundle            = bundle;
            RedPositions      = redPositions;
            RedDetached       = redDetached;
            RedsLocked        = redsLocked;
        }
    }
}
