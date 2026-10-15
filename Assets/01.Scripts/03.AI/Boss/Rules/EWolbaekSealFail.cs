namespace Shinshi.AI
{
    /// <summary> 여우불 유도와 봉인 핵 기믹이 실패한 이유, 보스전을 끝내는 일은 알림을 받은 세션이 한다 </summary>
    public enum EWolbaekSealFail
    {
        // 파티원이 모두 쓰러졌다, 같은 시각의 마지막 성공보다 앞선다
        Wipe,

        // 전체 제한 시간 안에 필요한 성공 횟수를 채우지 못했다
        TimeExpired
    }
}
