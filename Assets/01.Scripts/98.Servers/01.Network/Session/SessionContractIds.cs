// System
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 세션 API에 보내는 ID를 출정 ID와 순번에서 결정적으로 만든다, 같은 입력이면 프로세스가 달라도 같은 값이다 </summary>
    public static class SessionContractIds
    {
        // API는 operationId와 요청 해시로 효과를 한 번만 적용한다. 재시도 때 새 ID를 만들면 같은 배치가 다른 작업으로 보여
        // 순번 충돌로 거절되거나, 정산 ID가 SessionEnded에 적은 값과 달라져 정산이 막힌다. 그래서 난수 대신 이름에서 만든다.
        // 이름 앞의 종류 접두사가 달라 같은 출정 안에서도 이벤트, 배치, 정산, 시작 ID가 서로 겹치지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const string EVENT_PREFIX      = "shinshi.session-event|";
        private const string BATCH_PREFIX      = "shinshi.session-events|";
        private const string SETTLEMENT_PREFIX = "shinshi.session-settlement|";
        private const string START_PREFIX      = "shinshi.session-start|";
        private const string LEASE_PREFIX      = "shinshi.lease-transfer|";
        private const string INSTANCE_PREFIX   = "shinshi.instance|";
        private const char   SEPARATOR         = '|';
        private const string GUID_FORMAT       = "D";

        // RFC 4122 이름 기반 형식 표시, 7번 바이트 상위 4비트가 판, 8번 바이트 상위 2비트가 변형이다
        private const int  GUID_BYTES                    = 16;
        private const int  GUID_TEXT_CAPACITY_MULTIPLIER = 3; // 참가자 GUID 문자열과 구분자에 예약할 초기 용량 배수
        private const int  VERSION_INDEX                 = 7;
        private const int  VARIANT_INDEX                 = 8;
        private const byte VERSION_CLEAR                 = 0x0F;
        private const byte VERSION_NAME_SHA              = 0x50;
        private const byte VARIANT_CLEAR                 = 0x3F;
        private const byte VARIANT_RFC4122               = 0x80;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이벤트 한 건의 ID, 같은 출정의 같은 순번은 항상 같다 </summary>
        public static string CreateEventId(string sessionId, long sequence)
        {
            return CreateNameGuid(EVENT_PREFIX + sessionId + SEPARATOR + sequence.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary> 저장 배치의 operationId, 배치의 첫 순번과 마지막 순번으로 정한다 </summary>
        public static string CreateEventsOperationId(string sessionId, long firstSequence, long lastSequence)
        {
            return CreateNameGuid(BATCH_PREFIX + sessionId + SEPARATOR + firstSequence.ToString(CultureInfo.InvariantCulture)
                + SEPARATOR + lastSequence.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary> 출정 하나의 정산 operationId, SessionEnded 이벤트와 settlement 호출이 같은 값을 쓴다 </summary>
        public static string CreateSettlementOperationId(string sessionId)
        {
            return CreateNameGuid(SETTLEMENT_PREFIX + sessionId);
        }

        /// <summary> 시작 operationId, 준비 참가자 집합이 같으면 같은 값이다. 집합이 바뀌면 본문이 달라지므로 새 값을 쓴다 </summary>
        public static string CreateStartOperationId(string sessionId, string[] sortedParticipantIds, int count)
        {
            var builder = new StringBuilder(START_PREFIX, START_PREFIX.Length + sessionId.Length + (count * GUID_BYTES * GUID_TEXT_CAPACITY_MULTIPLIER));
            builder.Append(sessionId);
            for (int i = 0; i < count; ++i)
            {
                builder.Append(SEPARATOR);
                builder.Append(sortedParticipantIds[i]);
            }

            return CreateNameGuid(builder.ToString());
        }

        /// <summary> 참가자 접속 임대 이관의 newLeaseId, 같은 출정의 같은 참가자는 재시도와 재접속에도 같은 값이다 </summary>
        public static string CreateLeaseTransferId(string sessionId, string participantId)
        {
            return CreateNameGuid(LEASE_PREFIX + sessionId + SEPARATOR + participantId);
        }

        /// <summary> 실행 인자로 인스턴스 ID를 주지 않은 개발 실행의 기본값, 같은 역할과 포트면 항상 같다 </summary>
        public static string CreateDefaultInstanceId(string roleName, int port)
        {
            return CreateNameGuid(INSTANCE_PREFIX + roleName + SEPARATOR + port.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary> 비어 있지 않은 GUID 문자열인지 본다, API가 출정과 참가자 ID를 이 형식으로만 받는다 </summary>
        public static bool IsApiId(string value)
        {
            return (!string.IsNullOrEmpty(value)) && (Guid.TryParse(value, out Guid parsed)) && (parsed != Guid.Empty);
        }

        /// <summary> 던전 참가자 키에서 API 참가자 ID를 꺼낸다, 개발 토큰처럼 GUID가 아니면 거짓 </summary>
        public static bool TryGetApiParticipantId(string participantKey, out string participantId)
        {
            participantId = null;
            string prefix = Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX;
            if ((string.IsNullOrEmpty(participantKey)) || (!participantKey.StartsWith(prefix, StringComparison.Ordinal)))
            {
                return false;
            }

            string candidate = participantKey.Substring(prefix.Length);
            if (!IsApiId(candidate))
            {
                return false;
            }

            participantId = candidate;
            return true;
        }

        private static string CreateNameGuid(string name)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create())
            {
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(name));
            }

            var bytes = new byte[GUID_BYTES];
            Array.Copy(hash, bytes, GUID_BYTES);
            bytes[VERSION_INDEX] = (byte)((bytes[VERSION_INDEX] & VERSION_CLEAR) | VERSION_NAME_SHA);
            bytes[VARIANT_INDEX] = (byte)((bytes[VARIANT_INDEX] & VARIANT_CLEAR) | VARIANT_RFC4122);
            return new Guid(bytes).ToString(GUID_FORMAT);
        }
    }
}
