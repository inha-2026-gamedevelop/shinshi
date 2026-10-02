// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 자기 계정의 수령 가능한 우편 목록. 만료, 취소, 수령한 우편은 없고 오래된 순서로 최대 100개다 </summary>
    [DataContract]
    public sealed class InboxListResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "entries")]          private InboxEntry[] _entries;

        // 남은 기한 표시는 이 서버 시각을 기준으로 계산한다. 클라이언트 시계는 만료 판정에 쓰지 않는다
        [DataMember(Name = "serverTimeUnixMs")] private long         _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public InboxEntry[] Entries          => _entries;
        public long         ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public InboxListResponse(InboxEntry[] entries, long serverTimeUnixMs)
        {
            _entries          = entries;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private InboxListResponse() { }
    }
}
