// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 계정이 마친 성장 메뉴 안내 ID 목록 </summary>
    [DataContract]
    public sealed class MenuGuideListResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "menuIds")]          private string[] _menuIds;
        [DataMember(Name = "serverTimeUnixMs")] private long     _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string[] MenuIds          => _menuIds;
        public long     ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public MenuGuideListResponse(string[] menuIds, long serverTimeUnixMs)
        {
            _menuIds          = menuIds;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private MenuGuideListResponse() { }
    }
}
