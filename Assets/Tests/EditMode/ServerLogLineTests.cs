// System
using System;
using System.Text;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class ServerLogLineTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SERVICE         = "dungeon";
        private const string INSTANCE_ID     = "dungeon-01";
        private const string CONTENT_VERSION = "cv-1";
        private const string BUILD_ID        = "build-1";
        private const int    REASON_CODE     = 804;

        private static readonly DateTime G_Time = new DateTime(2026, 10, 1, 0, 0, 0, 5, DateTimeKind.Utc);

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Format_MissingValues_WritesAllKeysWithNull()
        {
            var line = new ServerLogLine(SERVICE, INSTANCE_ID, null);

            string text = line.Format(G_Time, ServerLogLine.LEVEL_INFO, ServerLogLine.EVENT_SERVER_STARTED, null, string.Empty,
                null, null, ServerLogLine.NO_REASON, null);

            Assert.AreEqual("{\"ts\":\"2026-10-01T00:00:00.005Z\",\"level\":\"info\",\"service\":\"dungeon\"," +
                "\"instanceId\":\"dungeon-01\",\"sessionId\":null,\"participantId\":null,\"operationId\":null," +
                "\"event\":\"server.started\",\"buildId\":null,\"contentVersion\":null,\"eventId\":null," +
                "\"reasonCode\":null,\"data\":null}", text);
        }

        [Test]
        public void Format_AllValues_WritesValuesInOrder()
        {
            var line = new ServerLogLine(SERVICE, INSTANCE_ID, CONTENT_VERSION);
            line.BuildId = BUILD_ID;

            string text = line.Format(G_Time, ServerLogLine.LEVEL_WARN, ServerLogLine.EVENT_DRAIN_REQUESTED, "s-1", "p-2", "op-3",
                "ev-4", REASON_CODE, "{\"exitCode\":0}");

            Assert.AreEqual("{\"ts\":\"2026-10-01T00:00:00.005Z\",\"level\":\"warn\",\"service\":\"dungeon\"," +
                "\"instanceId\":\"dungeon-01\",\"sessionId\":\"s-1\",\"participantId\":\"p-2\",\"operationId\":\"op-3\"," +
                "\"event\":\"server.drain.requested\",\"buildId\":\"build-1\",\"contentVersion\":\"cv-1\",\"eventId\":\"ev-4\"," +
                "\"reasonCode\":804,\"data\":{\"exitCode\":0}}", text);
        }

        [Test]
        public void Format_IsSingleLine()
        {
            var line = new ServerLogLine(SERVICE, INSTANCE_ID, CONTENT_VERSION);

            string text = line.Format(G_Time, ServerLogLine.LEVEL_INFO, ServerLogLine.EVENT_UNITY_LOG, null, null, null, null,
                ServerLogLine.NO_REASON, ServerLogLine.FormatMessageData("a\nb\rc\u2028d"));

            Assert.AreEqual(-1, text.IndexOf('\n'));
            Assert.AreEqual(-1, text.IndexOf('\r'));
            Assert.AreEqual(-1, text.IndexOf('\u2028'));
        }

        [Test]
        public void AppendJsonString_EscapesQuotesBackslashAndControls()
        {
            var builder = new StringBuilder();

            ServerLogLine.AppendJsonString(builder, "q\"b\\n\nt\tc\u0001");

            Assert.AreEqual("\"q\\\"b\\\\n\\nt\\tc\\u0001\"", builder.ToString());
        }

        [Test]
        public void AppendJsonString_Null_WritesNullLiteral()
        {
            var builder = new StringBuilder();

            ServerLogLine.AppendJsonString(builder, null);

            Assert.AreEqual("null", builder.ToString());
        }

        [Test]
        public void AppendJsonString_KoreanAndSurrogatePair_KeepsCharacters()
        {
            var builder = new StringBuilder();

            ServerLogLine.AppendJsonString(builder, "서버\U0001F600");

            Assert.AreEqual("\"서버\U0001F600\"", builder.ToString());
        }

        [Test]
        public void AppendJsonString_LoneSurrogate_IsEscaped()
        {
            var builder = new StringBuilder();

            ServerLogLine.AppendJsonString(builder, "a\uD800b");

            Assert.AreEqual("\"a\\ud800b\"", builder.ToString());
        }

        [Test]
        public void FormatMessageData_BearerToken_IsRedacted()
        {
            string data = ServerLogLine.FormatMessageData("header Authorization: Bearer secret-token-value end");

            Assert.AreEqual(-1, data.IndexOf("secret-token-value", StringComparison.Ordinal));
            StringAssert.Contains("Bearer [REDACTED] end", data);
        }

        [Test]
        public void RedactBearer_NoBearer_ReturnsSameText()
        {
            Assert.AreEqual("plain message", ServerLogLine.RedactBearer("plain message"));
        }

        [Test]
        public void FormatExceptionData_KeepsTypeOnly()
        {
            string data = ServerLogLine.FormatExceptionData(new InvalidOperationException("password=hunter2"));

            Assert.AreEqual("{\"exceptionType\":\"System.InvalidOperationException\"}", data);
        }

        [TestCase(LogType.Log, ServerLogLine.LEVEL_INFO)]
        [TestCase(LogType.Warning, ServerLogLine.LEVEL_WARN)]
        [TestCase(LogType.Error, ServerLogLine.LEVEL_ERROR)]
        [TestCase(LogType.Exception, ServerLogLine.LEVEL_ERROR)]
        [TestCase(LogType.Assert, ServerLogLine.LEVEL_ERROR)]
        public void LevelFor_MapsUnityLogType(LogType logType, string expected)
        {
            Assert.AreEqual(expected, ServerJsonLogHandler.LevelFor(logType));
        }

        [Test]
        public void Handler_LogFormat_WritesOneJsonLine()
        {
            var output  = new System.IO.StringWriter();
            var handler = new ServerJsonLogHandler(new ServerLogLine(SERVICE, INSTANCE_ID, CONTENT_VERSION), null, output,
                () => "s-9");

            handler.LogFormat(LogType.Warning, null, "{0}", "hello \"world\"");

            string text = output.ToString().TrimEnd('\r', '\n');
            Assert.AreEqual(-1, text.IndexOf('\n'));
            StringAssert.StartsWith("{\"ts\":", text);
            StringAssert.Contains("\"level\":\"warn\"", text);
            StringAssert.Contains("\"sessionId\":\"s-9\"", text);
            StringAssert.Contains("\"event\":\"unity.log\"", text);
            StringAssert.Contains("\"data\":{\"message\":\"hello \\\"world\\\"\"}", text);
        }
    }
}
