// 프로젝트
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>출정 준비 화면 파티 칸 하나가 표시할 값이며 서버 값을 화면용으로 옮긴 사본이다</summary>
    public struct SortieMemberData
    {
        public string       Name;
        public EPlayerClass PlayerClass;
        public bool         IsLeader;
        public bool         IsReady;
        public bool         IsConnected;

        // 해금, 티켓, 지참 물약 조건을 모두 갖췄는지
        public bool         MeetsRequirements;

        public bool IsEmpty { get { return string.IsNullOrEmpty(Name); } }
    }
}
