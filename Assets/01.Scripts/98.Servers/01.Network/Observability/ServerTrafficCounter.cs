// System
using System;
using System.Collections.Generic;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    /// <summary> 서버가 트랜스포트로 주고받은 바이트를 센다. 켜면 Mirror 메시지 종류별 대역폭도 센다 </summary>
    // 전체 바이트는 트랜스포트 콜백에서 세므로 할당이 없다. KCP와 UDP 헤더는 빠진 페이로드 크기다.
    // 종류별 집계는 Mirror가 메시지를 보낼 때마다 박싱해 이벤트로 넘기므로 프로파일링할 때만 켠다.
    // 콜백은 모두 Mirror가 메인 스레드에서 부른다
    public sealed class ServerTrafficCounter : IDisposable
    {
        /// <summary> 메시지 종류 하나의 누적 송수신 </summary>
        public sealed class MessageStat
        {
            /****************************************
            *                Fields
            ****************************************/

            private readonly string _name;

            public string Name          => _name;
            public long   SentBytes     { get; private set; }
            public long   SentCount     { get; private set; }
            public long   ReceivedBytes { get; private set; }
            public long   ReceivedCount { get; private set; }

            /****************************************
            *                Methods
            ****************************************/

            public MessageStat(string name)
            {
                _name = name;
            }

            public void AddSent(long bytes, long count)
            {
                SentBytes += bytes;
                SentCount += count;
            }

            public void AddReceived(long bytes)
            {
                ReceivedBytes += bytes;
                ++ReceivedCount;
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly Dictionary<Type, MessageStat> _byType   = new Dictionary<Type, MessageStat>();
        private readonly List<MessageStat>             _messages = new List<MessageStat>();

        private Transport _transport = null;
        private bool      _detailed  = false;

        private Action<int, ArraySegment<byte>, int>   _onSent     = null;
        private Action<int, ArraySegment<byte>, int>   _onReceived = null;
        private Action<NetworkDiagnostics.MessageInfo> _onOut      = null;
        private Action<NetworkDiagnostics.MessageInfo> _onIn       = null;

        /****************************************
        *              Properties
        ****************************************/

        public long SentBytes       { get; private set; }
        public long SentPackets     { get; private set; }
        public long ReceivedBytes   { get; private set; }
        public long ReceivedPackets { get; private set; }
        public bool IsDetailed      => _detailed;

        // 처음 본 순서로 쌓인다. 렌더가 이 목록을 돌아 사전 열거를 피한다
        public IReadOnlyList<MessageStat> Messages => _messages;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 트랜스포트가 없으면 거짓 </summary>
        public bool Attach(Transport transport, bool detailed)
        {
            if (transport == null)
            {
                return false;
            }

            Dispose();
            _transport  = transport;
            _detailed   = detailed;
            _onSent     = OnServerDataSent;
            _onReceived = OnServerDataReceived;
            _transport.OnServerDataSent     += _onSent;
            _transport.OnServerDataReceived += _onReceived;

            if (_detailed)
            {
                _onOut = OnOutMessage;
                _onIn  = OnInMessage;
                NetworkDiagnostics.OutMessageEvent += _onOut;
                NetworkDiagnostics.InMessageEvent  += _onIn;
            }

            return true;
        }

        public void Dispose()
        {
            if (_transport != null)
            {
                _transport.OnServerDataSent     -= _onSent;
                _transport.OnServerDataReceived -= _onReceived;
                _transport = null;
            }

            if (_onOut != null)
            {
                NetworkDiagnostics.OutMessageEvent -= _onOut;
                NetworkDiagnostics.InMessageEvent  -= _onIn;
                _onOut = null;
                _onIn  = null;
            }

            _detailed = false;
        }

        /// <summary> 메시지 하나를 count개 연결에 보낸 것으로 센다 </summary>
        public void RecordMessageSent(Type messageType, int bytes, int count)
        {
            if ((messageType == null) || (count <= 0))
            {
                return;
            }

            GetStat(messageType).AddSent((long)bytes * count, count);
        }

        public void RecordMessageReceived(Type messageType, int bytes)
        {
            if (messageType == null)
            {
                return;
            }

            GetStat(messageType).AddReceived(bytes);
        }

        private MessageStat GetStat(Type messageType)
        {
            if (!_byType.TryGetValue(messageType, out MessageStat stat))
            {
                stat = new MessageStat(messageType.Name);
                _byType.Add(messageType, stat);
                _messages.Add(stat);
            }

            return stat;
        }

        private void OnServerDataSent(int connectionId, ArraySegment<byte> data, int channelId)
        {
            SentBytes += data.Count;
            ++SentPackets;
        }

        private void OnServerDataReceived(int connectionId, ArraySegment<byte> data, int channelId)
        {
            ReceivedBytes += data.Count;
            ++ReceivedPackets;
        }

        private void OnOutMessage(NetworkDiagnostics.MessageInfo info)
        {
            RecordMessageSent(info.message?.GetType(), info.bytes, info.count);
        }

        private void OnInMessage(NetworkDiagnostics.MessageInfo info)
        {
            RecordMessageReceived(info.message?.GetType(), info.bytes);
        }
    }
}
