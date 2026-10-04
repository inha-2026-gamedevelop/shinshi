// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 계정 영구 진행. 해금 여부는 기록과 콘텐츠 요구 조건으로 API가 계산한다 </summary>
    [DataContract]
    public sealed class AccountProgressResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "introCompleted")]   private bool                 _introCompleted;
        [DataMember(Name = "foxTraces")]        private int                  _foxTraces;
        [DataMember(Name = "revision")]         private long                 _revision;
        [DataMember(Name = "regions")]          private ProgressTargetView[] _regions;
        [DataMember(Name = "bossBattles")]      private ProgressTargetView[] _bossBattles;
        [DataMember(Name = "contentVersion")]   private string               _contentVersion;
        [DataMember(Name = "serverTimeUnixMs")] private long                 _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public bool                 IntroCompleted   => _introCompleted;
        public int                  FoxTraces        => _foxTraces;
        public long                 Revision         => _revision;
        public ProgressTargetView[] Regions          => _regions;
        public ProgressTargetView[] BossBattles      => _bossBattles;
        public string               ContentVersion   => _contentVersion;
        public long                 ServerTimeUnixMs => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AccountProgressResponse(bool introCompleted, int foxTraces, long revision, ProgressTargetView[] regions,
            ProgressTargetView[] bossBattles, string contentVersion, long serverTimeUnixMs)
        {
            _introCompleted   = introCompleted;
            _foxTraces        = foxTraces;
            _revision         = revision;
            _regions          = regions;
            _bossBattles      = bossBattles;
            _contentVersion   = contentVersion;
            _serverTimeUnixMs = serverTimeUnixMs;
        }

        private AccountProgressResponse() { }
    }
}
