// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 입장 창 목록의 한 줄. 진행 카탈로그 값을 글자로 옮긴 것이며 해금이나 입장 허가는 담지 않는다 </summary>
    public readonly struct DungeonEntryData
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string          _contentId;
        private readonly EPartyMode      _mode;
        private readonly string          _title;
        private readonly string          _category;
        private readonly string          _summary;
        private readonly string          _requirement;
        private readonly string          _cost;
        private readonly bool            _isAvailable;
        private readonly string          _unavailableReason;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 일반전이면 지역 ID, 보스전이면 보스전 정의 ID. 파티 방을 만들 때 그대로 서버에 보낸다 </summary>
        public string     ContentId         => _contentId;
        public EPartyMode Mode              => _mode;
        public string     Title             => _title;
        public string     Category          => _category;
        public string     Summary           => _summary;
        public string     Requirement       => _requirement;
        public string     Cost              => _cost;

        /// <summary> 이 콘텐츠 묶음(체험판 여부)에서 입장 요청을 보낼 수 있는 항목인지. 계정 진행 해금은 서버가 판정한다 </summary>
        public bool       IsAvailable       => _isAvailable;
        public string     UnavailableReason => _unavailableReason;

        /****************************************
        *                Methods
        ****************************************/

        public DungeonEntryData(string contentId, EPartyMode mode, string title, string category, string summary, string requirement,
            string cost, bool isAvailable, string unavailableReason)
        {
            _contentId         = contentId;
            _mode              = mode;
            _title             = title;
            _category          = category;
            _summary           = summary;
            _requirement       = requirement;
            _cost              = cost;
            _isAvailable       = isAvailable;
            _unavailableReason = unavailableReason;
        }
    }
}
