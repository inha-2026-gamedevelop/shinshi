// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 눌린 대상이 내 캐릭터인지 다른 유저인지, 다른 유저 패널의 소셜 버튼을 켤지 가르는 순수 계산 </summary>
    public static class PlayerInteractionRules
    {
        /****************************************
        *                Types
        ****************************************/

        public enum ETarget
        {
            None,
            Self,
            Other,
        }

        /****************************************
        *                Methods
        ****************************************/

        // 씬에 고정으로 연결한 콜라이더(개발 씬)가 우선이고, 아니면 맞은 오브젝트 부모의 플레이어 상태로 가른다
        public static ETarget Classify(bool isFixedSelf, bool isFixedOther, bool hasPlayer, bool isLocalPlayer)
        {
            if (isFixedSelf)
            {
                return ETarget.Self;
            }

            if (isFixedOther)
            {
                return ETarget.Other;
            }

            if (!hasPlayer)
            {
                return ETarget.None;
            }

            return isLocalPlayer ? ETarget.Self : ETarget.Other;
        }

        // 관리 요청은 마을 서버만 받고 연결당 쓰기 1건이다. 조회 실패 뒤에는 친구, 길드 창과 같이 쓰기를 잠근다
        /// <summary> 친구 요청, 길드 초대를 보낼 수 있는 상태인지 </summary>
        public static bool CanSendSocialRequest(bool isTownConnection, bool hasPendingWrite, bool isSnapshotFailed)
        {
            return (isTownConnection) && (!hasPendingWrite) && (!isSnapshotFailed);
        }

        /// <summary> 길드 초대 버튼은 내가 길드장일 때만 켠다. 대상의 소속은 API가 확인한다 </summary>
        public static bool CanInviteToGuild(bool canSend, bool hasGuild, EGuildRole myRole)
        {
            return (canSend) && (hasGuild) && (myRole == EGuildRole.Leader);
        }

        /// <summary> 패널에서 보낸 친구 요청, 길드 초대 결과의 시스템 채팅 문구 </summary>
        public static string DescribeSocialResult(in SocialResultMessage result)
        {
            return result.IsAccepted ? SocialViewRules.SuccessText(result.Action) : SocialViewRules.Describe(result.Reason);
        }
    }
}
