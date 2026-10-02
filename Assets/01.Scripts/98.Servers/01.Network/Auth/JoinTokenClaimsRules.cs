// System
using System;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 토큰 교환 응답을 이 서버의 입장 정보로 바꾼다, 인스턴스와 역할이 다르거나 결합 규칙에 맞지 않으면 거절한다 </summary>
    public static class JoinTokenClaimsRules
    {
        // 참가자 키 결합 규칙
        // - 던전: 출정 참가자 ID가 키다. 같은 출정에서 연결이 바뀌어도(netId가 달라져도) 같은 참가자로 이어진다
        // - 마을: 출정 참가자가 없으므로 캐릭터 ID가 키다. 같은 캐릭터의 두 번째 연결은 이미 접속 중으로 거절된다
        // 두 키는 접두사가 달라 한 프로세스 안에서 섞여도 겹치지 않는다. 계정 단위 단일 세션은 API의 계정 세션 임대가 맡는다

        /****************************************
        *                Fields
        ****************************************/

        private const string GUID_FORMAT = "D";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 통과하면 None과 입장 정보, 아니면 거절 사유 </summary>
        public static ERejectReason TryCreate(EServerRole serverRole, string expectedInstanceId, JoinTokenExchangeResponse response,
            out JoinTokenClaims claims)
        {
            claims = default;
            if (response == null)
            {
                return ERejectReason.TokenRevoked;
            }

            // 다른 서버에 배정된 토큰은 가로챈 토큰일 수 있다. API가 걸렀어도 여기서 다시 막는다
            if ((string.IsNullOrEmpty(expectedInstanceId))
                || (!string.Equals(response.InstanceId, expectedInstanceId, StringComparison.Ordinal))
                || (response.ServerRole != (int)serverRole))
            {
                return ERejectReason.SessionMismatch;
            }

            if (!IsSafeId(response.CharacterId))
            {
                return ERejectReason.TokenRevoked;
            }

            bool hasParticipant = !string.IsNullOrEmpty(response.ParticipantId);
            bool hasSortie      = !string.IsNullOrEmpty(response.SortieId);
            if (serverRole == EServerRole.Dungeon)
            {
                if ((!hasParticipant) || (!hasSortie) || (!IsSafeId(response.ParticipantId)) || (!IsSafeId(response.SortieId)))
                {
                    return ERejectReason.SessionMismatch;
                }

                claims = new JoinTokenClaims(Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + response.ParticipantId,
                    response.CharacterId, response.InstanceId, response.SortieId);
                return ERejectReason.None;
            }

            // 출정 정보가 붙은 마을 입장은 발급 쪽 결함이다. 어느 키로 묶을지 정할 수 없으므로 받지 않는다
            if ((hasParticipant) || (hasSortie))
            {
                return ERejectReason.SessionMismatch;
            }

            claims = new JoinTokenClaims(Constants.Network.PARTICIPANT_KEY_TOWN_PREFIX + response.CharacterId,
                response.CharacterId, response.InstanceId, null, ToAccountLeaseId(ReadAccountLeaseId(response)));
            return ERejectReason.None;
        }

        /// <summary> 해제 경로에 넣을 수 있는 계정 접속 임대 ID만 남긴다, GUID가 아니면 null(해제 없이 heartbeat 만료로 풀린다) </summary>
        public static string ToAccountLeaseId(string value)
        {
            // 괄호 형식 GUID도 TryParse를 통과하므로 경로에는 하이픈 형식으로만 싣는다
            if ((!SessionContractIds.IsApiId(value)) || (!Guid.TryParse(value, out Guid parsed)))
            {
                return null;
            }

            return parsed.ToString(GUID_FORMAT);
        }

        // 마을 교환만 값이 있고 던전은 null이다. null이면 연결 종료 때 해제하지 않고 heartbeat 만료로 풀린다
        private static string ReadAccountLeaseId(JoinTokenExchangeResponse response)
        {
            return response.AccountLeaseId;
        }

        /// <summary> API 거절 코드를 접속 거절 사유로 바꾼다, 모르는 값이나 0은 토큰 무효로 본다 </summary>
        public static ERejectReason FromApiReasonCode(int reasonCode)
        {
            if ((reasonCode == ContractCodes.RejectReason.NONE) || (!Enum.IsDefined(typeof(ERejectReason), reasonCode)))
            {
                return ERejectReason.TokenRevoked;
            }

            return (ERejectReason)reasonCode;
        }

        // 키는 로그와 저장 이벤트에 그대로 쓰이므로 ASCII 식별자 문자만 받는다
        private static bool IsSafeId(string value)
        {
            if ((string.IsNullOrEmpty(value)) || (value.Length > Constants.Network.MAX_JOIN_CLAIM_ID_LENGTH))
            {
                return false;
            }

            for (int i = 0; i < value.Length; ++i)
            {
                char letter = value[i];
                bool isSafe = ((letter >= 'a') && (letter <= 'z')) || ((letter >= 'A') && (letter <= 'Z'))
                    || ((letter >= '0') && (letter <= '9')) || (letter == '-') || (letter == '_') || (letter == '.');
                if (!isSafe)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
