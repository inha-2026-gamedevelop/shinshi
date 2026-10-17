namespace Shinshi.AI
{
    /// <summary> 50% 정식 보호막을 2페이즈 전환 컷신에 넘긴 결과 </summary>
    public enum EWolbaekShieldHandover
    {
        None         = 0, // 넘길 보호막이 없었다(늦은 접속, 이미 사라짐)
        ActiveShield = 1, // 성공 사건 전에 반복 중이던 보호막을 넘겼다
        FadingShield = 2  // 성공 뒤 2.5초 소멸 중이던 보호막을 넘겼다
    }
}
