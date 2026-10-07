// 프로젝트
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>출정 준비 화면 파티 칸 하나가 표시할 값이며 서버 값을 화면용으로 옮긴 사본이다. 기본값은 빈 칸이다</summary>
    public readonly struct SortieMemberData
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string       _name;
        private readonly EPlayerClass _playerClass;
        private readonly bool         _isLeader;
        private readonly bool         _isReady;
        private readonly bool         _isConnected;
        private readonly bool         _meetsRequirements;
        private readonly int          _level;
        private readonly int          _combatPower;

        /****************************************
        *              Properties
        ****************************************/

        public string       Name        => _name;
        public EPlayerClass PlayerClass => _playerClass;
        public bool         IsLeader    => _isLeader;
        public bool         IsReady     => _isReady;
        public bool         IsConnected => _isConnected;
        public bool         IsEmpty     => string.IsNullOrEmpty(_name);

        // 0이면 아직 받지 못한 값이다
        public int Level       => _level;
        public int CombatPower => _combatPower;

        // 해금, 입장권, 지참 물약 조건을 모두 갖췄는지
        public bool MeetsRequirements => _meetsRequirements;

        /****************************************
        *                Methods
        ****************************************/

        public SortieMemberData(string name, EPlayerClass playerClass, bool isLeader, bool isReady,
            bool isConnected, bool meetsRequirements, int level, int combatPower)
        {
            _name              = name;
            _playerClass       = playerClass;
            _isLeader          = isLeader;
            _isReady           = isReady;
            _isConnected       = isConnected;
            _meetsRequirements = meetsRequirements;
            _level             = level;
            _combatPower       = combatPower;
        }
    }
}
