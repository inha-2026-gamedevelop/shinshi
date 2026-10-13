namespace Shinshi.AI
{
    // 늦게 접속하거나 재접속한 클라이언트가 지금 단계를 복원하는 데 쓴다. 복제 계층이 어떻게 전달할지는 이 값이 정하지 않는다
    /// <summary> 부적진 기믹의 현재 진행 값, 진행 중인 단계에서만 만들어진다 </summary>
    public readonly struct WolbaekTalismanSnapshot
    {
        /****************************************
        *              Properties
        ****************************************/

        public EWolbaekTalismanState State { get; }

        public WolbaekTalismanStart Start { get; }

        /// <summary> 필수 참가자 전원이 연속으로 자리를 지키는 중이다 </summary>
        public bool HasHold { get; }

        /// <summary> 연속 유지가 시작된 서버 시각, HasHold가 거짓이면 의미가 없다 </summary>
        public double HoldStartServerTime { get; }

        /// <summary> 예고를 시작했고 아직 판정이 나지 않은 공격이 있다 </summary>
        public bool HasAttack { get; }

        public WolbaekTalismanAttack Attack { get; }

        /// <summary> 반격 시간이 끝나는 서버 시각, 반격 단계가 아니면 0이다 </summary>
        public double CounterEndServerTime { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekTalismanSnapshot(EWolbaekTalismanState state, in WolbaekTalismanStart start, bool hasHold,
            double holdStartServerTime, bool hasAttack, in WolbaekTalismanAttack attack, double counterEndServerTime)
        {
            State                = state;
            Start                = start;
            HasHold              = hasHold;
            HoldStartServerTime  = holdStartServerTime;
            HasAttack            = hasAttack;
            Attack               = attack;
            CounterEndServerTime = counterEndServerTime;
        }
    }
}
