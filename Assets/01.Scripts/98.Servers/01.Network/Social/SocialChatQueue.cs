// System
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 게이트웨이는 요청을 병렬로 보내므로 같은 사람이 연달아 보낸 두 줄이 API에 뒤바뀌어 도착할 수 있다. 발신자마다 API 호출을 동시에
    // 1건만 두고 그동안 온 줄은 대기열에 둔다. 같은 발신자 줄의 delivery_id가 커밋 순서대로 커지고 꺼내기가 그 순서라 받는 쪽에서도
    // 발신자 순서가 지켜진다. 채팅 속도 제한이 1초 1줄이라 대기열은 거의 차지 않으며 넘치면 RateLimited로 거절한다.
    // 서로 다른 발신자 사이 순서는 보장하지 않는다
    /// <summary> 발신자별 귓속말, 길드 채팅 직렬 대기열, 엔진과 네트워크 상태를 갖지 않는다 </summary>
    public sealed class SocialChatQueue
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Dictionary<int, Sender> _senders;
        private readonly int                     _capacity;

        /****************************************
        *                Methods
        ****************************************/

        public SocialChatQueue() : this(Constants.Social.MAX_QUEUED_CHAT_PER_SENDER)
        {
        }

        /// <summary> capacity는 처리 중인 1줄을 뺀 대기 줄 수 </summary>
        public SocialChatQueue(int capacity)
        {
            _capacity = capacity;
            _senders  = new Dictionary<int, Sender>(Constants.Session.MAX_SESSION_PARTICIPANTS);
        }

        /// <summary> 한 줄을 넣는다. 처리 중인 줄이 없으면 sendNow가 참이고 호출자가 곧바로 보낸다. 대기열이 차면 RateLimited </summary>
        public EChatReject TryEnqueue(int participantId, in SocialChatLine line, out bool sendNow)
        {
            sendNow = false;
            if (!_senders.TryGetValue(participantId, out Sender sender))
            {
                sender = new Sender(_capacity);
                _senders.Add(participantId, sender);
            }

            if (!sender.IsBusy)
            {
                sender.IsBusy = true;
                sendNow       = true;
                return EChatReject.None;
            }

            if (sender.Waiting.Count >= _capacity)
            {
                return EChatReject.RateLimited;
            }

            sender.Waiting.Enqueue(line);
            return EChatReject.None;
        }

        /// <summary> 처리 중인 줄이 끝났다. 다음 줄이 있으면 꺼내 참, 없으면 발신자를 쉬게 하고 거짓 </summary>
        public bool TryNext(int participantId, out SocialChatLine line)
        {
            line = default;
            if (!_senders.TryGetValue(participantId, out Sender sender))
            {
                return false;
            }

            if (sender.Waiting.Count > 0)
            {
                line = sender.Waiting.Dequeue();
                return true;
            }

            sender.IsBusy = false;
            return false;
        }

        /// <summary> 처리 중이거나 기다리는 줄 수 </summary>
        public int CountOf(int participantId)
        {
            if (!_senders.TryGetValue(participantId, out Sender sender))
            {
                return 0;
            }

            return sender.Waiting.Count + (sender.IsBusy ? 1 : 0);
        }

        /// <summary> 연결이 끊겼다, 기다리는 줄을 버린다. 이미 보낸 줄의 응답은 받는 쪽 전달만 마친다 </summary>
        public void Forget(int participantId)
        {
            _senders.Remove(participantId);
        }

        /****************************************
        *               Records
        ****************************************/

        private sealed class Sender
        {
            public readonly Queue<SocialChatLine> Waiting;
            public bool                           IsBusy;

            public Sender(int capacity)
            {
                Waiting = new Queue<SocialChatLine>(capacity);
            }
        }
    }
}
