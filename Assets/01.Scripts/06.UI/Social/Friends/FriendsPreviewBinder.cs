// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Network;

// 같은 이름의 Contracts DTO(백엔드 응답)와 구분한다
using SocialGuildView = Shinshi.Network.SocialGuildView;

namespace Shinshi.UI
{
    /// <summary> 별도 개발 씬과 화면 캡처에서만 친구 창에 가짜 목록을 넣는다, 서버 상태가 있으면 아무것도 하지 않는다 </summary>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [ExecuteAlways]
#endif
    public sealed class FriendsPreviewBinder : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /****************************************
        *                Fields
        ****************************************/

        private const long PREVIEW_NOW_MS = 1760000000000L;
        private const long MS_PER_DAY     = 24L * 3600L * 1000L;
        private const long MS_PER_HOUR    = 3600L * 1000L;
        private const int  INCOMING_DAYS  = 6;
        private const int  OUTGOING_DAYS  = 2;
        private const int  SOON_HOURS     = 5;

        [SerializeField] private FriendsBinder _binder = null;
        [SerializeField] private int           _tab    = FriendsViewRules.TAB_FRIENDS;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            Show();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 탭을 바꿔 다시 그린다, 화면 캡처 도구가 부른다 </summary>
        public void ShowTab(int tab)
        {
            _tab = tab;
            Show();
        }

        private void Show()
        {
            if ((_binder == null) || (SocialClient.HasSnapshot))
            {
                return;
            }

            _binder.ApplyPreview(BuildSnapshot(), _tab);
        }

        private static SocialSnapshotMessage BuildSnapshot()
        {
            var friends = new[]
            {
                new SocialFriendEntry("f1", "이영희", ContractCodes.PlayerClass.SWORDSMAN, EPresence.Offline, false),
                new SocialFriendEntry("f2", "홍길동", ContractCodes.PlayerClass.ARCHER, EPresence.Town, true),
                new SocialFriendEntry("f3", "김철수", ContractCodes.PlayerClass.MAGEKNIGHT, EPresence.Dungeon, false),
                new SocialFriendEntry("f4", "박서준", ContractCodes.PlayerClass.SWORDSMAN, EPresence.Town, false)
            };
            var incoming = new[]
            {
                new SocialRequestEntry("r1", "박민수", ContractCodes.PlayerClass.SWORDSMAN, PREVIEW_NOW_MS + (INCOMING_DAYS * MS_PER_DAY)),
                new SocialRequestEntry("r2", "정다은", ContractCodes.PlayerClass.ARCHER, PREVIEW_NOW_MS + (SOON_HOURS * MS_PER_HOUR)),
                new SocialRequestEntry("r3", "윤호", ContractCodes.PlayerClass.MAGEKNIGHT, PREVIEW_NOW_MS + MS_PER_DAY)
            };
            var outgoing = new[]
            {
                new SocialRequestEntry("o1", "최지우", ContractCodes.PlayerClass.MAGEKNIGHT, PREVIEW_NOW_MS + (OUTGOING_DAYS * MS_PER_DAY))
            };
            return new SocialSnapshotMessage(PREVIEW_NOW_MS, friends, incoming, outgoing, SocialGuildView.None, null, null, null);
        }
#endif
    }
}
