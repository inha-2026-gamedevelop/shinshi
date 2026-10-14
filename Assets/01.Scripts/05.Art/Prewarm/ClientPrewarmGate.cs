// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 부팅 흐름이 프리워밍을 시작할지 묻는 창구, 러너가 스스로 등록하므로 씬 탐색이 필요 없다 </summary>
    public static class ClientPrewarmGate
    {
        /****************************************
        *                Fields
        ****************************************/

        /// <summary> 에디터 토글과 게이트가 같이 쓰는 EditorPrefs 키 </summary>
        public const string EDITOR_PREF_KEY = "Shinshi.Prewarm.RunInEditor";

        private const string ARG_SKIP  = "-skip-prewarm";
        private const string ARG_FORCE = "-force-prewarm";

        private static ClientPrewarmRunner _runner = null;
        private static bool                _hasRun = false;

        /****************************************
        *                Methods
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 값이 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _runner = null;
            _hasRun = false;
        }

        internal static void Register(ClientPrewarmRunner runner)
        {
            _runner = runner;
        }

        internal static void Unregister(ClientPrewarmRunner runner)
        {
            if (_runner == runner)
            {
                _runner = null;
            }
        }

        /// <summary> 프리워밍을 시작했으면 true, 끝나면 onFinished가 한 번 불린다. false면 호출하지 않았으니 호출자가 직접 진행한다 </summary>
        public static bool TryBegin(Action onFinished)
        {
#if UNITY_SERVER
            return false;
#else
            if (Application.isBatchMode)
            {
                return false;
            }

            if (_hasRun)
            {
                return false;
            }

            if (_runner == null)
            {
                return false;
            }

            if (HasArgument(ARG_SKIP))
            {
                return false;
            }

#if UNITY_EDITOR
            // 에디터는 반복 플레이가 느려지지 않게 기본으로 건너뛰고, 인자나 메뉴 토글이 켜졌을 때만 돈다
            if ((!HasArgument(ARG_FORCE)) && (!UnityEditor.EditorPrefs.GetBool(EDITOR_PREF_KEY, false)))
            {
                return false;
            }
#endif

            _hasRun = true;
            _runner.Run(onFinished);
            return true;
#endif
        }

#if !UNITY_SERVER
        private static bool HasArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; ++i)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
#endif
    }
}
