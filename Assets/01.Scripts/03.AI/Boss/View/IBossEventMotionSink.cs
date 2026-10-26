namespace Shinshi.AI
{
    // 서버 사건 종류로 모션을 정하는 패턴을 시험 연결부도 같은 진입점으로 부르도록 BossView의 두 호출만 떼어 낸다
    /// <summary> 사건으로 정하는 보스 몸 모션의 시작과 종료를 받는 쪽 </summary>
    public interface IBossEventMotionSink
    {
        /// <summary> 첫 판정 시각에 맞춰 모션을 시작하도록 계획한다. 같은 시전 번호는 다시 계획하지 않는다 </summary>
        void BeginEventMotion(int castId, EWolbaekEventMotion kind, double castStartServerTime, float strikeSeconds);

        /// <summary> 그 시전의 모션이 끝났거나 취소됐다 </summary>
        void EndEventMotion(int castId, bool isCancelled);
    }
}
