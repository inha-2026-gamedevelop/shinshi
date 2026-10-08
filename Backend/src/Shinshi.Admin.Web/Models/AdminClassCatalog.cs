namespace Shinshi.Admin.Web.Models
{
    /// <summary> 직업 ID(0~5)의 표시 이름·색 클래스. 3~5(검사, 마검사, 궁수)는 서버 설정으로 개방할 때까지 캐릭터가 생기지 않는다 </summary>
    public static class AdminClassCatalog
    {
        /****************************************
        *                Methods
        ****************************************/

        public static string Name(int classId) => classId switch
        {
            0 => "무사", 1 => "도술사", 2 => "신령사", 3 => "검사", 4 => "마검사", 5 => "궁수", _ => "알 수 없는 직업"
        };

        /// <summary> admin.css의 직업 색 클래스 이름 </summary>
        public static string CssClass(int classId) => classId switch
        {
            0 => "class-0", 1 => "class-1", 2 => "class-2", 3 => "class-3", 4 => "class-4", 5 => "class-5", _ => "class-x"
        };
    }
}
