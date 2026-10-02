// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.Tests
{
    public class ReviveRequestTests
    {
        // 부활 요청 연타가 대기 요청의 재평가 시각을 0으로 되돌리면 안전 지점 탐색이 매 틱 다시 돈다

        /****************************************
        *                Fields
        ****************************************/

        private const double DEFERRED_CHECK_TIME = 105.5;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void NextReviveCheckOnRequest_PendingRequest_KeepsDeferredTime()
        {
            Assert.AreEqual(DEFERRED_CHECK_TIME, PlayerServer.NextReviveCheckOnRequest(true, DEFERRED_CHECK_TIME));
        }

        [Test]
        public void NextReviveCheckOnRequest_NoPendingRequest_EvaluatesImmediately()
        {
            Assert.AreEqual(0.0, PlayerServer.NextReviveCheckOnRequest(false, DEFERRED_CHECK_TIME));
        }
    }
}
