// System
using System.Collections.Generic;

namespace Shinshi.AI
{
    // 늦게 접속하거나 재접속한 클라이언트가 지금 단계를 복원하는 데 쓴다. 복제 계층이 어떻게 전달할지는 이 값이 정하지 않는다
    /// <summary> 색 결계 기믹의 현재 진행 값, 진행 중인 단계에서만 만들어진다 </summary>
    public readonly struct WolbaekWardSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public EWolbaekWardState State { get; }

        public WolbaekWardStart Start { get; }

        /// <summary> 필수 참가자 전원이 중앙 결계에 연속으로 모이는 중이다, 집결 단계에서만 의미가 있다 </summary>
        public bool HasHold { get; }

        /// <summary> 연속 집결이 시작된 서버 시각, HasHold가 거짓이면 의미가 없다 </summary>
        public double HoldStartServerTime { get; }

        /// <summary> 지금 보이는 지점, 탐색과 광역 공격 단계에서만 의미가 있다 </summary>
        public WolbaekWardStage Stage { get; }

        /// <summary> 다음에 판정될 광역 공격 번호 </summary>
        public int NextStrikeIndex { get; }

        /// <summary> 명단 순서와 같은 합류 유예 끝 시각, 유예 중이 아니면 0이다 </summary>
        public IReadOnlyList<double> GraceEndServerTimes { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekWardSnapshot(EWolbaekWardState state, in WolbaekWardStart start, bool hasHold, double holdStartServerTime,
            in WolbaekWardStage stage, int nextStrikeIndex, IReadOnlyList<double> graceEndServerTimes)
        {
            State               = state;
            Start               = start;
            HasHold             = hasHold;
            HoldStartServerTime = holdStartServerTime;
            Stage               = stage;
            NextStrikeIndex     = nextStrikeIndex;
            GraceEndServerTimes = graceEndServerTimes;
        }
    }
}
