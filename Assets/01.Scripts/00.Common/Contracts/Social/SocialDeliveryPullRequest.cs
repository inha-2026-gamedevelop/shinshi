// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 전달함 꺼내기. ackDeliveries의 행을 먼저 지우고 꺼낼 행을 돌려준다 </summary>
    [DataContract]
    public sealed class SocialDeliveryPullRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 호출 서버 프로세스의 bootId. 서버 임대가 묶인 boot와 다르면 409 SESSION_MISMATCH
        [DataMember(Name = "bootId")]        private string              _bootId;
        // 롱폴 자리. 1차는 0만 받는다
        [DataMember(Name = "waitMs")]        private int                 _waitMs;
        // 최대 SocialLimits.MAX_ACKS_PER_PULL
        [DataMember(Name = "ackDeliveries")] private SocialDeliveryAck[] _ackDeliveries;

        /****************************************
        *              Properties
        ****************************************/

        public string              BootId        => _bootId;
        public int                 WaitMs        => _waitMs;
        public SocialDeliveryAck[] AckDeliveries => _ackDeliveries;

        /****************************************
        *                Methods
        ****************************************/

        public SocialDeliveryPullRequest(string bootId, int waitMs, SocialDeliveryAck[] ackDeliveries)
        {
            _bootId        = bootId;
            _waitMs        = waitMs;
            _ackDeliveries = ackDeliveries;
        }

        private SocialDeliveryPullRequest() { }
    }
}
