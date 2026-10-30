namespace Shinshi.AI
{
    /// <summary> 계정이 어떤 성장 메뉴 안내를 마쳤는지 기억하는 창구, 실제 구현은 서버 저장이고 시험은 메모리 구현으로 대신한다 </summary>
    public interface IMenuGuideRecord
    {
        bool IsCompleted(string menuId);

        /// <summary> 완료 기록을 남긴다, 같은 메뉴를 다시 기록해도 같은 결과여야 한다 </summary>
        void MarkCompleted(string menuId);
    }
}
