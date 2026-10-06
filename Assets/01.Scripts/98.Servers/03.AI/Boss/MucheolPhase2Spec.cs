namespace Shinshi.AI
{
    // PatternDefinition에는 타별 형태와 연속 돌진 횟수가 없어 2페이즈 여섯 패턴의 패턴별 데이터를 한 묶음으로 받는다.
    // 비어 있는 항목의 패턴은 팩토리가 만들지 않는다. 수치는 호출하는 쪽(데이터)이 정하며 이 클래스에는 값이 없다
    /// <summary> 무철 2페이즈 패턴별 실행 데이터 묶음, P01 P02 P04 P05는 순차 타격이고 P03 P06은 연속 돌진이다 </summary>
    public sealed class MucheolPhase2Spec
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly MucheolSequenceSpec  _p01;
        private readonly MucheolSequenceSpec  _p02;
        private readonly MucheolMultiDashSpec _p03;
        private readonly MucheolSequenceSpec  _p04;
        private readonly MucheolSequenceSpec  _p05;
        private readonly MucheolMultiDashSpec _p06;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 연속 휘두르기, 좌우 구역 </summary>
        public MucheolSequenceSpec P01 => _p01;

        /// <summary> 충격 내려찍기, 전방 부채꼴 뒤 전방 직선 충격파 </summary>
        public MucheolSequenceSpec P02 => _p02;

        /// <summary> 연속 어깨 돌진 </summary>
        public MucheolMultiDashSpec P03 => _p03;

        /// <summary> 이중 지면 충격파, 원형 두 번 </summary>
        public MucheolSequenceSpec P04 => _p04;

        /// <summary> 폭주 연타, 좌우 구역 뒤 내려찍기 </summary>
        public MucheolSequenceSpec P05 => _p05;

        /// <summary> 폭주 돌진 </summary>
        public MucheolMultiDashSpec P06 => _p06;

        /****************************************
        *                Methods
        ****************************************/

        public MucheolPhase2Spec(MucheolSequenceSpec p01, MucheolSequenceSpec p02, MucheolMultiDashSpec p03,
            MucheolSequenceSpec p04, MucheolSequenceSpec p05, MucheolMultiDashSpec p06)
        {
            _p01 = p01;
            _p02 = p02;
            _p03 = p03;
            _p04 = p04;
            _p05 = p05;
            _p06 = p06;
        }
    }
}
