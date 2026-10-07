namespace Shinshi.Admin.Web.Models
{
    /// <summary> 직업 ID(0/1/2)의 표시 이름·색 클래스. 직업 이름이 확정되면 이 표만 바꾼다 </summary>
    public static class AdminClassCatalog
    {
        /****************************************
        *                Methods
        ****************************************/

        public static string Name(int classId) => classId switch
        {
            0 => "무사", 1 => "도술사", 2 => "신령사", _ => "알 수 없는 직업"
        };

        /// <summary> admin.css의 직업 색 클래스 이름 </summary>
        public static string CssClass(int classId) => classId switch
        {
            0 => "class-0", 1 => "class-1", 2 => "class-2", _ => "class-x"
        };
    }
}
