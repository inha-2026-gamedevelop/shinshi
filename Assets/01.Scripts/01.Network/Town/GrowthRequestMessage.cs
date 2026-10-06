// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 대상 캐릭터는 서버가 인증된 연결로 정하고, 확률 판정 값과 기대 revision도 서버가 넣는다. 클라이언트는 의도만 보낸다.
    // OperationId는 클라이언트가 요청마다 새로 만들고, 응답을 잃어 다시 보낼 때만 같은 값을 쓴다.
    // 종류별 인자: 부적 등록, 강화 targetId=효과 / 분해 targetId=효과, intArg=수량 / 조합 loadout /
    // 수련 intArg=항목(ETrainingStat), intArg2=단계 / 재분배 allocation / 제작 targetId=장비 정의 /
    // 강화, 착용, 해제, 분해 targetId=장비 ID / 잠금 targetId=장비 ID, flag=잠금 / 계승 targetId=원본, secondaryId=대상
    /// <summary> 클라이언트의 마을 성장 요청, 결과는 GrowthResultMessage와 GrowthSnapshotMessage로 온다 </summary>
    public readonly struct GrowthRequestMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Guid               _operationId;
        private readonly EGrowthAction      _action;
        private readonly string             _targetId;
        private readonly string             _secondaryId;
        private readonly int                _intArg;
        private readonly int                _intArg2;
        private readonly bool               _flag;
        private readonly int[]              _allocation;
        private readonly TownLoadoutEntry[] _loadout;

        /****************************************
        *              Properties
        ****************************************/

        public Guid               OperationId => _operationId;
        public EGrowthAction      Action      => _action;
        public string             TargetId    => _targetId;
        public string             SecondaryId => _secondaryId;
        public int                IntArg      => _intArg;
        public int                IntArg2     => _intArg2;
        public bool               Flag        => _flag;
        public int[]              Allocation  => _allocation ?? Array.Empty<int>();
        public TownLoadoutEntry[] Loadout     => _loadout ?? Array.Empty<TownLoadoutEntry>();

        /****************************************
        *                Methods
        ****************************************/

        public GrowthRequestMessage(Guid operationId, EGrowthAction action, string targetId, string secondaryId, int intArg,
            int intArg2, bool flag, int[] allocation, TownLoadoutEntry[] loadout)
        {
            _operationId = operationId;
            _action      = action;
            _targetId    = targetId;
            _secondaryId = secondaryId;
            _intArg      = intArg;
            _intArg2     = intArg2;
            _flag        = flag;
            _allocation  = allocation;
            _loadout     = loadout;
        }
    }
}
