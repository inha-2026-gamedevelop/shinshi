// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 부적 효과 하나의 등록 진도, level 0은 실패 보정만 쌓인 상태다 </summary>
    [DataContract]
    public sealed class TalismanProgressView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "effectId")]         private string _effectId;
        [DataMember(Name = "level")]            private int    _level;
        [DataMember(Name = "registerFailures")] private int    _registerFailures;

        /****************************************
        *              Properties
        ****************************************/

        public string EffectId         => _effectId;
        public int    Level            => _level;
        public int    RegisterFailures => _registerFailures;

        /****************************************
        *                Methods
        ****************************************/

        public TalismanProgressView(string effectId, int level, int registerFailures)
        {
            _effectId         = effectId;
            _level            = level;
            _registerFailures = registerFailures;
        }

        private TalismanProgressView() { }
    }
}
