#if UNITY_EDITOR || DEVELOPMENT_BUILD
// System
using System;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 에디터, 개발 빌드 전용 토큰 검증기, "dev-{참가자 키}" 형식만 보고 서명, 만료, 1회 사용은 보지 않는다 </summary>
    public sealed class DevJoinTokenVerifier : IJoinTokenVerifier
    {
        // 출시 서버 빌드에 들어가면 아무나 원하는 참가자 키로 들어올 수 있으므로 파일 전체를 개발 정의 안에 둔다

        /****************************************
        *                Methods
        ****************************************/

        public void Verify(int requestId, string joinToken, IJoinTokenVerifyListener listener)
        {
            if (listener == null)
            {
                throw new ArgumentNullException(nameof(listener));
            }

            if (!TryParse(joinToken, out string participantKey))
            {
                listener.OnJoinTokenVerified(requestId, ERejectReason.TokenRevoked, default);
                return;
            }

            // 개발 토큰에는 캐릭터가 없어 참가자 키가 이름 규칙에 맞으면 그대로 이름으로 띄운다
            string characterName = CharacterNameRules.TryNormalize(participantKey, out string name, out string _) ? name : null;
            listener.OnJoinTokenVerified(requestId, ERejectReason.None, new JoinTokenClaims(participantKey, characterName));
        }

        // 같은 개발 토큰은 언제나 다시 쓸 수 있으므로 기억할 것이 없다
        public void OnAdmitted(int requestId) { }

        /// <summary> 형식만 확인해 참가자 키를 꺼낸다 </summary>
        public static bool TryParse(string joinToken, out string participantKey)
        {
            participantKey = null;
            if ((joinToken == null) || (!joinToken.StartsWith(Constants.Network.DEV_JOIN_TOKEN_PREFIX, StringComparison.Ordinal)))
            {
                return false;
            }

            int keyLength = joinToken.Length - Constants.Network.DEV_JOIN_TOKEN_PREFIX.Length;
            if ((keyLength <= 0) || (keyLength > Constants.Network.DEV_PARTICIPANT_KEY_MAX_LENGTH))
            {
                return false;
            }

            for (int i = Constants.Network.DEV_JOIN_TOKEN_PREFIX.Length; i < joinToken.Length; ++i)
            {
                if (!IsKeyCharacter(joinToken[i]))
                {
                    return false;
                }
            }

            participantKey = joinToken.Substring(Constants.Network.DEV_JOIN_TOKEN_PREFIX.Length);
            return true;
        }

        // 로그와 파일 이름에 그대로 써도 안전한 ASCII만 받는다
        private static bool IsKeyCharacter(char value)
        {
            return ((value >= 'a') && (value <= 'z')) || ((value >= 'A') && (value <= 'Z'))
                || ((value >= '0') && (value <= '9')) || (value == '-') || (value == '_');
        }
    }
}
#endif
