// System
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.Art
{
    /// <summary> 여러 장면이 함께 열려도 켜진 AudioListener가 하나만 남게 한다 </summary>
    public static class AudioListenerGuard
    {
        /****************************************
        *                Fields
        ****************************************/

        // 장면을 읽을 때만 쓰는 임시 목록이다. 매번 새로 만들지 않으려고 재사용한다
        private static readonly List<GameObject>    _roots     = new List<GameObject>();
        private static readonly List<AudioListener> _found     = new List<AudioListener>();
        private static readonly List<AudioListener> _listeners = new List<AudioListener>();

        /****************************************
        *              Unity Event
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 구독이 겹치지 않게 먼저 뗀다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            SceneManager.sceneLoaded        -= HandleSceneLoaded;
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        }

        // 에디터에서 장면 여럿을 열어 둔 채 실행하면 처음 열린 장면들은 sceneLoaded가 오지 않아 여기서 한 번 정리한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeAfterSceneLoad()
        {
            SceneManager.sceneLoaded        += HandleSceneLoaded;
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
            KeepSingleListener();
        }

        /****************************************
        *                Methods
        ****************************************/

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            KeepSingleListener();
        }

        private static void HandleActiveSceneChanged(Scene previous, Scene next)
        {
            KeepSingleListener();
        }

        // 활성 장면의 리스너를 우선 남기고, 활성 장면에 없으면 처음 찾은 것을 남긴다
        private static void KeepSingleListener()
        {
            CollectEnabledListeners();
            if (_listeners.Count <= 1)
            {
                _listeners.Clear();
                return;
            }

            Scene active = SceneManager.GetActiveScene();
            AudioListener keep = _listeners[0];
            for (int i = 0; i < _listeners.Count; i++)
            {
                if (_listeners[i].gameObject.scene == active)
                {
                    keep = _listeners[i];
                    break;
                }
            }

            for (int i = 0; i < _listeners.Count; i++)
            {
                if (_listeners[i] != keep)
                {
                    _listeners[i].enabled = false;
                }
            }

            _listeners.Clear();
        }

        // Find 계열 API 대신 열린 장면의 루트부터 훑는다. 장면이 바뀔 때만 불린다
        private static void CollectEnabledListeners()
        {
            _listeners.Clear();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                {
                    continue;
                }

                scene.GetRootGameObjects(_roots);
                for (int r = 0; r < _roots.Count; r++)
                {
                    _roots[r].GetComponentsInChildren(false, _found);
                    for (int l = 0; l < _found.Count; l++)
                    {
                        if (_found[l].enabled)
                        {
                            _listeners.Add(_found[l]);
                        }
                    }
                }
            }

            _roots.Clear();
            _found.Clear();
        }
    }
}
