// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // Mirror Send는 보내기 버퍼에 넣을 뿐이고 연결 정리 때 버퍼를 버린다. 그래서 서버는 보낸 직후가 아니라 클라이언트가 받은 ID를
    // 확인해 준 뒤에만 API에 지움을 요청한다. 확인 메시지의 ID 중 그 연결에 보낸 기록이 있는 것만 받는다. 수신 캐릭터는 보낸 기록에서
    // 정하고 클라이언트 값으로 정하지 않는다. 연결마다 기록은 상한까지만 두고 넘치면 가장 오래된 것을 덮는다(행은 DB에 남아 다시 온다).
    // 재전송 간격은 DB의 next_attempt_at이 관리하므로 여기에는 시각을 두지 않는다
    /// <summary> 연결마다 보낸 전달 ID와 수신 캐릭터, 확인 메시지 속도 제한, 다음 꺼내기에 실을 확인 목록 </summary>
    public sealed class SocialDeliveryLedger
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Dictionary<int, Connection> _connections;
        private readonly List<SocialDeliveryAck>     _outgoing;
        private readonly int                         _capacity;

        /****************************************
        *              Properties
        ****************************************/

        // 다음 꺼내기에 실을 확인 수
        public int OutgoingAckCount => _outgoing.Count;

        /****************************************
        *                Methods
        ****************************************/

        public SocialDeliveryLedger() : this(Constants.Social.MAX_PENDING_DELIVERIES_PER_CONNECTION)
        {
        }

        public SocialDeliveryLedger(int capacityPerConnection)
        {
            if (capacityPerConnection <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacityPerConnection));
            }

            _capacity    = capacityPerConnection;
            _connections = new Dictionary<int, Connection>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _outgoing    = new List<SocialDeliveryAck>(SocialLimits.MAX_DELIVERIES_PER_PULL);
        }

        /// <summary> 이 연결에 전달 행을 보냈다고 적는다. 같은 ID를 다시 보내면 기록 하나를 유지한다 </summary>
        public void RecordSent(int participantId, string recipientCharacterId, long deliveryId)
        {
            if ((deliveryId <= 0) || (string.IsNullOrEmpty(recipientCharacterId)))
            {
                return;
            }

            if (!_connections.TryGetValue(participantId, out Connection connection))
            {
                connection = new Connection(_capacity);
                _connections.Add(participantId, connection);
            }

            connection.Record(deliveryId, recipientCharacterId);
        }

        /// <summary> 이 연결에 보냈고 아직 확인받지 않은 기록이 있으면 참 </summary>
        public bool IsPending(int participantId, long deliveryId)
        {
            return (_connections.TryGetValue(participantId, out Connection connection)) && (connection.IndexOf(deliveryId) >= 0);
        }

        /// <summary> 클라이언트 확인 메시지 한 건을 받는다. 속도, 길이 상한을 넘으면 통째로 버린다. 받아들인 ID 수 </summary>
        public int AcceptAcks(int participantId, long[] deliveryIds, double now)
        {
            if ((deliveryIds == null) || (deliveryIds.Length == 0) || (deliveryIds.Length > Constants.Social.MAX_ACK_IDS_PER_MESSAGE))
            {
                return 0;
            }

            if (!_connections.TryGetValue(participantId, out Connection connection))
            {
                return 0;
            }

            if (!connection.Throttle.TryAccept(now, Constants.Social.ACK_MESSAGES_PER_WINDOW, Constants.Social.ACK_WINDOW_SECONDS))
            {
                return 0;
            }

            int accepted = 0;
            for (int i = 0; i < deliveryIds.Length; ++i)
            {
                long id    = deliveryIds[i];
                int  index = connection.IndexOf(id);
                if (index < 0)
                {
                    continue;
                }

                _outgoing.Add(new SocialDeliveryAck(id, connection.RecipientAt(index)));
                connection.Clear(index);
                ++accepted;
            }

            return accepted;
        }

        /// <summary> 다음 꺼내기 요청에 실을 확인을 최대 max개 꺼낸다, 없으면 빈 배열 </summary>
        public SocialDeliveryAck[] TakeAcks(int max)
        {
            int count = Math.Min(_outgoing.Count, max);
            if (count <= 0)
            {
                return Array.Empty<SocialDeliveryAck>();
            }

            var acks = new SocialDeliveryAck[count];
            _outgoing.CopyTo(0, acks, 0, count);
            _outgoing.RemoveRange(0, count);
            return acks;
        }

        /// <summary> 꺼내기 요청이 실패했다, 실었던 확인을 다음 요청 맨 앞에 다시 넣는다 </summary>
        public void RestoreAcks(SocialDeliveryAck[] acks)
        {
            if ((acks == null) || (acks.Length == 0))
            {
                return;
            }

            _outgoing.InsertRange(0, acks);
        }

        /// <summary> 연결이 끊겼다, 보낸 기록을 버린다. 확인받지 못한 행은 DB에 남아 다시 꺼내진다 </summary>
        public void Forget(int participantId)
        {
            _connections.Remove(participantId);
        }

        /****************************************
        *               Records
        ****************************************/

        // 연결 하나의 보낸 기록. 고리 배열이라 기록에 할당이 없고, 확인할 때 상한 크기만큼 훑는다
        private sealed class Connection
        {
            private readonly long[]   _ids;
            private readonly string[] _recipients;

            private int _next = 0;

            public TownRequestThrottle Throttle;

            public Connection(int capacity)
            {
                _ids        = new long[capacity];
                _recipients = new string[capacity];
            }

            public void Record(long deliveryId, string recipient)
            {
                int index = IndexOf(deliveryId);
                if (index >= 0)
                {
                    _recipients[index] = recipient;
                    return;
                }

                _ids[_next]        = deliveryId;
                _recipients[_next] = recipient;
                _next = (_next + 1) % _ids.Length;
            }

            public int IndexOf(long deliveryId)
            {
                if (deliveryId <= 0)
                {
                    return -1;
                }

                for (int i = 0; i < _ids.Length; ++i)
                {
                    if (_ids[i] == deliveryId)
                    {
                        return i;
                    }
                }

                return -1;
            }

            public string RecipientAt(int index)
            {
                return _recipients[index];
            }

            public void Clear(int index)
            {
                _ids[index]        = 0;
                _recipients[index] = null;
            }
        }
    }
}
