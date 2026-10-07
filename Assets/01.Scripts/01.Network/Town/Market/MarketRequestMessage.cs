// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 계정, 캐릭터 자리가 없다. 신원은 마을 서버가 연결에서 정한다. RequestId는 응답을 짝짓는 값이고, 쓰기(List, Buy, Cancel, Ack)에서는
    // API operationId이기도 하다. 결과를 못 받으면 같은 RequestId로 다시 보내면 효과가 한 번이다.
    // 종류별 인자: Search category/classId/grade(NONE이면 조건 없음), definitionIds, sort, order, page / Listings definitionId, page /
    // Mine 없음 / History page / List assetKind, definitionId, itemId(장비만), quantity, unitPrice /
    // Buy listingId, quantity, unitPrice(확인한 개당 가격) / Cancel listingId / Ack listingIds
    /// <summary> 클라이언트의 유저 경매장 요청, 결과는 MarketResultMessage로 온다 </summary>
    public readonly struct MarketRequestMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int NONE = -1;

        private readonly Guid          _requestId;
        private readonly EMarketAction _action;
        private readonly int           _category;
        private readonly int           _classId;
        private readonly int           _grade;
        private readonly string[]      _definitionIds;
        private readonly int           _sort;
        private readonly int           _order;
        private readonly int           _page;
        private readonly string        _definitionId;
        private readonly string        _listingId;
        private readonly string        _assetKind;
        private readonly string        _itemId;
        private readonly int           _quantity;
        private readonly long          _unitPrice;
        private readonly string[]      _listingIds;

        /****************************************
        *              Properties
        ****************************************/

        public Guid          RequestId     => _requestId;
        public EMarketAction Action        => _action;
        public int           Category      => _category;
        public int           ClassId       => _classId;
        public int           Grade         => _grade;
        public string[]      DefinitionIds => _definitionIds ?? Array.Empty<string>();
        public int           Sort          => _sort;
        public int           Order         => _order;
        public int           Page          => _page;
        public string        DefinitionId  => _definitionId;
        public string        ListingId     => _listingId;
        public string        AssetKind     => _assetKind;
        public string        ItemId        => _itemId;
        public int           Quantity      => _quantity;
        public long          UnitPrice     => _unitPrice;
        public string[]      ListingIds    => _listingIds ?? Array.Empty<string>();

        public bool IsWrite => (_action == EMarketAction.List) || (_action == EMarketAction.Buy) || (_action == EMarketAction.Cancel)
            || (_action == EMarketAction.Ack);

        /****************************************
        *                Methods
        ****************************************/

        public MarketRequestMessage(Guid requestId, EMarketAction action, int category, int classId, int grade, string[] definitionIds,
            int sort, int order, int page, string definitionId, string listingId, string assetKind, string itemId, int quantity,
            long unitPrice, string[] listingIds)
        {
            _requestId     = requestId;
            _action        = action;
            _category      = category;
            _classId       = classId;
            _grade         = grade;
            _definitionIds = definitionIds;
            _sort          = sort;
            _order         = order;
            _page          = page;
            _definitionId  = definitionId;
            _listingId     = listingId;
            _assetKind     = assetKind;
            _itemId        = itemId;
            _quantity      = quantity;
            _unitPrice     = unitPrice;
            _listingIds    = listingIds;
        }
    }
}
