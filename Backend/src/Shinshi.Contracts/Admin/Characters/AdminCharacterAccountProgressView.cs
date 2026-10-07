// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 계정 영구 진행과 명시된 해금 기록 </summary>
    [DataContract]
    public sealed class AdminCharacterAccountProgressView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "introCompletedAtUnixMs")] private long?                      _introCompletedAtUnixMs;
        [DataMember(Name = "foxTraces")]              private int?                       _foxTraces;
        [DataMember(Name = "revision")]               private long?                      _revision;
        [DataMember(Name = "updatedAtUnixMs")]        private long?                      _updatedAtUnixMs;
        [DataMember(Name = "unlocks")]                private AdminCharacterUnlockView[] _unlocks;

        /****************************************
        *              Properties
        ****************************************/

        public long?                      IntroCompletedAtUnixMs => _introCompletedAtUnixMs;
        public int?                       FoxTraces              => _foxTraces;
        public long?                      Revision               => _revision;
        public long?                      UpdatedAtUnixMs        => _updatedAtUnixMs;
        public AdminCharacterUnlockView[] Unlocks                => _unlocks;

        /****************************************
        *                Methods
        ****************************************/

        public AdminCharacterAccountProgressView(long? introCompletedAtUnixMs, int? foxTraces, long? revision,
            long? updatedAtUnixMs, AdminCharacterUnlockView[] unlocks)
        {
            _introCompletedAtUnixMs = introCompletedAtUnixMs;
            _foxTraces              = foxTraces;
            _revision               = revision;
            _updatedAtUnixMs        = updatedAtUnixMs;
            _unlocks                = unlocks;
        }

        private AdminCharacterAccountProgressView() { }
    }
}
