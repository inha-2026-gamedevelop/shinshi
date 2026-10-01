// Unity
using UnityEngine;

namespace Shinshi.Common
{
    /// <summary> 클라이언트 표현 컴포넌트의 기반, 서버 빌드에서는 스스로 꺼진다 </summary>
    public abstract class ClientView : MonoBehaviour
    {
        /****************************************
        *              Properties
        ****************************************/

        // 헤드리스 서버는 화면이 없으므로 표현 갱신 비용을 쓰지 않는다
        protected static bool IsServerBuild
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return false;
#endif
            }
        }

        /****************************************
        *              Unity Event
        ****************************************/

        // 하위 클래스는 Awake를 직접 만들지 않고 OnViewAwake를 쓴다. 여기서 꺼지면 OnEnable, Update가 불리지 않는다
        private void Awake()
        {
            if (IsServerBuild)
            {
                OnServerBuildAwake();
                enabled = false;
                return;
            }

            OnViewAwake();
        }

        /****************************************
        *                Methods
        ****************************************/

        protected virtual void OnViewAwake() { }

        /// <summary> 서버 빌드에서 이 컴포넌트가 꺼지기 직전에 한 번 불린다, 함께 붙은 표현용 외부 컴포넌트를 끌 때 쓴다 </summary>
        protected virtual void OnServerBuildAwake() { }
    }
}
