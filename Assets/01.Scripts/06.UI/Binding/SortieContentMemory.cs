// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 출정 직전 마을 파티 방의 콘텐츠를 기억해 던전 결과 창이 지역 이름을 쓰게 한다 </summary>
    // 던전 접속은 새 클라이언트라 마을 파티 방 상태가 비워지고, 출정 상태 메시지에도 콘텐츠 ID가 없다.
    // 서버 메시지를 늘리지 않으려고 마을 UI가 방이 바뀔 때마다 적어 두고 결과 창이 읽는다. 재접속으로 놓치면 이름은 비어 있다
    public static class SortieContentMemory
    {
        /****************************************
        *                Fields
        ****************************************/

        private static bool       _hasValue  = false;
        private static EPartyMode _mode      = EPartyMode.Normal;
        private static string     _contentId = string.Empty;

        /****************************************
        *                Methods
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 값이 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            Clear();
        }

        /// <summary> 방에 있을 때만 기억한다, 방을 나갈 때 값을 지우지 않는다. 출정하면 방이 닫히며 마지막 방이 남아야 한다 </summary>
        public static void Remember(in PartyRoomStateMessage room)
        {
            if ((!room.InRoom) || (string.IsNullOrEmpty(room.ContentId)))
            {
                return;
            }

            _hasValue  = true;
            _mode      = room.Mode;
            _contentId = room.ContentId;
        }

        public static bool TryRecall(out EPartyMode mode, out string contentId)
        {
            mode      = _mode;
            contentId = _contentId;
            return _hasValue;
        }

        public static void Clear()
        {
            _hasValue  = false;
            _mode      = EPartyMode.Normal;
            _contentId = string.Empty;
        }
    }
}
