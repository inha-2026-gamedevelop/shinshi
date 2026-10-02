namespace Shinshi.Combat
{
    /// <summary> 기술 키를 눌렀을 때 소유 클라이언트가 할 일 </summary>
    public enum ESkillInputDecision : byte
    {
        /****************************************
        *                Fields
        ****************************************/

        // 준비되지 않았거나 이미 같은 기술을 지정 중이다
        Ignore,
        // 방향, 자기 위치 기술이라 바로 요청한다. 진행 중인 지점 지정은 끝낸다
        CastNow,
        // 지점 기술이라 범위를 띄우고 클릭 확정을 기다린다. 다른 지점 지정 중이면 이 기술로 바꾼다
        BeginTargeting
    }
}
