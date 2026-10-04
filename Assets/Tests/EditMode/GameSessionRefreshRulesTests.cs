// System
using System.Diagnostics;
using System.Reflection;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class GameSessionRefreshRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ACCOUNT_ID    = "acc-1";
        private const string OTHER_ACCOUNT = "acc-2";
        private const string FIRST_TOKEN   = "first-game-session-token";
        private const string SECOND_TOKEN  = "second-game-session-token";
        private const string THIRD_TOKEN   = "third-game-session-token";
        private const string API_BASE_URL  = "https://api.example.test";
        private const long   SERVER_TIME   = 1700000000000L;
        private const long   LIFETIME_MS   = 900000L;
        private const long   STATUS_OK     = 200;
        private const long   STATUS_DENIED = 401;
        private const long   STATUS_LIMIT  = 429;
        private const long   NO_RESPONSE   = 0;

        /****************************************
        *              Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            ResetLauncherSession();
        }

        [TearDown]
        public void TearDown()
        {
            ResetLauncherSession();
        }

        /****************************************
        *           Refresh Timing Tests
        ****************************************/

        [Test]
        public void ShouldRefresh_AboveLead_ReturnsFalse()
        {
            Assert.IsFalse(LauncherSessionRules.ShouldRefresh(LauncherSessionRules.REFRESH_LEAD_MS + 1));
        }

        [Test]
        public void ShouldRefresh_AtLead_ReturnsTrue()
        {
            Assert.IsTrue(LauncherSessionRules.ShouldRefresh(LauncherSessionRules.REFRESH_LEAD_MS));
        }

        [Test]
        public void ShouldRefresh_AlreadyExpired_ReturnsFalse()
        {
            Assert.IsFalse(LauncherSessionRules.ShouldRefresh(0));
        }

        [Test]
        public void TryGetRefreshRetryDelay_DoublesThenStops()
        {
            Assert.IsTrue(LauncherSessionRules.TryGetRefreshRetryDelay(0, out float first));
            Assert.IsTrue(LauncherSessionRules.TryGetRefreshRetryDelay(1, out float second));
            Assert.IsTrue(LauncherSessionRules.TryGetRefreshRetryDelay(2, out float third));
            Assert.IsFalse(LauncherSessionRules.TryGetRefreshRetryDelay(3, out _));
            Assert.AreEqual(1f, first);
            Assert.AreEqual(2f, second);
            Assert.AreEqual(4f, third);
        }

        /****************************************
        *          Reply Classify Tests
        ****************************************/

        [Test]
        public void ClassifyRefreshReply_ValidValue_ReturnsSucceeded()
        {
            Assert.AreEqual(EGameSessionRefreshOutcome.Succeeded,
                LauncherSessionRules.ClassifyRefreshReply(STATUS_OK, ContractCodes.RejectReason.NONE, CreateResponse(ACCOUNT_ID, SECOND_TOKEN)));
        }

        [Test]
        public void ClassifyRefreshReply_ValueWithoutToken_ReturnsStopped()
        {
            Assert.AreEqual(EGameSessionRefreshOutcome.Stopped,
                LauncherSessionRules.ClassifyRefreshReply(STATUS_OK, ContractCodes.RejectReason.NONE, CreateResponse(ACCOUNT_ID, " ")));
        }

        [TestCase(ContractCodes.RejectReason.TOKEN_REVOKED)]
        [TestCase(ContractCodes.RejectReason.TOKEN_EXPIRED)]
        public void ClassifyRefreshReply_TokenRejected_ReturnsSessionEnded(int reason)
        {
            Assert.AreEqual(EGameSessionRefreshOutcome.SessionEnded, LauncherSessionRules.ClassifyRefreshReply(STATUS_DENIED, reason, null));
        }

        [Test]
        public void ClassifyRefreshReply_NoResponse_ReturnsRetryTransport()
        {
            Assert.AreEqual(EGameSessionRefreshOutcome.RetryTransport,
                LauncherSessionRules.ClassifyRefreshReply(NO_RESPONSE, ContractCodes.RejectReason.NONE, null));
        }

        [Test]
        public void ClassifyRefreshReply_OtherRejection_ReturnsStopped()
        {
            Assert.AreEqual(EGameSessionRefreshOutcome.Stopped,
                LauncherSessionRules.ClassifyRefreshReply(STATUS_LIMIT, ContractCodes.RejectReason.RATE_LIMITED, null));
        }

        /****************************************
        *          Token Replace Tests
        ****************************************/

        [Test]
        public void ReplaceToken_CurrentAttempt_SwapsToken()
        {
            int attempt = AcceptSession();

            Assert.IsTrue(LauncherSession.ReplaceToken(attempt, CreateResponse(ACCOUNT_ID, SECOND_TOKEN), Stopwatch.GetTimestamp()));
            Assert.AreEqual(SECOND_TOKEN, LauncherSession.GetGameSessionToken());
            Assert.AreNotEqual(attempt, LauncherSession.SessionAttempt);
        }

        [Test]
        public void ReplaceToken_LateResponseForOldAttempt_IsDiscarded()
        {
            int attempt = AcceptSession();
            Assert.IsTrue(LauncherSession.ReplaceToken(attempt, CreateResponse(ACCOUNT_ID, SECOND_TOKEN), Stopwatch.GetTimestamp()));

            bool replaced = LauncherSession.ReplaceToken(attempt, CreateResponse(ACCOUNT_ID, THIRD_TOKEN), Stopwatch.GetTimestamp());

            Assert.IsFalse(replaced);
            Assert.AreEqual(SECOND_TOKEN, LauncherSession.GetGameSessionToken());
        }

        [Test]
        public void ReplaceToken_OtherAccount_IsDiscarded()
        {
            int attempt = AcceptSession();

            Assert.IsFalse(LauncherSession.ReplaceToken(attempt, CreateResponse(OTHER_ACCOUNT, SECOND_TOKEN), Stopwatch.GetTimestamp()));
            Assert.AreEqual(FIRST_TOKEN, LauncherSession.GetGameSessionToken());
        }

        [Test]
        public void ReplaceToken_WithoutSession_ReturnsFalse()
        {
            int attempt = LauncherSession.SessionAttempt;

            Assert.IsFalse(LauncherSession.ReplaceToken(attempt, CreateResponse(ACCOUNT_ID, SECOND_TOKEN), Stopwatch.GetTimestamp()));
            Assert.IsNull(LauncherSession.GetGameSessionToken());
        }

        [Test]
        public void EndSession_CurrentAttempt_FailsWithReasonAndClearsToken()
        {
            int attempt = AcceptSession();

            Assert.IsTrue(LauncherSession.EndSession(attempt, ELauncherSessionFailure.Revoked));
            Assert.AreEqual(ELauncherSessionState.Failed, LauncherSession.State);
            Assert.AreEqual(ELauncherSessionFailure.Revoked, LauncherSession.FailureReason);
            Assert.IsNull(LauncherSession.GetGameSessionToken());
        }

        [Test]
        public void EndSession_StaleAttempt_KeepsSession()
        {
            int attempt = AcceptSession();
            Assert.IsTrue(LauncherSession.ReplaceToken(attempt, CreateResponse(ACCOUNT_ID, SECOND_TOKEN), Stopwatch.GetTimestamp()));

            Assert.IsFalse(LauncherSession.EndSession(attempt, ELauncherSessionFailure.Revoked));
            Assert.AreEqual(ELauncherSessionState.Received, LauncherSession.State);
            Assert.AreEqual(SECOND_TOKEN, LauncherSession.GetGameSessionToken());
        }

        /****************************************
        *         Launch Status Text Tests
        ****************************************/

        [Test]
        public void DescribeLaunchStatus_LaunchRequiredNotLaunched_AsksLauncher()
        {
            Assert.AreEqual(LauncherSessionRules.TEXT_LAUNCH_REQUIRED, LauncherSessionRules.DescribeLaunchStatus(true,
                ELauncherSessionState.NotLaunched, ELauncherSessionFailure.None));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void DescribeLaunchStatus_Receiving_ShowsReceiving(bool launchRequired)
        {
            Assert.AreEqual(LauncherSessionRules.TEXT_RECEIVING, LauncherSessionRules.DescribeLaunchStatus(launchRequired,
                ELauncherSessionState.Receiving, ELauncherSessionFailure.None));
        }

        [TestCase(ELauncherSessionFailure.Revoked)]
        [TestCase(ELauncherSessionFailure.Expired)]
        public void DescribeLaunchStatus_LaunchRequiredSessionEnded_AsksRelaunch(ELauncherSessionFailure failure)
        {
            Assert.AreEqual(LauncherSessionRules.TEXT_SESSION_ENDED, LauncherSessionRules.DescribeLaunchStatus(true,
                ELauncherSessionState.Failed, failure));
        }

        [Test]
        public void DescribeLaunchStatus_LaunchRequiredExpiredReceived_AsksRelaunch()
        {
            Assert.AreEqual(LauncherSessionRules.TEXT_SESSION_ENDED, LauncherSessionRules.DescribeLaunchStatus(true,
                ELauncherSessionState.Received, ELauncherSessionFailure.None));
        }

        [Test]
        public void DescribeLaunchStatus_LaunchRequiredReceiveFailed_AsksRelaunch()
        {
            Assert.AreEqual(LauncherSessionRules.TEXT_RECEIVE_FAILED, LauncherSessionRules.DescribeLaunchStatus(true,
                ELauncherSessionState.Failed, ELauncherSessionFailure.Timeout));
        }

        [Test]
        public void DescribeLaunchStatus_DevelopmentNotLaunched_ShowsDevLogin()
        {
            Assert.AreEqual(LauncherSessionRules.TEXT_DEV_LOGIN, LauncherSessionRules.DescribeLaunchStatus(false,
                ELauncherSessionState.NotLaunched, ELauncherSessionFailure.None));
        }

        [Test]
        public void DescribeLaunchStatus_DevelopmentRevoked_ShowsDevSessionEnded()
        {
            Assert.AreEqual(LauncherSessionRules.TEXT_DEV_SESSION_ENDED, LauncherSessionRules.DescribeLaunchStatus(false,
                ELauncherSessionState.Failed, ELauncherSessionFailure.Revoked));
        }

        [Test]
        public void DescribeLaunchStatus_DevelopmentReceiveFailed_ShowsDevFallback()
        {
            Assert.AreEqual(LauncherSessionRules.TEXT_DEV_RECEIVE_FAILED, LauncherSessionRules.DescribeLaunchStatus(false,
                ELauncherSessionState.Failed, ELauncherSessionFailure.ConnectFailed));
        }

        /****************************************
        *               Helpers
        ****************************************/

        // 에디터는 런처 필수가 아니므로 개발 로그인 경로로 받은 세션을 만든다
        private static int AcceptSession()
        {
            var handoff = new LauncherHandoff(LauncherPipeProtocol.PROTOCOL_VERSION, ACCOUNT_ID, FIRST_TOKEN,
                SERVER_TIME + LIFETIME_MS, SERVER_TIME, API_BASE_URL);
            Assert.IsTrue(LauncherSession.AcceptDevelopmentLogin(handoff));
            return LauncherSession.SessionAttempt;
        }

        private static GameSessionIssueResponse CreateResponse(string accountId, string token)
        {
            return new GameSessionIssueResponse(accountId, token, SERVER_TIME + LIFETIME_MS, SERVER_TIME);
        }

        // 정적 세션이 다른 테스트로 새지 않게 플레이 시작 때와 같은 초기화를 부른다
        private static void ResetLauncherSession()
        {
            MethodInfo reset = typeof(LauncherSession).GetMethod("ResetStatic", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(reset);
            reset.Invoke(null, null);
        }
    }
}
