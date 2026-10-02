// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 계정 상세(Viewer, 감사 기록). 한 읽기 스냅샷에서 모은 값이며 비밀값은 싣지 않는다 </summary>
    [DataContract]
    public sealed class AdminAccountDetailResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "accountId")]           private string                    _accountId;
        [DataMember(Name = "createdAtUnixMs")]     private long                      _createdAtUnixMs;
        [DataMember(Name = "revision")]            private long                      _revision;

        // 지갑 행이 아직 없으면 null
        [DataMember(Name = "wallet")]              private AdminWalletView           _wallet;
        [DataMember(Name = "characters")]          private AdminCharacterView[]      _characters;
        [DataMember(Name = "items")]               private AdminAccountItemView[]    _items;

        // 게임 서버가 쥔 계정 임대. 없으면 null
        [DataMember(Name = "lease")]               private AdminAccountLeaseView     _lease;

        // 최근 출정(생성 시각 내림차순, 최대 20건)
        [DataMember(Name = "recentSessions")]      private AdminAccountSessionView[] _recentSessions;

        // 폐기되지 않고 쓸 수 있는 갱신 토큰이 남은 로그인 묶음 수
        [DataMember(Name = "activeTokenFamilies")] private int                       _activeTokenFamilies;

        // 최근 초대(만료 시각 내림차순, 최대 50건)
        [DataMember(Name = "invites")]             private AdminInviteView[]         _invites;

        /****************************************
        *              Properties
        ****************************************/

        public string                    AccountId           => _accountId;
        public long                      CreatedAtUnixMs     => _createdAtUnixMs;
        public long                      Revision            => _revision;
        public AdminWalletView           Wallet              => _wallet;
        public AdminCharacterView[]      Characters          => _characters;
        public AdminAccountItemView[]    Items               => _items;
        public AdminAccountLeaseView     Lease               => _lease;
        public AdminAccountSessionView[] RecentSessions      => _recentSessions;
        public int                       ActiveTokenFamilies => _activeTokenFamilies;
        public AdminInviteView[]         Invites             => _invites;

        /****************************************
        *                Methods
        ****************************************/

        public AdminAccountDetailResponse(string accountId, long createdAtUnixMs, long revision, AdminWalletView wallet,
            AdminCharacterView[] characters, AdminAccountItemView[] items, AdminAccountLeaseView lease,
            AdminAccountSessionView[] recentSessions, int activeTokenFamilies, AdminInviteView[] invites)
        {
            _accountId           = accountId;
            _createdAtUnixMs     = createdAtUnixMs;
            _revision            = revision;
            _wallet              = wallet;
            _characters          = characters;
            _items               = items;
            _lease               = lease;
            _recentSessions      = recentSessions;
            _activeTokenFamilies = activeTokenFamilies;
            _invites             = invites;
        }

        private AdminAccountDetailResponse() { }
    }
}
