// 프로젝트
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>서버가 돌려준 거절 사유를 화면에 보일 한 줄 안내로 바꾼다. 거절 사유가 아닌 값은 빈 글자다</summary>
    public static class RejectText
    {
        /****************************************
        *                Methods
        ****************************************/

        public static string Describe(ERejectReason reason)
        {
            switch (reason)
            {
                case ERejectReason.Dead:
                    return "쓰러진 상태에서는 사용할 수 없습니다";
                case ERejectReason.WrongPhase:
                    return "지금은 사용할 수 없습니다";
                case ERejectReason.Cooldown:
                    return "아직 사용할 수 없습니다";
                case ERejectReason.InsufficientEnergy:
                    return "기력이 부족합니다";
                case ERejectReason.InvalidTarget:
                    return "대상을 지정할 수 없습니다";
                case ERejectReason.FullHealth:
                    return "체력이 가득 찼습니다";
                case ERejectReason.Disabled:
                    return "지금은 물약을 쓸 수 없습니다";
                case ERejectReason.PotionLimit:
                    return "물약 사용 횟수를 모두 썼습니다";
                case ERejectReason.PendingCommit:
                    return "물약 사용을 처리 중입니다";
                case ERejectReason.TooEarly:
                    return "아직 부활할 수 없습니다";
                case ERejectReason.NoSafePoint:
                    return "안전한 부활 지점을 찾는 중입니다";
                case ERejectReason.NoRevives:
                    return "남은 부활 횟수가 없습니다";
                case ERejectReason.SessionTerminal:
                    return "전투가 이미 끝났습니다";
                case ERejectReason.Locked:
                    return "잠겨 있습니다";
                case ERejectReason.MissingUnlock:
                    return "해금하지 않은 전장입니다";
                case ERejectReason.InsufficientTicket:
                    return "보스 입장권이 부족합니다";
                case ERejectReason.StaleRevision:
                    return "구성이 바뀌었습니다. 다시 확인해 주세요";
                case ERejectReason.NotLeader:
                    return "방장만 시작할 수 있습니다";
                case ERejectReason.NotAllReady:
                    return "모든 참가자가 준비해야 합니다";
                case ERejectReason.NoCapacity:
                    return "자리가 없습니다";
                case ERejectReason.ReservationConflict:
                    return "예약이 겹쳤습니다. 잠시 뒤 다시 시도해 주세요";
                default:
                    return string.Empty;
            }
        }
    }
}
