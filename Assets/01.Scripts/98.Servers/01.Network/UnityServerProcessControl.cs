// Unity
using UnityEngine;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    /// <summary> Mirror 서버와 Unity 프로세스에 대한 종료 제어 </summary>
    public sealed class UnityServerProcessControl : IServerProcessControl
    {
        /****************************************
        *              Properties
        ****************************************/

        public int ConnectionCount => NetworkServer.connections.Count;

        /****************************************
        *                Methods
        ****************************************/

        public void DisconnectAll()
        {
            NetworkServer.DisconnectAll();
        }

        // 에디터에서 Quit을 부르면 플레이만 멈추지 않고 무시되거나 다른 작업을 끊을 수 있어 기록만 남긴다
        public void Quit(int exitCode)
        {
#if UNITY_EDITOR
            Debug.LogWarning($"[Shutdown] 에디터라 프로세스를 끝내지 않는다, exitCode={exitCode}");
#else
            Debug.Log($"[Shutdown] 프로세스를 끝낸다, exitCode={exitCode}");
            Application.Quit(exitCode);
#endif
        }
    }
}
