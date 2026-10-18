// System
using System.Collections.Generic;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public sealed class ChatBroadcastLimitTests
    {
        // 전체 채팅 서버 전체 초당 방송 상한: 상한을 넘으면 발신자에게 RateLimited, 창이 지나면 다시 받는다

        /****************************************
        *                Fields
        ****************************************/

        private CountingAudience _audience = null;
        private double           _now      = 0.0;
        private ChatServer       _server   = null;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _audience = new CountingAudience();
            _now      = 10.0;
            _server   = new ChatServer(_audience, null, () => _now);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Handle_BroadcastsBeyondServerLimit_RejectsExtraWithRateLimited()
        {
            // 발신자마다 한 건씩이라 발신자별 제한에는 걸리지 않는다
            for (int i = 0; i < Constants.Chat.BROADCASTS_PER_WINDOW; ++i)
            {
                Assert.That(Send(100 + i), Is.EqualTo(EChatReject.None));
            }

            Assert.That(Send(999), Is.EqualTo(EChatReject.RateLimited));
            Assert.That(_audience.Broadcasts, Is.EqualTo(Constants.Chat.BROADCASTS_PER_WINDOW));
            Assert.That(_audience.RejectTargets, Does.Contain(999));
        }

        [Test]
        public void Handle_NextWindow_AcceptsAgain()
        {
            for (int i = 0; i <= Constants.Chat.BROADCASTS_PER_WINDOW; ++i)
            {
                Send(100 + i);
            }

            _now += Constants.Chat.BROADCAST_WINDOW_SECONDS;

            Assert.That(Send(999), Is.EqualTo(EChatReject.None));
        }

        [Test]
        public void Handle_SameSenderTwiceInWindow_StillLimitedPerSender()
        {
            Assert.That(Send(1), Is.EqualTo(EChatReject.None));
            Assert.That(Send(1), Is.EqualTo(EChatReject.RateLimited));
            Assert.That(_audience.Broadcasts, Is.EqualTo(1));
        }

        [Test]
        public void Forget_NotifiesAudience()
        {
            _server.Forget(5);

            Assert.That(_audience.Forgotten, Is.EqualTo(new List<int> { 5 }));
        }

        /****************************************
        *               Helpers
        ****************************************/

        private EChatReject Send(int participantId)
        {
            var message = new ChatRequestMessage(EChatChannel.All, "hi");
            return _server.Handle(participantId, "n", in message);
        }

        private sealed class CountingAudience : IChatAudience
        {
            public int       Broadcasts;
            public List<int> RejectTargets = new List<int>();
            public List<int> Forgotten     = new List<int>();

            public void Send<T>(int participantId, T message) where T : struct, NetworkMessage
            {
                if (message is ChatRejectMessage)
                {
                    RejectTargets.Add(participantId);
                }
            }

            public void SendToAll<T>(T message) where T : struct, NetworkMessage
            {
                ++Broadcasts;
            }

            public void Forget(int participantId)
            {
                Forgotten.Add(participantId);
            }
        }
    }
}
