// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 출정 종료 정산안이 만들어진 뒤 그 참가자에게만 한 번, (재)접속 때 다시 보낸다. 지급 확정은 백엔드 저장이 정하고
    // 이 메시지는 서버가 정산 요청에 담은 값을 화면에 보여 주기 위한 사본이다
    /// <summary> 이 참가자의 출정 정산 보상 내역 </summary>
    public readonly struct SortieRewardMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly SortieRewardLine[] G_NoLines = Array.Empty<SortieRewardLine>();

        private readonly long               _baseEssence;
        private readonly long               _bossEssence;
        private readonly long               _paidExtraEssence;
        private readonly long               _lostExtraEssence;
        private readonly long               _essence;
        private readonly long               _refineShards;
        private readonly long               _tickets;
        private readonly long               _currency;
        private readonly SortieRewardLine[] _items;
        private readonly SortieRewardLine[] _talismans;

        /****************************************
        *              Properties
        ****************************************/

        // 완료 웨이브 확보 기본 정기
        public long               BaseEssence      => _baseEssence;
        public long               BossEssence      => _bossEssence;

        // 적립한 추가 정기 중 지급률을 곱해 받은 양과 실패, 포기로 잃은 양
        public long               PaidExtraEssence => _paidExtraEssence;
        public long               LostExtraEssence => _lostExtraEssence;

        // 정산 상한을 적용한 최종 정기
        public long               Essence          => _essence;
        public long               RefineShards     => _refineShards;
        public long               Tickets          => _tickets;
        public long               Currency         => _currency;
        public SortieRewardLine[] Items            => _items ?? G_NoLines;
        public SortieRewardLine[] Talismans        => _talismans ?? G_NoLines;

        /****************************************
        *                Methods
        ****************************************/

        public SortieRewardMessage(long baseEssence, long bossEssence, long paidExtraEssence, long lostExtraEssence,
            long essence, long refineShards, long tickets, long currency, SortieRewardLine[] items,
            SortieRewardLine[] talismans)
        {
            _baseEssence      = baseEssence;
            _bossEssence      = bossEssence;
            _paidExtraEssence = paidExtraEssence;
            _lostExtraEssence = lostExtraEssence;
            _essence          = essence;
            _refineShards     = refineShards;
            _tickets          = tickets;
            _currency         = currency;
            _items            = items;
            _talismans        = talismans;
        }
    }
}
