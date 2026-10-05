// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 준비 확인 참가자의 계정 진행 기록. 해금 여부는 마을이 GameDB 요구 조건으로 판정하고 예약이 같은 판정으로 확정한다 </summary>
    [DataContract]
    public sealed class ReadinessProgress
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "introCompleted")]    private bool     _introCompleted;
        [DataMember(Name = "foxTraces")]         private int      _foxTraces;

        // 기록된 ID만 담는다. unlocked는 명시 해금 기록이고 요구 조건으로 계산한 해금은 넣지 않는다. ID 오름차순
        [DataMember(Name = "clearedRegionIds")]  private string[] _clearedRegionIds;
        [DataMember(Name = "unlockedRegionIds")] private string[] _unlockedRegionIds;
        [DataMember(Name = "clearedBattleIds")]  private string[] _clearedBattleIds;
        [DataMember(Name = "unlockedBattleIds")] private string[] _unlockedBattleIds;

        /****************************************
        *              Properties
        ****************************************/

        public bool     IntroCompleted    => _introCompleted;
        public int      FoxTraces         => _foxTraces;
        public string[] ClearedRegionIds  => _clearedRegionIds;
        public string[] UnlockedRegionIds => _unlockedRegionIds;
        public string[] ClearedBattleIds  => _clearedBattleIds;
        public string[] UnlockedBattleIds => _unlockedBattleIds;

        /****************************************
        *                Methods
        ****************************************/

        public ReadinessProgress(bool introCompleted, int foxTraces, string[] clearedRegionIds, string[] unlockedRegionIds,
            string[] clearedBattleIds, string[] unlockedBattleIds)
        {
            _introCompleted    = introCompleted;
            _foxTraces         = foxTraces;
            _clearedRegionIds  = clearedRegionIds;
            _unlockedRegionIds = unlockedRegionIds;
            _clearedBattleIds  = clearedBattleIds;
            _unlockedBattleIds = unlockedBattleIds;
        }

        private ReadinessProgress() { }
    }
}
