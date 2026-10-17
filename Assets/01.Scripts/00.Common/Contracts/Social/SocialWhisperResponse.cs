// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 귓속말 결과. 성공이면 호출 인스턴스에 있는 수신자 앞 남은 행(새 행 포함)을 오름차순으로 준다 </summary>
    [DataContract]
    public sealed class SocialWhisperResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "result")]          private SocialActionResponse _result;
        // 대상의 저장된 표시 이름. 거절이면 빈 문자열
        [DataMember(Name = "targetName")]      private string               _targetName;
        // 대상이 다른 서버에 있거나 남은 행이 20개를 넘으면 비어 있다(꺼내기에 맡긴다)
        [DataMember(Name = "localDeliveries")] private SocialDeliveryRow[]  _localDeliveries;

        /****************************************
        *              Properties
        ****************************************/

        public SocialActionResponse Result          => _result;
        public string               TargetName      => _targetName;
        public SocialDeliveryRow[]  LocalDeliveries => _localDeliveries;

        /****************************************
        *                Methods
        ****************************************/

        public SocialWhisperResponse(SocialActionResponse result, string targetName, SocialDeliveryRow[] localDeliveries)
        {
            _result          = result;
            _targetName      = targetName;
            _localDeliveries = localDeliveries;
        }

        private SocialWhisperResponse() { }
    }
}
