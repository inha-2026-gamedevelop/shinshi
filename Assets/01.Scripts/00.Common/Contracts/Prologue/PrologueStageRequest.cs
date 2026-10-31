// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 던전 서버가 보내는 프롤로그 단계 저장 요청, 세션은 경로에 넣는다. 단계는 앞으로만 간다(같은 단계 재전송은 확정) </summary>
    [DataContract]
    public sealed class PrologueStageRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]     private string _operationId;
        [DataMember(Name = "characterId")]     private string _characterId;

        // PrologueCodes.Stage
        [DataMember(Name = "stage")]           private int    _stage;

        // 월백 진입 당시 임시 물약 수, 월백 이외 단계에서는 0이다
        [DataMember(Name = "wolbaekPotions")]  private int    _wolbaekPotions;

        // 부적 조사 화면을 본 적이 있는가. 한 번 참이 되면 되돌리지 않는다
        [DataMember(Name = "talismanChecked")] private bool   _talismanChecked;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId     => _operationId;
        public string CharacterId     => _characterId;
        public int    Stage           => _stage;
        public int    WolbaekPotions  => _wolbaekPotions;
        public bool   TalismanChecked => _talismanChecked;

        /****************************************
        *                Methods
        ****************************************/

        public PrologueStageRequest(string operationId, string characterId, int stage, int wolbaekPotions, bool talismanChecked)
        {
            _operationId     = operationId;
            _characterId     = characterId;
            _stage           = stage;
            _wolbaekPotions  = wolbaekPotions;
            _talismanChecked = talismanChecked;
        }

        private PrologueStageRequest() { }
    }
}
