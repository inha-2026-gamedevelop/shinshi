// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 바인더들이 함께 쓰는 클라이언트 접속 상태 조회 </summary>
    public static class ClientRoles
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 던전 서버에 접속해 있으면 참. 던전은 아직 전용 씬이 없어 마을 씬에서 이 값으로 구분한다 </summary>
        public static bool IsDungeon()
        {
            return (NetworkClient.isConnected) && (NetworkManager.singleton is ShinshiNetworkManager manager)
                && (manager.ServerRole == EServerRole.Dungeon);
        }

        /// <summary> 마을 서버에 접속해 있으면 참 </summary>
        public static bool IsTown()
        {
            return (NetworkClient.isConnected) && (NetworkManager.singleton is ShinshiNetworkManager manager)
                && (manager.ServerRole == EServerRole.Town);
        }

        /// <summary> 소유 플레이어의 캐릭터 이름, 아직 스폰되지 않았으면 빈 문자열 </summary>
        public static string LocalCharacterName()
        {
            NetworkIdentity local = NetworkClient.localPlayer;
            if ((local == null) || (!local.TryGetComponent(out PlayerCombatState combat)))
            {
                return string.Empty;
            }

            return combat.DisplayName;
        }
    }
}
