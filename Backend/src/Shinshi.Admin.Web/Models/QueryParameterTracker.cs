namespace Shinshi.Admin.Web.Models
{
    /// <summary> 다른 화면의 링크가 넘긴 UUID query 값을 한 번씩만 받는다. 값이 없는 상태도 기억해 같은 링크로 다시 들어오면 받는다 </summary>
    public sealed class QueryParameterTracker
    {
        /****************************************
        *                Fields
        ****************************************/

        private string _last = string.Empty;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> query 값이 지난번과 달라졌고 올바른 UUID이면 true. 빈 값·잘못된 값으로 바뀐 것도 기억한다 </summary>
        public bool TryTake(string? raw, out string uuid)
        {
            string current = raw ?? string.Empty;
            uuid = string.Empty;
            if (current == _last)
            {
                return false;
            }
            _last = current;
            return TargetInput.IsUuid(current, out uuid);
        }
    }
}
