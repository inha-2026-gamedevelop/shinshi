// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 서버가 보스 스폰 전에 정한 구조물 배치와 지금까지 확정한 파괴 여부다. 늦은 접속과 재접속 복원에만 쓰고 Rpc로는 보내지 않는다.
    // 배열은 Count까지만 의미가 있다. 받은 쪽은 배열을 고치지 않는다
    /// <summary> 무철 돌진 구조물 배치와 파괴 여부 복원 값 </summary>
    public readonly struct BossChargeStructureSnapshot
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly BossChargeStructureInfo[] _structures;
        private readonly bool[]                    _destroyed;
        private readonly int                       _count;

        /****************************************
        *              Properties
        ****************************************/

        public int Count => _count;

        /// <summary> 배열이 Count만큼 있고 Count가 0 이상 상한 이하다, 아니면 서버가 싣지 않는다 </summary>
        public bool IsWritable
        {
            get
            {
                return (_count >= 0) && (_count <= Constants.Network.BOSS_CHARGE_STRUCTURE_MAX_COUNT)
                    && (_structures != null) && (_destroyed != null)
                    && (_structures.Length >= _count) && (_destroyed.Length >= _count);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public BossChargeStructureSnapshot(BossChargeStructureInfo[] structures, bool[] destroyed, int count)
        {
            _structures = structures;
            _destroyed  = destroyed;
            _count      = count;
        }

        /// <summary> index번째 구조물 </summary>
        public BossChargeStructureInfo GetStructure(int index)
        {
            return _structures[index];
        }

        /// <summary> index번째 구조물이 부서졌다 </summary>
        public bool IsDestroyed(int index)
        {
            return _destroyed[index];
        }
    }
}
