// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using kcp2k;
using Mirror;

namespace Shinshi.Network
{
    /// <summary> 연결별 kcp 송신 큐와 송신 버퍼 길이를 모아 합계, 최댓값, 임계 초과 횟수를 낸다 </summary>
    public sealed class ServerKcpQueueProbe
    {
        // KcpTransport.server가 protected라 Assets/Mirror 원본을 고치지 않고는 읽을 수 없다. 1초에 한 번만 읽으므로 리플렉션으로 꺼낸다.
        // IL2CPP 코드 제거가 이 필드를 지우면 지표가 빠질 뿐이다. 서버 빌드의 링커 설정은 별도로 확인해야 한다.
        // 연결 사전은 메인 스레드가 갱신하므로 메인 스레드에서만 부른다. 경고는 임계값을 위로 넘는 순간 연결마다 한 번 낸다
        // 연결을 끊지는 않는다. 끊김은 Mirror의 합계 1만 줄 규칙에 맡긴다

        /****************************************
        *                Fields
        ****************************************/

        public const int DEFAULT_WARN_THRESHOLD = 2000;

        private const string SERVER_FIELD_NAME = "server";

        private static readonly FieldInfo _serverField = typeof(KcpTransport).GetField(SERVER_FIELD_NAME,
            BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly int _warnThreshold;

        private HashSet<int> _over     = new HashSet<int>();
        private HashSet<int> _overNext = new HashSet<int>();

        private bool _sampling = false;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 마지막 표본을 얻었는지, 거짓이면 지표를 내지 않는다 </summary>
        public bool IsAvailable       { get; private set; }
        public int  ConnectionCount   { get; private set; }
        public long SendQueueTotal    { get; private set; }
        public int  SendQueueMax      { get; private set; }
        public long SendBufferTotal   { get; private set; }
        public int  SendBufferMax     { get; private set; }

        // 연결 하나의 송신 큐와 송신 버퍼를 더한 값 중 가장 큰 것
        public int  BacklogMax        { get; private set; }
        public long WarnTotal         { get; private set; }
        public int  WarnThreshold     => _warnThreshold;

        /****************************************
        *                Methods
        ****************************************/

        public ServerKcpQueueProbe(int warnThreshold = DEFAULT_WARN_THRESHOLD)
        {
            _warnThreshold = warnThreshold;
        }

        /// <summary> 활성 트랜스포트가 KcpTransport이고 서버가 열려 있으면 표본을 얻는다, 아니면 지표를 비운다 </summary>
        public void Sample(Transport transport)
        {
            KcpServer server = null;
            if ((transport is KcpTransport kcp) && (_serverField != null))
            {
                server = _serverField.GetValue(kcp) as KcpServer;
            }

            if (server == null)
            {
                Clear();
                return;
            }

            BeginSample();
            foreach (KeyValuePair<int, KcpServerConnection> pair in server.connections)
            {
                KcpServerConnection connection = pair.Value;
                if (connection != null)
                {
                    Observe(pair.Key, connection.SendQueueCount, connection.SendBufferCount);
                }
            }

            EndSample();
        }

        /// <summary> 표본 한 번을 시작한다, 이어서 연결마다 Observe를 부르고 EndSample로 끝낸다 </summary>
        public void BeginSample()
        {
            _sampling        = true;
            ConnectionCount  = 0;
            SendQueueTotal   = 0;
            SendQueueMax     = 0;
            SendBufferTotal  = 0;
            SendBufferMax    = 0;
            BacklogMax       = 0;
            _overNext.Clear();
        }

        public void Observe(int connectionId, int sendQueue, int sendBuffer)
        {
            if (!_sampling)
            {
                return;
            }

            ++ConnectionCount;
            SendQueueTotal  += sendQueue;
            SendBufferTotal += sendBuffer;
            if (sendQueue > SendQueueMax)
            {
                SendQueueMax = sendQueue;
            }

            if (sendBuffer > SendBufferMax)
            {
                SendBufferMax = sendBuffer;
            }

            int backlog = sendQueue + sendBuffer;
            if (backlog > BacklogMax)
            {
                BacklogMax = backlog;
            }

            if (backlog < _warnThreshold)
            {
                return;
            }

            _overNext.Add(connectionId);
            if (_over.Contains(connectionId))
            {
                return;
            }

            ++WarnTotal;
            Debug.LogWarning($"[Kcp] 연결 송신 대기열이 임계값을 넘었다, connection={connectionId}, " +
                $"sendQueue={sendQueue}, sendBuffer={sendBuffer}, threshold={_warnThreshold}");
        }

        public void EndSample()
        {
            if (!_sampling)
            {
                return;
            }

            _sampling = false;
            IsAvailable = true;

            // 이번에도 넘은 연결만 다음 표본의 기준으로 남겨 다시 내려갔다 올라오면 경고한다
            (_over, _overNext) = (_overNext, _over);
        }

        private void Clear()
        {
            IsAvailable     = false;
            _sampling       = false;
            ConnectionCount = 0;
            SendQueueTotal  = 0;
            SendQueueMax    = 0;
            SendBufferTotal = 0;
            SendBufferMax   = 0;
            BacklogMax      = 0;
            _over.Clear();
            _overNext.Clear();
        }
    }
}
