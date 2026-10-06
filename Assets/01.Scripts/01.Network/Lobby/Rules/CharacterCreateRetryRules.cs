// System
using System;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 생성 재시도 판단. 이름, 직업, 외형 전체 스냅샷이 같을 때만 대기 중인 operationId를 재사용한다 </summary>
    public static class CharacterCreateRetryRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 응답 유실 재시도와 선택이 바뀐 새 요청을 구분한다 </summary>
        public static bool CanReuse(string pendingName, int pendingClassId, CharacterLook pendingLook,
            string nextName, int nextClassId, CharacterLook nextLook)
        {
            return string.Equals(pendingName, nextName, StringComparison.Ordinal)
                && (pendingClassId == nextClassId) && pendingLook.Equals(nextLook);
        }
    }
}
