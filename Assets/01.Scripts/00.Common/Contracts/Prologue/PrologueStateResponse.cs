// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 캐릭터의 프롤로그 저장 상태와 계정 완료 여부, 로비 선택창과 던전 재개가 읽는다 </summary>
    [DataContract]
    public sealed class PrologueStateResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "characterId")]      private string _characterId;

        // PrologueCodes.Stage. 프롤로그를 아직 시작하지 않은 캐릭터는 INTRO다
        [DataMember(Name = "stage")]            private int    _stage;

        // 월백 구간에 들어갈 때 남아 있던 임시 물약 수. 월백 재개 때 이 수량으로 복원한다
        [DataMember(Name = "wolbaekPotions")]   private int    _wolbaekPotions;

        // 이 캐릭터가 부적 조사 화면을 이미 본 적이 있는가
        [DataMember(Name = "talismanChecked")]  private bool   _talismanChecked;

        // 계정이 프롤로그를 한 번이라도 마쳤는가(신시 안내 완료 기록과 같다). 거짓이면 건너뛸 수 없다
        [DataMember(Name = "accountCompleted")] private bool   _accountCompleted;

        [DataMember(Name = "serverTimeUnixMs")] private long   _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string CharacterId      => _characterId;
        public int    Stage            => _stage;
        public int    WolbaekPotions   => _wolbaekPotions;
        public bool   TalismanChecked  => _talismanChecked;
        public bool   AccountCompleted => _accountCompleted;
        public long   ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public PrologueStateResponse(string characterId, int stage, int wolbaekPotions, bool talismanChecked,
            bool accountCompleted, long serverTimeUnixMs)
        {
            _characterId      = characterId;
            _stage            = stage;
            _wolbaekPotions   = wolbaekPotions;
            _talismanChecked  = talismanChecked;
            _accountCompleted = accountCompleted;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private PrologueStateResponse() { }
    }
}
