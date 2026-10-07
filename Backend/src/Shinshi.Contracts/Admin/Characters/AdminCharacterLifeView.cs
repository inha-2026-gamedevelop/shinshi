// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> DB 스냅샷 시각에 회복을 반영한 계정 생활 상태. 지갑이 없으면 행동력은 null </summary>
    [DataContract]
    public sealed class AdminCharacterLifeView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "actionValue")]         private long?                  _actionValue;
        [DataMember(Name = "actionCap")]           private long                   _actionCap;
        [DataMember(Name = "actionAnchorUnixMs")]  private long?                  _actionAnchorUnixMs;
        [DataMember(Name = "nextRecoverAtUnixMs")] private long?                  _nextRecoverAtUnixMs;
        [DataMember(Name = "snapshotAtUnixMs")]    private long                   _snapshotAtUnixMs;
        [DataMember(Name = "herbs")]               private AdminAccountItemView[] _herbs;
        [DataMember(Name = "potions")]             private AdminAccountItemView[] _potions;

        /****************************************
        *              Properties
        ****************************************/

        public long?                  ActionValue         => _actionValue;
        public long                   ActionCap           => _actionCap;
        public long?                  ActionAnchorUnixMs  => _actionAnchorUnixMs;
        public long?                  NextRecoverAtUnixMs => _nextRecoverAtUnixMs;
        public long                   SnapshotAtUnixMs    => _snapshotAtUnixMs;
        public AdminAccountItemView[] Herbs               => _herbs;
        public AdminAccountItemView[] Potions             => _potions;

        /****************************************
        *                Methods
        ****************************************/

        public AdminCharacterLifeView(long? actionValue, long actionCap, long? actionAnchorUnixMs,
            long? nextRecoverAtUnixMs, long snapshotAtUnixMs, AdminAccountItemView[] herbs,
            AdminAccountItemView[] potions)
        {
            _actionValue         = actionValue;
            _actionCap           = actionCap;
            _actionAnchorUnixMs  = actionAnchorUnixMs;
            _nextRecoverAtUnixMs = nextRecoverAtUnixMs;
            _snapshotAtUnixMs    = snapshotAtUnixMs;
            _herbs               = herbs;
            _potions             = potions;
        }

        private AdminCharacterLifeView() { }
    }
}
