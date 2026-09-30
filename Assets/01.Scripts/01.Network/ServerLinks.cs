// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.Network
{
    /// <summary> 상태 컴포넌트의 서버 시작, 종료를 서버 어셈블리의 처리기에 넘기는 창구 </summary>
    public static class ServerLinks
    {
        /****************************************
        *                Fields
        ****************************************/

        // 정확한 타입으로만 찾는다. 상속한 타입은 따로 등록한다
        private static readonly Dictionary<Type, Action<Component>> _started = new Dictionary<Type, Action<Component>>();
        private static readonly Dictionary<Type, Action<Component>> _stopped = new Dictionary<Type, Action<Component>>();

        /****************************************
        *                Methods
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 처리기가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            Clear();
        }

        /// <summary> 서버 부트스트랩이 타입마다 한 번 등록한다 </summary>
        public static void Register<T>(Action<T> started, Action<T> stopped) where T : Component
        {
            if ((started == null) || (stopped == null))
            {
                throw new ArgumentNullException(started == null ? nameof(started) : nameof(stopped));
            }

            Type type = typeof(T);
            if (_started.ContainsKey(type))
            {
                throw new InvalidOperationException($"ServerLinks에 이미 등록된 타입이다: {type.Name}");
            }

            _started.Add(type, state => started((T)state));
            _stopped.Add(type, state => stopped((T)state));
        }

        /// <summary> 상태의 OnStartServer에서 부른다 </summary>
        public static void NotifyStarted(Component state)
        {
            Notify(_started, state);
        }

        /// <summary> 상태의 OnStopServer에서 부른다 </summary>
        public static void NotifyStopped(Component state)
        {
            Notify(_stopped, state);
        }

        public static void Clear()
        {
            _started.Clear();
            _stopped.Clear();
        }

        private static void Notify(Dictionary<Type, Action<Component>> handlers, Component state)
        {
            if (state == null)
            {
                return;
            }

            if (!handlers.TryGetValue(state.GetType(), out Action<Component> handler))
            {
                return;
            }

            handler(state);
        }
    }
}
