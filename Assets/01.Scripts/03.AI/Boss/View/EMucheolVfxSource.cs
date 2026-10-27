namespace Shinshi.AI
{
    /// <summary> 무철 패턴 VFX가 어떤 서버 사건 묶음으로 시작하고 확정되는가 </summary>
    public enum EMucheolVfxSource
    {
        /// <summary> 기본 예고와 타격 확정(OnStruck), 1페이즈 단발 </summary>
        BasicStrike = 0,

        /// <summary> 기본 예고와 돌진 시작, 정지, 1페이즈 돌진 </summary>
        BasicDash = 1,

        /// <summary> 콤보와 순차 타격, 단계마다 타 확정 사건이 온다 </summary>
        StrikeChain = 2,

        /// <summary> 연속 돌진, 돌진마다 재조준과 시작, 정지 사건이 온다 </summary>
        MultiDash = 3,

        /// <summary> 전장 공격 </summary>
        Battlefield = 4,

        /// <summary> 팔 휘두르기, 기본 예고와 타격 확정(OnStruck)으로 시작하고 확정되지만 땅 임팩트 대신 빨간 예고와 팔 궤적을 그린다 </summary>
        ArmSwing = 5,

        /// <summary> 팔을 휘두르는 연타(전 단계 부채꼴), 순차 타격 사건으로 시작하고 단계마다 확정되지만 땅 임팩트 대신 단계별 빨간 예고와 양손 궤적을 그린다 </summary>
        ArmChain = 6,
    }
}
