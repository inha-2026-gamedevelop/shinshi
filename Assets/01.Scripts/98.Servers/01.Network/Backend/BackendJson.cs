// System
using System;
using System.Globalization;

// 서드파티
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> Contracts DTO의 JSON 직렬화, 백엔드와 같은 Newtonsoft 설정을 쓴다 </summary>
    public static class BackendJson
    {
        // DTO는 DataContract와 private 필드라 기본 해석기가 DataMember 이름으로 읽고 쓴다. 이 설정은 백엔드 ContractJson과 맞춘다.
        // 타입 이름 처리는 끈다. 응답 본문이 만들 타입을 고르게 두면 원격 코드 실행 경로가 된다.
        // 설정과 해석기는 스레드 사이에서 공유해도 안전하고 해석 결과를 캐시하므로 하나만 만든다

        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_DEPTH = 32;

        private static readonly JsonSerializerSettings _settings = CreateSettings();

        /****************************************
        *                Methods
        ****************************************/

        public static string Serialize(object value)
        {
            return JsonConvert.SerializeObject(value, _settings);
        }

        public static object Deserialize(string json, Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            return JsonConvert.DeserializeObject(json, type, _settings);
        }

        public static T Deserialize<T>(string json)
        {
            return (T)Deserialize(json, typeof(T));
        }

        /// <summary> 본문이 비었거나 계약과 맞지 않으면 거짓, 예외를 밖으로 내지 않는다 </summary>
        public static bool TryDeserialize(string json, Type type, out object value)
        {
            value = null;
            if ((string.IsNullOrEmpty(json)) || (type == null))
            {
                return false;
            }

            try
            {
                value = Deserialize(json, type);
            }
            catch (JsonException)
            {
                return false;
            }

            return value != null;
        }

        /// <summary> 거절 본문에서 사유 코드만 꺼낸다, 읽지 못하면 0 </summary>
        public static int ReadReasonCode(string json)
        {
            if (!TryDeserialize(json, typeof(ApiRejection), out object value))
            {
                return 0;
            }

            return ((ApiRejection)value).ReasonCode;
        }

        private static JsonSerializerSettings CreateSettings()
        {
            var settings = new JsonSerializerSettings();
            settings.ContractResolver    = new DefaultContractResolver();
            settings.ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor;
            settings.TypeNameHandling    = TypeNameHandling.None;
            settings.DateParseHandling   = DateParseHandling.None;
            settings.Culture             = CultureInfo.InvariantCulture;
            settings.NullValueHandling   = NullValueHandling.Include;
            settings.Formatting          = Formatting.None;
            settings.MaxDepth            = MAX_DEPTH;
            return settings;
        }
    }
}
