namespace Shinshi.Common
{
    /// <summary> 로컬 플레이어의 이산 상태 스냅샷, 서버가 확정해 복제한 값만 담는다 </summary>
    public readonly struct LocalPlayerSnapshot
    {
        /****************************************
        *              Constants
        ****************************************/

        // 아직 캐릭터에 묶이지 않은 상태의 바인딩 번호
        public const int UNBOUND_VERSION = 0;

        /****************************************
        *                Fields
        ****************************************/

        private readonly int                   _bindingVersion;
        private readonly EPlayerClass          _playerClass;
        private readonly EParticipantLifeState _lifeState;
        private readonly long                  _characterRevision;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 캐릭터가 아직 스폰되지 않았거나 연결이 끊긴 동안의 빈 스냅샷, IsBound가 false면 나머지 값은 의미가 없다 </summary>
        public static LocalPlayerSnapshot Unbound => new LocalPlayerSnapshot(UNBOUND_VERSION, default, default,
            OperationReceipt.NO_REVISION);

        public bool IsBound => _bindingVersion != UNBOUND_VERSION;

        // 재접속이나 캐릭터 교체로 다른 인스턴스에 다시 묶일 때마다 바뀐다. 구현은 1부터 올린다.
        // 같은 캐릭터의 사망과 부활은 인스턴스가 그대로라 바뀌지 않는다
        public int BindingVersion => _bindingVersion;

        public EPlayerClass          PlayerClass => _playerClass;
        public EParticipantLifeState LifeState   => _lifeState;

        // 마지막으로 받은 확정 revision. 성장 요청의 기대 revision으로 보내고, 모르면 OperationReceipt.NO_REVISION
        public long CharacterRevision => _characterRevision;

        /****************************************
        *                Methods
        ****************************************/

        public LocalPlayerSnapshot(int bindingVersion, EPlayerClass playerClass, EParticipantLifeState lifeState,
            long characterRevision)
        {
            _bindingVersion    = bindingVersion;
            _playerClass       = playerClass;
            _lifeState         = lifeState;
            _characterRevision = characterRevision;
        }
    }
}
