namespace Shinshi.Launcher.Presentation
{
    /// <summary> 소식 카드 하나. 썸네일 색은 화면이 분류에 따라 고른다 </summary>
    public sealed class NewsItem
    {
        /****************************************
        *              Properties
        ****************************************/

        public ENewsCategory Category     { get; }
        public string        Title        { get; }
        public string        Date         { get; }
        public string        CategoryText => ToCategoryText(Category);

        /****************************************
        *                Methods
        ****************************************/

        public NewsItem(ENewsCategory category, string title, string date)
        {
            Category = category;
            Title    = title;
            Date     = date;
        }

        private static string ToCategoryText(ENewsCategory category)
        {
            switch (category)
            {
                case ENewsCategory.Notice:
                    return "공지";
                case ENewsCategory.Update:
                    return "업데이트";
                case ENewsCategory.Event:
                    return "이벤트";
                default:
                    return string.Empty;
            }
        }
    }
}
