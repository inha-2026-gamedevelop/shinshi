// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 운영자 목록(Owner). 비활성 운영자도 생성 순서대로 포함한다 </summary>
    [DataContract]
    public sealed class AdminOperatorListResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operators")] private AdminOperatorSummary[] _operators;

        /****************************************
        *              Properties
        ****************************************/

        public AdminOperatorSummary[] Operators => _operators;

        /****************************************
        *                Methods
        ****************************************/

        public AdminOperatorListResponse(AdminOperatorSummary[] operators)
        {
            _operators = operators;
        }

        private AdminOperatorListResponse() { }
    }
}
