// System
using System.Globalization;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 마을 서버의 거절, 기술 사용 거절과 파티 초대를 알림과 확인 창으로 보여 준다 </summary>
    public sealed class NetworkFeedbackBinder : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string INVITE_FORMAT = "{0}님이 '{1}' 파티에 초대했다. 들어갈까?";

        [SerializeField] private UiPopupHub            _popups      = null;
        [SerializeField] private HudRejectFeedbackView _skillReject = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            TownPartyClient.ResultReceived  += OnPartyResult;
            TownPartyClient.InviteReceived  += OnInvite;
            TownGrowthClient.ResultReceived += OnGrowthResult;
            TownLifeClient.ResultReceived   += OnLifeResult;
            LocalPlayerView.Current.SkillRejected += OnSkillRejected;
        }

        private void OnDisable()
        {
            TownPartyClient.ResultReceived  -= OnPartyResult;
            TownPartyClient.InviteReceived  -= OnInvite;
            TownGrowthClient.ResultReceived -= OnGrowthResult;
            TownLifeClient.ResultReceived   -= OnLifeResult;
            LocalPlayerView.Current.SkillRejected -= OnSkillRejected;
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnPartyResult(PartyResultMessage result)
        {
            ShowReject(result.Reason);
        }

        private void OnGrowthResult(GrowthResultMessage result)
        {
            ShowReject(result.Reason);
        }

        private void OnLifeResult(LifeResultMessage result)
        {
            ShowReject(result.Reason);
        }

        private void OnSkillRejected(ESkillRequestKind kind, byte slot, ERejectReason reason)
        {
            if (_skillReject != null)
            {
                _skillReject.Show(reason);
            }
        }

        private void ShowReject(ERejectReason reason)
        {
            if ((reason == ERejectReason.None) || (_popups == null))
            {
                return;
            }

            _popups.ShowToast(RejectText.Describe(reason));
        }

        private void OnInvite(PartyInviteMessage invite)
        {
            if (_popups == null)
            {
                return;
            }

            int    roomId  = invite.RoomId;
            string message = string.Format(CultureInfo.InvariantCulture, INVITE_FORMAT, invite.InviterName, invite.RoomName);
            _popups.ShowConfirm(EPopupSize.Medium, message, accepted =>
            {
                if (accepted)
                {
                    TownPartyClient.AcceptInvite(roomId);
                }
                else
                {
                    TownPartyClient.DeclineInvite(roomId);
                }
            });
        }
    }
}
