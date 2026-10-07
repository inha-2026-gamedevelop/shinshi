// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 부적 효과별 등록 단계와 등록 실패 보정 횟수 </summary>
    [DataContract]
    public sealed class AdminCharacterTalismanProgressView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "effectId")]         private string _effectId;
        [DataMember(Name = "level")]            private int    _level;
        [DataMember(Name = "registerFailures")] private int    _registerFailures;
        [DataMember(Name = "updatedAtUnixMs")]  private long   _updatedAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string EffectId         => _effectId;
        public int    Level            => _level;
        public int    RegisterFailures => _registerFailures;
        public long   UpdatedAtUnixMs  => _updatedAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AdminCharacterTalismanProgressView(string effectId, int level, int registerFailures,
            long updatedAtUnixMs)
        {
            _effectId         = effectId;
            _level            = level;
            _registerFailures = registerFailures;
            _updatedAtUnixMs  = updatedAtUnixMs;
        }

        private AdminCharacterTalismanProgressView() { }
    }
}
