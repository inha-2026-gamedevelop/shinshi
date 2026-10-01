// System
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 로그인한 계정의 캐릭터 목록 </summary>
    [DataContract]
    public sealed class CharacterListResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        // 최상위 배열 대신 객체로 감싸 v1 안에서 필드를 더할 수 있게 한다
        [DataMember(Name = "characters")]       private CharacterSummary[] _characters;

        [DataMember(Name = "serverTimeUnixMs")] private long               _serverTimeUnixMs;

        // TODO(민성): 계정당 캐릭터 수 상한은 기획 미정

        /****************************************
        *              Properties
        ****************************************/

        public IReadOnlyList<CharacterSummary> Characters       => _characters;
        public long                            ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public CharacterListResponse(CharacterSummary[] characters, long serverTimeUnixMs)
        {
            _characters       = characters;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private CharacterListResponse() { }
    }
}
