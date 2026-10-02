// System
using System;
using System.Collections.Concurrent;
using System.Linq;

// Third Party
using Microsoft.Extensions.Logging;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 기록된 로그의 수준과 메시지를 모아 테스트가 경고, 오류 횟수를 셀 수 있게 하는 로거 공급자 </summary>
    internal sealed class RecordingLoggerProvider : ILoggerProvider
    {
        /****************************************
        *              Properties
        ****************************************/

        public ConcurrentQueue<(string Category, LogLevel Level, string Message)> Entries { get; }
            = new ConcurrentQueue<(string Category, LogLevel Level, string Message)>();

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이 공급자만 붙인 팩터리로 형식 이름 범주의 로거를 만든다 </summary>
        public ILogger<T> CreateLogger<T>()
        {
            return LoggerFactory.Create(builder => builder.AddProvider(this).SetMinimumLevel(LogLevel.Trace))
                .CreateLogger<T>();
        }

        public int Count(LogLevel level, string messagePart)
        {
            return Entries.Count(entry => (entry.Level == level)
                && entry.Message.Contains(messagePart, StringComparison.Ordinal));
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new RecordingLogger(this, categoryName);
        }

        // 팩터리가 함께 정리하려고 부른다. 모은 기록은 테스트가 끝까지 읽어야 하므로 비우지 않는다
        public void Dispose() { }

        private sealed class RecordingLogger : ILogger
        {
            private readonly RecordingLoggerProvider _provider;
            private readonly string _category;

            public RecordingLogger(RecordingLoggerProvider provider, string category)
            {
                _provider = provider;
                _category = category;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull { return null; }

            public bool IsEnabled(LogLevel logLevel) { return true; }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _provider.Entries.Enqueue((_category, logLevel, formatter(state, exception)));
            }
        }
    }
}
