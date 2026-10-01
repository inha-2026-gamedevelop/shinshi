// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 새 캐릭터 생성, 초기 지급이 함께 확정되므로 같은 operationId로만 재시도한다 </summary>
    [DataContract]
    public sealed class CharacterCreateRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 클라이언트가 처음 만들 때 UUID로 생성한다. 재시도에 새 ID를 쓰면 캐릭터와 지급이 두 번 생긴다
        [DataMember(Name = "operationId")] private string _operationId;

        // TODO(민성): 이름 길이, 허용 문자, 중복 규칙은 기획 미정
        [DataMember(Name = "name")]        private string _name;

        // ContractCodes.PlayerClass
        [DataMember(Name = "classId")]     private int    _classId;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId => _operationId;
        public string Name        => _name;
        public int    ClassId     => _classId;

        /****************************************
        *                Methods
        ****************************************/

        public CharacterCreateRequest(string operationId, string name, int classId)
        {
            _operationId = operationId;
            _name        = name;
            _classId     = classId;
        }

        private CharacterCreateRequest() { }
    }
}
