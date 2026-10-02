// System
using System;
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 참가자 한 명의 정산안, 지급률을 이미 적용한 결과다 </summary>
    [DataContract]
    public sealed class SettlementGrant
    {
        // 지갑 자산(정련편, 티켓, 엽전)은 아이템 줄로 보내지 않고 아래 전용 칸으로 보낸다. 아이템 줄의 지급 금지 ID
        // (item_essence, item_refine_shard, item_boss_ticket, item_yeopjeon)는 그대로 거절된다.
        // 새 칸이 없는 이전 서버의 정산안은 0과 빈 배열로 읽혀 지급하지 않으므로 기존 정산 결과가 바뀌지 않는다

        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "participantId")] private string                _participantId;
        [DataMember(Name = "essence")]       private long                  _essence;

        // 보호 전리품. 미사용 물약 반환은 API가 예약에서 계산하므로 넣지 않는다
        [DataMember(Name = "items")]         private SettlementItemGrant[] _items;

        // 정련편, 캐릭터 귀속(characters.shards). 완료 웨이브 확보분이라 결과와 무관하게 보존한다
        [DataMember(Name = "refineShards")]  private long                  _refineShards;

        // 공용 보스 티켓, 계정 지갑(account_wallets.tickets). 처치 판정 확보분이다
        [DataMember(Name = "tickets")]       private long                  _tickets;

        // 엽전, 계정 지갑(account_wallets.currency). 보스 처치 성공의 확정 기본 보상에서만 발행한다
        [DataMember(Name = "currency")]      private long                  _currency;

        // 미등록 부적 카드, 계정 귀속 인벤토리(account_items)에 효과 ID별로 쌓인다.
        // 형식: definitionId = TalismanCatalogSO 효과 ID(T 두 자리 숫자 - 단계 숫자 선택 대문자, 예 T01-1A),
        // bindingKind = "Account"(사용자 결정 2026-10-01), quantity = 1 이상. 같은 효과 ID는 한 줄로 합친다
        [DataMember(Name = "talismans")]     private SettlementItemGrant[] _talismans;

        /****************************************
        *              Properties
        ****************************************/

        public string                ParticipantId => _participantId;
        public long                  Essence       => _essence;
        public SettlementItemGrant[] Items         => _items;
        public long                  RefineShards  => _refineShards;
        public long                  Tickets       => _tickets;
        public long                  Currency      => _currency;
        public SettlementItemGrant[] Talismans     => _talismans;

        /****************************************
        *                Methods
        ****************************************/

        public SettlementGrant(string participantId, long essence, SettlementItemGrant[] items)
            : this(participantId, essence, items, 0L, 0L, 0L, Array.Empty<SettlementItemGrant>())
        {
        }

        public SettlementGrant(string participantId, long essence, SettlementItemGrant[] items, long refineShards,
            long tickets, long currency, SettlementItemGrant[] talismans)
        {
            _participantId = participantId;
            _essence       = essence;
            _items         = items;
            _refineShards  = refineShards;
            _tickets       = tickets;
            _currency      = currency;
            _talismans     = talismans;
        }

        private SettlementGrant() { }
    }
}
