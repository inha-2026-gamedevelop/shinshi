namespace Shinshi.AI
{
    /// <summary> 숲 튜토리얼 안의 진행 순서 </summary>
    public enum EForestPhase
    {
        // 부적 자동 조사 화면, 이동과 전투 입력을 막는다
        Talisman    = 0,
        // 일반 산귀 순차 두 마리, 기본 공격과 Q 학습
        FirstFight  = 1,
        // 공격 예고 산귀 한 마리
        SecondFight = 2,
        Done        = 3
    }
}
