// System
using System;

// Third Party
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Shinshi.Data.Admin
{
    /// <summary> 감사 로그의 변경 전후 상태를 camelCase JSON 객체로 만든다. 객체가 아니면 거절한다 </summary>
    public static class AdminAuditJson
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly JsonSerializer G_Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            TypeNameHandling = TypeNameHandling.None,
            DateParseHandling = DateParseHandling.None,
            NullValueHandling = NullValueHandling.Include
        });

        /****************************************
        *                Methods
        ****************************************/

        public static string? Serialize(object? state)
        {
            if (state == null)
            {
                return null;
            }

            JToken token = state as JToken ?? JToken.FromObject(state, G_Serializer);
            if (token is not JObject)
            {
                throw new ArgumentException("Audit state must serialize to a JSON object.", nameof(state));
            }

            return token.ToString(Formatting.None);
        }
    }
}
