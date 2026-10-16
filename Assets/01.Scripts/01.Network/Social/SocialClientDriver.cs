// Unity
using UnityEngine;

namespace Shinshi.Network
{
    // SocialClient는 정적 창구라 스스로 주기를 돌지 못한다. 수신 확인 묶음은 창이 닫혀 있어도 보내야 서버가 전달함 행을 지우므로
    // 창 바인더가 아니라 씬과 무관하게 남는 이 개체가 돌린다. 서버 빌드와 무화면 실행에서는 만들지 않는다
    /// <summary> SocialClient의 주기 처리(수신 확인, 시간 초과, 미룬 스냅샷)를 매 프레임 부른다, 씬에 두지 않고 스스로 만든다 </summary>
    public sealed class SocialClientDriver : MonoBehaviour
    {
        /****************************************
        *              Unity Event
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateOnStart()
        {
#if !UNITY_SERVER
            if (Application.isBatchMode)
            {
                return;
            }

            var root = new GameObject(nameof(SocialClientDriver));
            DontDestroyOnLoad(root);
            root.AddComponent<SocialClientDriver>();
#endif
        }

        private void Update()
        {
            SocialClient.Tick(Time.realtimeSinceStartupAsDouble);
        }
    }
}
