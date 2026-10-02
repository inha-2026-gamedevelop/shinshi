// System
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

// Third Party
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Shinshi.Data
{
    /// <summary> 작업 종류와 JSON 본문을 함께 해시한다. 객체 키 순서는 정규화하며 배열 순서는 보존한다 </summary>
    public static class OperationRequestHasher
    {
        /****************************************
        *                Methods
        ****************************************/

        public static string ComputeHash(string operationKind, string requestJson)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(operationKind);
            ArgumentNullException.ThrowIfNull(requestJson);

            using StringReader input = new StringReader(requestJson);
            using JsonTextReader reader = new JsonTextReader(input)
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Decimal
            };
            JToken body = JToken.Load(reader, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                CommentHandling = CommentHandling.Load
            });
            if (reader.Read())
            {
                throw new JsonReaderException("Only one JSON value is permitted.");
            }

            StringBuilder normalized = new StringBuilder();
            using StringWriter output = new StringWriter(normalized, CultureInfo.InvariantCulture);
            using JsonTextWriter writer = new JsonTextWriter(output)
            {
                Formatting = Formatting.None,
                Culture = CultureInfo.InvariantCulture
            };
            writer.WriteStartObject();
            writer.WritePropertyName("kind");
            writer.WriteValue(operationKind);
            writer.WritePropertyName("body");
            WriteCanonical(writer, body);
            writer.WriteEndObject();
            writer.Flush();

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToString())));
        }

        private static void WriteCanonical(JsonWriter writer, JToken token)
        {
            if (token is JObject value)
            {
                writer.WriteStartObject();
                foreach (JProperty property in value.Properties().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                return;
            }

            if (token is JArray array)
            {
                writer.WriteStartArray();
                foreach (JToken element in array)
                {
                    WriteCanonical(writer, element);
                }
                writer.WriteEndArray();
                return;
            }

            if (token.Type == JTokenType.Float)
            {
                decimal number = token.Value<decimal>();
                writer.WriteRawValue(number.ToString("G29", CultureInfo.InvariantCulture));
                return;
            }

            if ((token.Type != JTokenType.Integer) && (token.Type != JTokenType.String)
                && (token.Type != JTokenType.Boolean) && (token.Type != JTokenType.Null))
            {
                throw new JsonReaderException("Only JSON values are permitted in an operation request.");
            }

            token.WriteTo(writer);
        }
    }
}

