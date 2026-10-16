// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Network;

// 같은 이름의 Contracts DTO(백엔드 응답)와 구분한다
using SocialGuildView = Shinshi.Network.SocialGuildView;

namespace Shinshi.UI
{
    /// <summary> 별도 개발 씬과 화면 캡처에서만 길드 창에 가짜 길드 상태를 넣는다, 서버 상태가 있으면 아무것도 하지 않는다 </summary>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [ExecuteAlways]
#endif
    public sealed class GuildPreviewBinder : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /****************************************
        *                Fields
        ****************************************/

        private const long   PREVIEW_NOW_MS   = 1760000000000L;
        private const long   MS_PER_DAY       = 24L * 3600L * 1000L;
        private const int    APPLY_DAYS       = 6;
        private const int    INVITE_DAYS      = 3;
        private const long   LEADER_VERSION   = 2;
        private const string MY_MEMBERSHIP    = "m1";

        [SerializeField] private GuildBinder _binder  = null;
        [SerializeField] private bool        _inGuild = true;
        [SerializeField] private int         _tab     = GuildViewRules.TAB_MEMBERS;

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

        /// <summary> 길드 있음, 없음과 탭을 바꿔 다시 그린다, 화면 캡처 도구가 부른다 </summary>
        public void ShowState(bool inGuild, int tab)
        {
            _inGuild = inGuild;
            _tab     = tab;
            Show();
        }

        private void Show()
        {
            if ((_binder == null) || (SocialClient.HasSnapshot))
            {
                return;
            }

            _binder.ApplyPreview(_inGuild ? BuildGuild() : BuildNoGuild(), _tab);
        }

        private static SocialSnapshotMessage BuildGuild()
        {
            var members = new[]
            {
                new SocialGuildMemberEntry("c2", "m2", "김철수", ContractCodes.PlayerClass.MAGEKNIGHT, EGuildRole.Member, EPresence.Dungeon, false),
                new SocialGuildMemberEntry("c1", MY_MEMBERSHIP, "홍길동", ContractCodes.PlayerClass.ARCHER, EGuildRole.Leader, EPresence.Town, true),
                new SocialGuildMemberEntry("c3", "m3", "이영희", ContractCodes.PlayerClass.SWORDSMAN, EGuildRole.Member, EPresence.Offline, false),
                new SocialGuildMemberEntry("c4", "m4", "박서준", ContractCodes.PlayerClass.SWORDSMAN, EGuildRole.Member, EPresence.Town, true)
            };
            var applications = new[]
            {
                new SocialRequestEntry("a1", "박민수", ContractCodes.PlayerClass.SWORDSMAN, PREVIEW_NOW_MS + (APPLY_DAYS * MS_PER_DAY))
            };
            var guild = new SocialGuildView("g1", "청룡단", members.Length, EGuildRole.Leader, MY_MEMBERSHIP, LEADER_VERSION);
            return new SocialSnapshotMessage(PREVIEW_NOW_MS, null, null, null, guild, members, applications, null);
        }

        private static SocialSnapshotMessage BuildNoGuild()
        {
            var invites = new[]
            {
                new SocialGuildInviteEntry("g2", "백호회", "최지우", PREVIEW_NOW_MS + (INVITE_DAYS * MS_PER_DAY)),
                new SocialGuildInviteEntry("g3", "주작", string.Empty, PREVIEW_NOW_MS + MS_PER_DAY)
            };
            return new SocialSnapshotMessage(PREVIEW_NOW_MS, null, null, null, SocialGuildView.None, null, null, invites);
        }
#endif
    }
}
