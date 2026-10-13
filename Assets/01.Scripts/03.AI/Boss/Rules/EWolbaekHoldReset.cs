namespace Shinshi.AI
{
    /// <summary> 공용 자리 유지 진행 시간이 0으로 돌아간 이유, 피격 자체는 이유가 아니다 </summary>
    public enum EWolbaekHoldReset
    {
        // 필수 참가자가 자기 구조물 범위를 벗어났다, 밀려나서 벗어난 경우도 같다
        Left,

        // 필수 참가자가 쓰러졌다
        Died,

        // 필수 참가자의 접속이 끊겼다
        Disconnected
    }
}
