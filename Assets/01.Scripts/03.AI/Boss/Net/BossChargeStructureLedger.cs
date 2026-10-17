// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 다른 기록(BossPhaseEffectLedger)과 달리 진행 중인 공격이 아니라 전투 내내 남는 전장 상태다. 배치는 스폰 전에 한 번 받고,
    // 파괴는 서버가 확정한 사건으로만 쌓인다. 시전 종료나 페이즈 전환으로 지우지 않는다.
    // 서버와 클라이언트가 같은 사건으로 같은 기록을 이어 가고, 새 관찰자에게는 스폰 초기 상태에 실어 보낸다.
    // 버퍼는 상한만큼 미리 잡아 사건 처리와 스폰 쓰기에서 할당하지 않는다. 복원 재생만 수신부에 넘길 복사본을 만든다
    /// <summary> 무철 돌진 구조물 배치와 파괴 기록, 늦은 접속 복원에 쓴다 </summary>
    public sealed class BossChargeStructureLedger
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int NOT_FOUND = -1;

        private readonly BossChargeStructureInfo[] _structures =
            new BossChargeStructureInfo[Constants.Network.BOSS_CHARGE_STRUCTURE_MAX_COUNT];
        private readonly bool[] _destroyed = new bool[Constants.Network.BOSS_CHARGE_STRUCTURE_MAX_COUNT];

        private int  _count     = 0;
        private bool _hasLayout = false;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 배치를 받았다, 구조물이 0개인 배치도 받은 것이다 </summary>
        public bool HasLayout => _hasLayout;

        public int Count => _count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 배치와 파괴 여부를 통째로 바꾼다, 상한을 넘거나 배열이 모자라면 바꾸지 않고 거짓이다 </summary>
        public bool SetLayout(in BossChargeStructureSnapshot layout)
        {
            if (!layout.IsWritable)
            {
                return false;
            }

            Clear();
            for (int i = 0; i < layout.Count; ++i)
            {
                _structures[i] = layout.GetStructure(i);
                _destroyed[i]  = layout.IsDestroyed(i);
            }

            _count     = layout.Count;
            _hasLayout = true;
            return true;
        }

        /// <summary> 구조물을 부서진 것으로 남긴다, 배치에 없거나 이미 부서졌으면 거짓이다 </summary>
        public bool MarkDestroyed(int structureId)
        {
            int index = IndexOf(structureId);
            if ((index == NOT_FOUND) || (_destroyed[index]))
            {
                return false;
            }

            _destroyed[index] = true;
            return true;
        }

        public bool IsDestroyed(int structureId)
        {
            int index = IndexOf(structureId);
            return (index != NOT_FOUND) && (_destroyed[index]);
        }

        /// <summary> 배치는 두고 파괴 기록만 지운다 </summary>
        public void ClearDestroyed()
        {
            for (int i = 0; i < _count; ++i)
            {
                _destroyed[i] = false;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _count; ++i)
            {
                _structures[i] = default;
                _destroyed[i]  = false;
            }

            _count     = 0;
            _hasLayout = false;
        }

        // 수신부가 값을 붙잡아도 이 기록이 뒤에 바뀌지 않도록 복사본을 만든다
        /// <summary> 지금 배치와 파괴 여부의 복사본 </summary>
        public BossChargeStructureSnapshot CreateSnapshot()
        {
            var structures = new BossChargeStructureInfo[_count];
            var destroyed  = new bool[_count];
            for (int i = 0; i < _count; ++i)
            {
                structures[i] = _structures[i];
                destroyed[i]  = _destroyed[i];
            }

            return new BossChargeStructureSnapshot(structures, destroyed, _count);
        }

        /// <summary> 배치를 받았으면 수신부에 복원으로 넘긴다 </summary>
        public void Replay(IBossMucheolStateListener listener)
        {
            if ((listener == null) || (!_hasLayout))
            {
                return;
            }

            BossChargeStructureSnapshot snapshot = CreateSnapshot();
            listener.OnChargeStructuresRestored(in snapshot);
        }

        public void Write(NetworkWriter writer)
        {
            writer.WriteBool(_hasLayout);
            if (_hasLayout)
            {
                writer.WriteBossChargeStructureSnapshot(new BossChargeStructureSnapshot(_structures, _destroyed, _count));
            }
        }

        public void Read(NetworkReader reader)
        {
            Clear();
            if (!reader.ReadBool())
            {
                return;
            }

            BossChargeStructureSnapshot layout = reader.ReadBossChargeStructureSnapshot();
            SetLayout(in layout);
        }

        private int IndexOf(int structureId)
        {
            for (int i = 0; i < _count; ++i)
            {
                if (_structures[i].Id == structureId)
                {
                    return i;
                }
            }

            return NOT_FOUND;
        }
    }
}
