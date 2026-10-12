// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 연결 하나에 하나만 만든다. 신원은 인증기가 결합에 넣은 값만 쓰고 클라이언트 메시지로 바꾸지 않는다.
    // 마을 연결이 끊기면 참가자 번호가 사라지므로 다시 들어오면 새 번호의 새 개체가 된다. 계정 ID로 이어지는 상태(채집 선점)만
    // 각 서버가 계정 ID로 다시 붙인다
    /// <summary> 마을에 접속한 참가자 한 명의 신원과 요청 제한 </summary>
    public sealed class TownMember
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int          _participantId;
        private readonly string       _accountId;
        private readonly string       _characterId;
        private readonly string       _accountLeaseId;
        private readonly string       _characterName;
        private readonly EPlayerClass _playerClass;
        private readonly long         _totalExperience;

        private TownRequestThrottle _party;
        private TownRequestThrottle _growth;
        private TownRequestThrottle _life;
        private TownRequestThrottle _market;
        private TownRequestThrottle _trade;

        /****************************************
        *              Properties
        ****************************************/

        public int          ParticipantId  => _participantId;

        // 마을 입장 교환이 준 계정 ID, 개발 토큰이면 null이라 계정이 필요한 요청을 거절한다
        public string       AccountId      => _accountId;
        public string       CharacterId    => _characterId;
        public string       AccountLeaseId => _accountLeaseId;
        public string       CharacterName  => _characterName;
        public EPlayerClass PlayerClass    => _playerClass;

        // 마을에서는 경험치를 얻지 않는다. 재입장은 새 교환·새 개체로 최신 값을 다시 받는다
        public long         TotalExperience => _totalExperience;

        // API 요청에 필요한 신원(계정, 캐릭터, 계정 임대)이 모두 있는지
        public bool HasBackendIdentity => (!string.IsNullOrEmpty(_accountId)) && (!string.IsNullOrEmpty(_characterId))
            && (!string.IsNullOrEmpty(_accountLeaseId));

        /****************************************
        *                Methods
        ****************************************/

        public TownMember(int participantId, string accountId, string characterId, string accountLeaseId, string characterName,
            EPlayerClass playerClass, long totalExperience = -1)
        {
            _participantId   = participantId;
            _accountId       = accountId;
            _characterId     = characterId;
            _accountLeaseId  = accountLeaseId;
            _characterName   = characterName;
            _playerClass     = playerClass;
            _totalExperience = totalExperience;
        }

        /// <summary> 인증된 결합에서 만든다 </summary>
        public static TownMember FromBinding(SessionBinding binding)
        {
            return new TownMember(binding.ParticipantId, binding.AccountId, binding.CharacterId, binding.AccountLeaseId,
                binding.CharacterName, binding.PlayerClass, binding.Experience?.TotalExperience ?? -1);
        }

        /// <summary> 종류별 1초 창 안의 요청 수가 상한보다 적으면 하나를 세고 참 </summary>
        public bool TryAcceptRequest(ETownChannel channel, double now)
        {
            switch (channel)
            {
                case ETownChannel.Party:
                    return _party.TryAccept(now, Constants.Town.REQUESTS_PER_SECOND, Constants.Town.REQUEST_WINDOW_SECONDS);
                case ETownChannel.Growth:
                    return _growth.TryAccept(now, Constants.Town.REQUESTS_PER_SECOND, Constants.Town.REQUEST_WINDOW_SECONDS);
                case ETownChannel.Life:
                    return _life.TryAccept(now, Constants.Town.REQUESTS_PER_SECOND, Constants.Town.REQUEST_WINDOW_SECONDS);
                case ETownChannel.Market:
                    return _market.TryAccept(now, Constants.Town.REQUESTS_PER_SECOND, Constants.Town.REQUEST_WINDOW_SECONDS);
                case ETownChannel.Trade:
                    return _trade.TryAccept(now, Constants.Town.REQUESTS_PER_SECOND, Constants.Town.REQUEST_WINDOW_SECONDS);
                default:
                    return false;
            }
        }
    }
}
