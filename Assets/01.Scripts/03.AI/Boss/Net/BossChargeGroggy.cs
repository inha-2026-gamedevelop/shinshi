namespace Shinshi.AI
{
    // 서버 기믹(MucheolChargeGimmick)이 정한 그로기 구간을 표현용 값으로 옮겼다. 그로기 판정과 행동 가능 시각은 서버만 정한다.
    // 대기 사건에서는 시작과 끝을 아직 모르므로 NaN이고, 시작 사건부터 둘 다 채워진다
    /// <summary> 무철 기믹 1 마지막 구조물 파괴 뒤의 그로기 구간 </summary>
    public readonly struct BossChargeGroggy
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int    _bossId;
        private readonly int    _castId;
        private readonly int    _structureId;
        private readonly double _collisionServerTime;
        private readonly double _groggyStartServerTime;
        private readonly double _groggyEndServerTime;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 보스의 전투 번호(CombatId) </summary>
        public int BossId => _bossId;

        /// <summary> 마지막 구조물을 부순 돌진의 시전 번호, 모르면 0이다 </summary>
        public int CastId => _castId;

        /// <summary> 마지막으로 파괴가 확인된 구조물의 식별자 </summary>
        public int StructureId => _structureId;

        /// <summary> 그 충돌의 서버 시각, 돌진 결과를 모르면 파괴를 확인한 시각이다 </summary>
        public double CollisionServerTime => _collisionServerTime;

        /// <summary> 그로기 시작 서버 시각, 아직 모르면 NaN이다 </summary>
        public double GroggyStartServerTime => _groggyStartServerTime;

        /// <summary> 그로기 끝 서버 시각(끝 경계 미포함), 아직 모르면 NaN이다 </summary>
        public double GroggyEndServerTime => _groggyEndServerTime;

        /// <summary> 시작과 끝이 정해졌다 </summary>
        public bool HasStarted => !double.IsNaN(_groggyEndServerTime);

        /****************************************
        *                Methods
        ****************************************/

        public BossChargeGroggy(int bossId, int castId, int structureId, double collisionServerTime, double groggyStartServerTime,
            double groggyEndServerTime)
        {
            _bossId                = bossId;
            _castId                = castId;
            _structureId           = structureId;
            _collisionServerTime   = collisionServerTime;
            _groggyStartServerTime = groggyStartServerTime;
            _groggyEndServerTime   = groggyEndServerTime;
        }
    }
}
