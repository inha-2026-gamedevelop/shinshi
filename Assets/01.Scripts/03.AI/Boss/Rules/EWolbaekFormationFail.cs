namespace Shinshi.AI
{
    /// <summary> 부적진 기믹이 실패한 이유, 보스전을 끝내는 일은 알림을 받은 세션이 한다 </summary>
    public enum EWolbaekFormationFail
    {
        // 필수 참가자가 모두 쓰러졌다
        Wipe,

        // 필수 참가자 중 부활할 수 없거나 명단에서 사라진 사람이 있어 자리를 채울 수 없다
        Unrecoverable
    }
}
