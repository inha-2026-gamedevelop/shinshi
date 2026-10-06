// System
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

// Third-party
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Shinshi.Backend.Observability
{
    // 배포 로그 규약의 공통 키를 모든 줄에 같은 순서로 쓴다. 없는 상관 ID는 빈 문자열이나 새 ID가 아니라 null이다.
    // 프레임워크(Microsoft.*) 로그는 메시지 값에 URL, query, 헤더가 섞일 수 있어 값을 버리고 메시지 틀만 남긴다.
    // 예외는 메시지에 연결 문자열 같은 비밀값이 섞일 수 있어 형식 이름만 남긴다
    /// <summary> api 서비스의 한 줄 JSON 콘솔 로그. Logging__Console__FormatterName=shinshi-json으로 켠다 </summary>
    public sealed class ShinshiJsonConsoleFormatter : ConsoleFormatter, IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string FORMATTER_NAME = "shinshi-json";
        public const string SERVICE_NAME   = "api";

        private const string TIMESTAMP_FORMAT  = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
        private const string PROJECT_PREFIX    = "Shinshi.";
        private const string ORIGINAL_FORMAT   = "{OriginalFormat}";

        // 공통 키로 올리는 메시지 값 이름. 대소문자를 가리지 않는다
        private const string KEY_EVENT          = "event";
        private const string KEY_SESSION        = "sessionId";
        private const string KEY_PARTICIPANT    = "participantId";
        private const string KEY_OPERATION      = "operationId";
        private const string KEY_EVENT_ID       = "eventId";
        private const string KEY_REASON         = "reasonCode";
        private const string KEY_BUILD          = "buildId";
        private const string KEY_CONTENT        = "contentVersion";

        private static readonly JsonWriterOptions G_WriterOptions = new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = false
        };

        private readonly IDisposable? _reload;
        private readonly TimeProvider _timeProvider;
        private ShinshiJsonFormatterOptions _options;

        /****************************************
        *                Methods
        ****************************************/

        public ShinshiJsonConsoleFormatter(IOptionsMonitor<ShinshiJsonFormatterOptions> options, TimeProvider timeProvider)
            : base(FORMATTER_NAME)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(timeProvider);
            _options      = options.CurrentValue;
            _reload       = options.OnChange(updated => _options = updated);
            _timeProvider = timeProvider;
        }

        public void Dispose()
        {
            _reload?.Dispose();
        }

        public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider,
            TextWriter textWriter)
        {
            ArgumentNullException.ThrowIfNull(textWriter);
            string? message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
            if ((logEntry.Exception == null) && (message == null))
            {
                return;
            }

            bool project = logEntry.Category.StartsWith(PROJECT_PREFIX, StringComparison.Ordinal);
            Fields fields = new Fields(_options);
            List<KeyValuePair<string, object?>> data = new List<KeyValuePair<string, object?>>();
            string? template = null;
            if (logEntry.State is IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                foreach (KeyValuePair<string, object?> value in values)
                {
                    if (string.Equals(value.Key, ORIGINAL_FORMAT, StringComparison.Ordinal))
                    {
                        template = value.Value as string;
                        continue;
                    }
                    if (project && !fields.TryTake(value.Key, value.Value))
                    {
                        data.Add(new KeyValuePair<string, object?>(ToCamelCase(value.Key), value.Value));
                    }
                }
            }

            if (project)
            {
                data.Add(new KeyValuePair<string, object?>("message", message));
            }
            else if (template != null)
            {
                data.Add(new KeyValuePair<string, object?>("template", template));
            }
            if (logEntry.Exception != null)
            {
                data.Add(new KeyValuePair<string, object?>("exceptionType", logEntry.Exception.GetType().FullName));
            }

            string eventName = fields.Event ?? (string.IsNullOrEmpty(logEntry.EventId.Name)
                ? logEntry.Category : logEntry.EventId.Name);
            textWriter.Write(Serialize(_timeProvider.GetUtcNow(), logEntry.LogLevel, eventName, fields, data));
            textWriter.Write(Environment.NewLine);
        }

        private string Serialize(DateTimeOffset now, LogLevel level, string eventName, Fields fields,
            List<KeyValuePair<string, object?>> data)
        {
            ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer, G_WriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteString("ts", now.UtcDateTime.ToString(TIMESTAMP_FORMAT, CultureInfo.InvariantCulture));
                writer.WriteString("level", LevelName(level));
                writer.WriteString("service", SERVICE_NAME);
                WriteNullable(writer, "instanceId", _options.InstanceId);
                WriteNullable(writer, KEY_SESSION, fields.SessionId);
                WriteNullable(writer, KEY_PARTICIPANT, fields.ParticipantId);
                WriteNullable(writer, KEY_OPERATION, fields.OperationId);
                writer.WriteString(KEY_EVENT, eventName);
                WriteNullable(writer, KEY_BUILD, fields.BuildId);
                WriteNullable(writer, KEY_CONTENT, fields.ContentVersion);
                WriteNullable(writer, KEY_EVENT_ID, fields.EventId);
                writer.WritePropertyName(KEY_REASON);
                WriteValue(writer, fields.ReasonCode);
                writer.WritePropertyName("data");
                writer.WriteStartObject();
                foreach (KeyValuePair<string, object?> entry in data)
                {
                    writer.WritePropertyName(entry.Key);
                    WriteValue(writer, entry.Value);
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
        {
            if (value == null)
            {
                writer.WriteNull(name);
                return;
            }

            writer.WriteString(name, value);
        }

        private static void WriteValue(Utf8JsonWriter writer, object? value)
        {
            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    return;
                case string text:
                    writer.WriteStringValue(text);
                    return;
                case bool flag:
                    writer.WriteBooleanValue(flag);
                    return;
                case int number:
                    writer.WriteNumberValue(number);
                    return;
                case long number:
                    writer.WriteNumberValue(number);
                    return;
                case double number when double.IsFinite(number):
                    writer.WriteNumberValue(number);
                    return;
                case float number when float.IsFinite(number):
                    writer.WriteNumberValue(number);
                    return;
                case decimal number:
                    writer.WriteNumberValue(number);
                    return;
                case Guid id:
                    writer.WriteStringValue(id.ToString("D"));
                    return;
                case DateTimeOffset time:
                    writer.WriteStringValue(time.UtcDateTime.ToString(TIMESTAMP_FORMAT, CultureInfo.InvariantCulture));
                    return;
                case IEnumerable<KeyValuePair<string, object?>> nested:
                    writer.WriteStartObject();
                    foreach (KeyValuePair<string, object?> entry in nested)
                    {
                        writer.WritePropertyName(entry.Key);
                        WriteValue(writer, entry.Value);
                    }
                    writer.WriteEndObject();
                    return;
                default:
                    writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
            }
        }

        private static string LevelName(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Trace:
                    return "trace";
                case LogLevel.Debug:
                    return "debug";
                case LogLevel.Information:
                    return "info";
                case LogLevel.Warning:
                    return "warn";
                case LogLevel.Error:
                    return "error";
                default:
                    return "critical";
            }
        }

        private static string ToCamelCase(string name)
        {
            if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
            {
                return name;
            }

            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        /// <summary> 한 줄의 공통 키 값. 메시지 값이 같은 이름이면 그 값을, 아니면 설정 값이나 null을 쓴다 </summary>
        private sealed class Fields
        {
            /****************************************
            *              Properties
            ****************************************/

            public string? Event          { get; private set; }
            public string? SessionId      { get; private set; }
            public string? ParticipantId  { get; private set; }
            public string? OperationId    { get; private set; }
            public string? EventId        { get; private set; }
            public object? ReasonCode     { get; private set; }
            public string? BuildId        { get; private set; }
            public string? ContentVersion { get; private set; }

            /****************************************
            *                Methods
            ****************************************/

            public Fields(ShinshiJsonFormatterOptions options)
            {
                BuildId        = options.BuildId;
                ContentVersion = options.ContentVersion;
            }

            // 공통 키면 받아 두고 true를 돌려준다. 나머지 값은 data로 간다
            public bool TryTake(string key, object? value)
            {
                string? text = value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
                switch (key.ToLowerInvariant())
                {
                    case "event":
                        Event = text;
                        return true;
                    case "sessionid":
                        SessionId = text;
                        return true;
                    case "participantid":
                        ParticipantId = text;
                        return true;
                    case "operationid":
                        OperationId = text;
                        return true;
                    case "eventid":
                        EventId = text;
                        return true;
                    case "reasoncode":
                        ReasonCode = value;
                        return true;
                    case "buildid":
                        BuildId = text;
                        return true;
                    case "contentversion":
                        ContentVersion = text;
                        return true;
                    default:
                        return false;
                }
            }
        }
    }
}
