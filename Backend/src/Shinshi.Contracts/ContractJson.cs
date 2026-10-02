// System
using System.Globalization;

// Third-party
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> DataMember 기반 private 필드와 숫자 코드를 Unity 전제에 맞춰 직렬화한다 </summary>
    public static class ContractJson
    {
        /****************************************
        *                Methods
        ****************************************/

        public static JsonSerializerSettings CreateSettings()
        {
            JsonSerializerSettings settings = new JsonSerializerSettings();
            Configure(settings);
            return settings;
        }

        public static void Configure(JsonSerializerSettings settings)
        {
            settings.ContractResolver    = new DefaultContractResolver();
            settings.ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor;
            settings.TypeNameHandling    = TypeNameHandling.None;
            settings.DateParseHandling   = DateParseHandling.None;
            settings.Culture             = CultureInfo.InvariantCulture;
            settings.NullValueHandling   = NullValueHandling.Include;
            settings.Formatting          = Formatting.None;
            settings.Converters.Clear();
        }
    }
}
