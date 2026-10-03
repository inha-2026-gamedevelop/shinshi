// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 인증을 통과한 연결과 출정 참가자의 결합, 인증기가 연결의 authenticationData에 넣는다 </summary>
    public sealed class SessionBinding
    {
        // 연결 하나에 하나만 만든다. 연결이 끊기면 ServerSessionRegistry.Release가 비활성으로 바꾼다

        /****************************************
        *                Fields
        ****************************************/

        private readonly string       _participantKey;
        private readonly int          _participantId;
        private readonly string       _accountLeaseId;
        private readonly EPlayerClass _playerClass;
        private readonly string       _characterName;

        /****************************************
        *              Properties
        ****************************************/

        // 토큰이 증명한 영구 참가자 키. 재접속해도 같다
        public string ParticipantKey => _participantKey;

        // 이 서버 프로세스가 키에 붙인 번호, 입력 헤더의 참가자 번호로 쓴다
        public int ParticipantId => _participantId;

        // 마을 계정 접속 임대 ID, 연결이 끊기면 인증기가 해제를 보낸다. 없으면 null
        public string AccountLeaseId => _accountLeaseId;

        // 토큰을 교환한 API가 알려 준 캐릭터 직업, 전투 개체는 이 값으로 만든다. 재접속마다 새 교환에서 다시 받는다
        public EPlayerClass PlayerClass => _playerClass;

        // 머리 위에 띄우는 표시 이름, 없으면 null
        public string CharacterName => _characterName;

        public bool IsActive { get; internal set; }

        /****************************************
        *                Methods
        ****************************************/

        public SessionBinding(string participantKey, int participantId, string accountLeaseId, EPlayerClass playerClass)
            : this(participantKey, participantId, accountLeaseId, playerClass, null)
        {
        }

        public SessionBinding(string participantKey, int participantId, string accountLeaseId, EPlayerClass playerClass,
            string characterName)
        {
            _participantKey = participantKey;
            _participantId  = participantId;
            _accountLeaseId = accountLeaseId;
            _playerClass    = playerClass;
            _characterName  = characterName;
            IsActive        = true;
        }
    }
}
