// System
using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 출정 중 참가자별로 확보한 보상을 쌓는다, 인덱스는 명단 순서다. 서버 메인 스레드에서만 쓴다 </summary>
    public sealed class SettlementLedger
    {
        // 처치와 웨이브 완료는 틱 경로라 모든 칸을 생성자와 시작 전 등록에서 만들고, 이후에는 값만 더한다.
        // 추가 정기는 ESSENCE_ACCRUED, 완료 웨이브 기본 정기는 BASE_ESSENCE_ACCRUED로 보낸 양과 같은 값을 여기에 쌓는다.
        // 정산안의 기본, 추가 정기는 이 값에서만 계산하므로 적립 합계와 정산 기준, API 장애 취소 기준이 어긋나지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private readonly long[]     _baseEssence;
        private readonly long[]     _extraEssence;
        private readonly long[]     _refineShards;
        private readonly long[]     _tickets;
        private readonly long[]     _bossEssence;
        private readonly long[]     _bossMaterials;
        private readonly long[]     _bossCurrency;
        private readonly bool[]     _accrualClamped;
        private readonly string[][] _effects;
        private readonly long[][]   _talismans;

        /****************************************
        *              Properties
        ****************************************/

        public int Capacity => _baseEssence.Length;

        /****************************************
        *                Methods
        ****************************************/

        public SettlementLedger(int capacity = Constants.Session.MAX_PARTY_SIZE)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _baseEssence    = new long[capacity];
            _extraEssence   = new long[capacity];
            _refineShards   = new long[capacity];
            _tickets        = new long[capacity];
            _bossEssence    = new long[capacity];
            _bossMaterials  = new long[capacity];
            _bossCurrency   = new long[capacity];
            _accrualClamped = new bool[capacity];
            _effects        = new string[capacity][];
            _talismans      = new long[capacity][];
        }

        /// <summary> 시작 전에 부른다, effects는 정렬을 마친 자기 직업 효과 ID이고 순서가 부적 정산 순서다 </summary>
        public void SetEffects(int index, string[] effects)
        {
            if (effects == null)
            {
                throw new ArgumentNullException(nameof(effects));
            }

            _effects[index]   = effects;
            _talismans[index] = new long[effects.Length];
        }

        /// <summary> 추가 정기를 출정 상한까지만 쌓고 실제로 쌓은 양을 돌려준다, 이 값을 그대로 적립 이벤트로 보낸다 </summary>
        public long AccrueExtra(int index, long amount)
        {
            return AccrueClamped(_extraEssence, index, amount);
        }

        /// <summary> 완료 웨이브의 확보 기본 정기와 정련편, 실패해도 보존한다. 기본 정기는 출정 상한까지만 쌓고 쌓은 양을 돌려준다,
        /// 이 값을 그대로 기본 정기 적립 이벤트로 보낸다 </summary>
        public long AddWaveBase(int index, long essence, long shards)
        {
            if (shards > 0)
            {
                _refineShards[index] += shards;
            }

            return AccrueClamped(_baseEssence, index, essence);
        }

        /// <summary> 처치 판정의 티켓과 부적을 확보 목록에 더한다, 추가 정기는 AccrueExtra가 따로 쌓는다 </summary>
        public void AddLoot(int index, in LootRollResult loot)
        {
            _tickets[index] += loot.TicketQuantity;
            if ((loot.TalismanQuantity <= 0) || (loot.TalismanEffectId == null))
            {
                return;
            }

            string[] effects = _effects[index];
            if (effects == null)
            {
                return;
            }

            for (int i = 0; i < effects.Length; ++i)
            {
                if (string.Equals(effects[i], loot.TalismanEffectId, StringComparison.Ordinal))
                {
                    _talismans[index][i] += loot.TalismanQuantity;
                    return;
                }
            }
        }

        /// <summary> 보스 처치 성공 때 한 번 부른다, currency는 확정 엽전이다 </summary>
        public void SetBossReward(int index, long essence, long materials, long currency)
        {
            _bossEssence[index]   = essence;
            _bossMaterials[index] = materials;
            _bossCurrency[index]  = currency;
        }

        public long GetBaseEssence(int index)   { return _baseEssence[index]; }
        public long GetExtraEssence(int index)  { return _extraEssence[index]; }
        public long GetRefineShards(int index)  { return _refineShards[index]; }
        public long GetTickets(int index)       { return _tickets[index]; }
        public long GetBossEssence(int index)   { return _bossEssence[index]; }
        public long GetBossMaterials(int index) { return _bossMaterials[index]; }
        public long GetBossCurrency(int index)  { return _bossCurrency[index]; }
        public bool WasAccrualClamped(int index) { return _accrualClamped[index]; }

        /// <summary> 부적 효과 종류 수, 직업 효과를 등록하지 않았으면 0 </summary>
        public int GetEffectCount(int index)
        {
            return (_effects[index] != null) ? _effects[index].Length : 0;
        }

        public string GetEffectId(int index, int effectIndex) { return _effects[index][effectIndex]; }

        public long GetTalismans(int index, int effectIndex) { return _talismans[index][effectIndex]; }

        // 상한을 넘는 적립은 API가 배치 전체를 확정 거절해 출정이 장애 취소로 끝난다. 남은 한도만 받고 넘친 양은 버린다
        private long AccrueClamped(long[] totals, int index, long amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            long room = Constants.Session.SETTLEMENT_MAX_SESSION_ESSENCE - totals[index];
            long accepted = (amount > room) ? room : amount;
            if (accepted < amount)
            {
                _accrualClamped[index] = true;
            }

            if (accepted <= 0)
            {
                return 0;
            }

            totals[index] += accepted;
            return accepted;
        }
    }
}
