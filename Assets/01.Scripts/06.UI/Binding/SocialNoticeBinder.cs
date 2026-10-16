// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.UI
{
    // 알림 팝업은 1차에 만들지 않는다. 받은 알림은 시스템 채팅 한 줄로, 받은 요청이 있으면 HUD 메뉴 버튼의 빨간 점으로 알린다.
    // 창을 한 번도 열지 않아도 점이 맞도록 마을에 접속할 때마다 목록을 한 번 받는다
    /// <summary> 소셜 알림을 채팅 시스템 줄로 넣고 HUD 메뉴의 친구, 길드 빨간 점을 켜고 끈다 </summary>
    public sealed class SocialNoticeBinder : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private GameObject _friendBadge = null;
        [SerializeField] private GameObject _guildBadge  = null;

        private bool _requestedForConnection = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            SocialClient.NoticeReceived  += OnNotice;
            SocialClient.SnapshotChanged += OnSnapshot;
            SocialClient.StateChanged    += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            SocialClient.NoticeReceived  -= OnNotice;
            SocialClient.SnapshotChanged -= OnSnapshot;
            SocialClient.StateChanged    -= Refresh;
        }

        private void Update()
        {
            if (!NetworkClient.isConnected)
            {
                _requestedForConnection = false;
                return;
            }

            if ((_requestedForConnection) || (!SocialClient.IsTownConnection()))
            {
                return;
            }

            _requestedForConnection = (SocialClient.HasSnapshot) || (SocialClient.RequestSnapshot());
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnNotice(SocialNoticeMessage notice)
        {
            ChatLog.AddSystem(SocialViewRules.DescribeNotice(notice.Kind, notice.ActorName, notice.GuildName));
        }

        private void OnSnapshot(SocialSnapshotMessage snapshot)
        {
            Refresh();
        }

        private void Refresh()
        {
            SetActive(_friendBadge, SocialClient.FriendBadgeCount > 0);
            SetActive(_guildBadge, SocialClient.GuildBadgeCount > 0);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if ((target != null) && (target.activeSelf != active))
            {
                target.SetActive(active);
            }
        }
    }
}
