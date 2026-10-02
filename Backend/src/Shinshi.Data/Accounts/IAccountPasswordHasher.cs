namespace Shinshi.Data.Accounts
{
    /// <summary> 플레이어 비밀번호 해시 계약. API가 DB 트랜잭션 밖에서 부르며 테스트는 호출 수를 세는 대역으로 바꾼다 </summary>
    public interface IAccountPasswordHasher
    {
        // 정책을 통과한 비밀번호를 새 솔트로 해시해 저장 형식 문자열로 돌려준다
        string Hash(string password);

        // 저장 형식의 회수, 솔트로 다시 계산해 고정 시간으로 비교한다. 형식이 틀린 저장값은 false다
        bool Verify(string? password, string encodedHash);

        // 없는 아이디도 같은 회수의 계산을 거쳐 응답 시간으로 계정 존재를 알 수 없게 한다. 항상 false다
        bool VerifyDummy(string? password);

        // 저장 해시의 회수가 현재 기본 회수보다 낮으면 true다. 로그인에 성공했을 때 새 회수로 다시 저장하는 데 쓴다
        bool NeedsRehash(string encodedHash);
    }
}
