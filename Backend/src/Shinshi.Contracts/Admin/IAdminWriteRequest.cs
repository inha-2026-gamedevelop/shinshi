namespace Shinshi.Contracts.Admin
{
    /// <summary> 어드민 쓰기 요청 공통. operationId는 Guid 문자열, reason은 공백이 아닌 최대 500자이며 없으면 400으로 거절한다 </summary>
    public interface IAdminWriteRequest
    {
        /****************************************
        *              Properties
        ****************************************/

        // 같은 ID로 다시 보내면 처음 확정한 결과를 받는다. 응답을 잃은 작업은 새 ID를 만들지 않는다
        string OperationId { get; }

        // 감사 로그에 그대로 남는다
        string Reason { get; }
    }
}
