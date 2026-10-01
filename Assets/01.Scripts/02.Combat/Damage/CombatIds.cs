namespace Shinshi.Combat
{
    /// <summary> 전투 대상 번호와 시전, 동작 번호의 공통 규약 </summary>
    public static class CombatIds
    {
        /****************************************
        *                Fields
        ****************************************/

        // 발급 번호는 1부터 쓰고 0은 없음을 뜻한다. default로 만든 요청이 유효한 번호를 가진 것처럼 읽히지 않게 한다
        // TODO(민성): 공통 계약값이 되면 Constants.Combat으로 옮긴다
        public const int NONE = 0;
    }
}
