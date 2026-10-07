// Third Party
using Newtonsoft.Json.Linq;

namespace Shinshi.Admin.Web.Models
{
    /// <summary> API 응답 JToken에서 값을 읽는 화면용 보조 </summary>
    public static class AdminJson
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 정수 필드를 long으로 읽는다. 없거나 null이면 null </summary>
        public static long? Ms(JToken? token, string key)
        {
            JToken? value = token?[key];
            return value?.Type == JTokenType.Integer ? value.Value<long>() : null;
        }
    }
}
