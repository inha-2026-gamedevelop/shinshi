// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 401은 서버 임대 갱신 사이의 거절이라 대개 처리 전이지만 단정하지 않고 결과 불명으로 보고 스냅샷을 다시 받는다.
    // 429는 API가 처리하지 않은 것이 확실하다(게이트웨이는 ServerError로 분류한다)
    /// <summary> 소셜 API 응답을 결론과 화면 사유(ERejectReason, EChatReject)로 바꾸는 순수 규칙 </summary>
    public static class SocialApiRules
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int STATUS_UNAUTHORIZED      = 401;
        private const int STATUS_TOO_MANY_REQUESTS = 429;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 응답의 결론과 클라이언트에게 돌려줄 사유. nested는 본문의 결과(없으면 null) </summary>
        public static ESocialApiVerdict Classify(in BackendResponse response, SocialActionResponse nested, out ERejectReason reason)
        {
            reason = ERejectReason.BackendUnavailable;
            switch (response.Outcome)
            {
                case EBackendOutcome.Succeeded:
                    if ((nested == null) || (nested.Status == ContractCodes.OperationStatus.COMMITTED))
                    {
                        reason = ERejectReason.None;
                        return ESocialApiVerdict.Committed;
                    }

                    if (nested.Status == ContractCodes.OperationStatus.REJECTED)
                    {
                        reason = TownRejectRules.FromApi(nested.ReasonCode, ERejectReason.InvalidTarget);
                        return ESocialApiVerdict.Rejected;
                    }

                    return ESocialApiVerdict.Unknown;
                case EBackendOutcome.Rejected:
                    if (response.StatusCode == STATUS_UNAUTHORIZED)
                    {
                        return ESocialApiVerdict.Unknown;
                    }

                    int code = ((nested != null) && (nested.ReasonCode != ContractCodes.RejectReason.NONE))
                        ? nested.ReasonCode
                        : response.ReasonCode;
                    reason = TownRejectRules.FromApi(code, ERejectReason.InvalidTarget);
                    return ESocialApiVerdict.Rejected;
                case EBackendOutcome.ServerError:
                    if (response.StatusCode == STATUS_TOO_MANY_REQUESTS)
                    {
                        reason = ERejectReason.RateLimited;
                        return ESocialApiVerdict.Rejected;
                    }

                    return ESocialApiVerdict.Unknown;
                case EBackendOutcome.CircuitOpen:
                case EBackendOutcome.Overloaded:
                case EBackendOutcome.LeaseUnavailable:
                    return ESocialApiVerdict.NotSent;
                default:
                    return ESocialApiVerdict.Unknown;
            }
        }

        /// <summary> 귓속말, 길드 채팅 거절 사유를 채팅 안내 사유로 바꾼다. 모르는 사유는 BackendUnavailable </summary>
        public static EChatReject ToChatReject(ERejectReason reason)
        {
            switch (reason)
            {
                case ERejectReason.None:
                    return EChatReject.None;
                case ERejectReason.SocialTargetNotFound:
                    return EChatReject.WhisperTargetNotFound;
                case ERejectReason.WhisperTargetOffline:
                    return EChatReject.WhisperTargetOffline;
                case ERejectReason.SocialTargetSelf:
                    return EChatReject.WhisperSelf;
                case ERejectReason.GuildNotMember:
                    return EChatReject.NoGuild;
                case ERejectReason.FeatureDisabled:
                    return EChatReject.FeatureDisabled;
                case ERejectReason.RateLimited:
                    return EChatReject.RateLimited;
                default:
                    return EChatReject.BackendUnavailable;
            }
        }
    }
}
