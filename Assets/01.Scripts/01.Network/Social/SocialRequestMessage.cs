// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 행위 캐릭터는 서버가 인증된 연결의 결합에서 정한다. 메시지에는 대상(이름 또는 스냅샷에서 받은 ID)만 싣는다.
    // 탈퇴는 MembershipId에 내 가입, 추방과 위임은 대상 가입을 싣고 GuildId에 기대 길드를 싣는다.
    // 위임만 ExpectedLeaderVersion에 스냅샷의 길드장 버전을 싣는다. 길드 생성의 RequestId는 결과를 모를 때 같은 값으로 다시 보낸다
    /// <summary> 친구, 길드 관리 요청 한 건. 마을 서버만 받고 결과는 SocialResultMessage로 온다 </summary>
    public readonly struct SocialRequestMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ESocialAction _action;
        private readonly Guid          _requestId;
        private readonly string        _name;
        private readonly string        _targetId;
        private readonly string        _guildId;
        private readonly string        _membershipId;
        private readonly long          _expectedLeaderVersion;

        /****************************************
        *              Properties
        ****************************************/

        public ESocialAction Action                => _action;
        public Guid          RequestId             => _requestId;

        // 대상 캐릭터 이름 또는 길드 이름
        public string        Name                  => _name;

        // 스냅샷에서 받은 대상 캐릭터 ID 또는 길드 ID
        public string        TargetId              => _targetId;
        public string        GuildId               => _guildId;
        public string        MembershipId          => _membershipId;
        public long          ExpectedLeaderVersion => _expectedLeaderVersion;

        /****************************************
        *                Methods
        ****************************************/

        public SocialRequestMessage(ESocialAction action, Guid requestId, string name, string targetId, string guildId,
            string membershipId, long expectedLeaderVersion)
        {
            _action                = action;
            _requestId             = requestId;
            _name                  = name;
            _targetId              = targetId;
            _guildId               = guildId;
            _membershipId          = membershipId;
            _expectedLeaderVersion = expectedLeaderVersion;
        }
    }
}
