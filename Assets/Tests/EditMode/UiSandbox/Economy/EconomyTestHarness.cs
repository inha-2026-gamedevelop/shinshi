// System
using System;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Editor;

namespace Shinshi.Tests
{
    /// <summary> 경제 화면 시험 도구. 미리보기 씬에 시험 범위를 열고 공유 UiSandboxContext를 만든다. 활성 씬은 건드리지 않으며, 정리하면 페이지를 떼고 범위와 씬을 닫는다 </summary>
    internal sealed class EconomyTestHarness : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const double START_TIME = 100.0;

        private const string UI_NAMESPACE = "Shinshi.UI";

        private readonly HashSet<MonoBehaviour> _awoken = new HashSet<MonoBehaviour>();
        private readonly List<IUiSandboxPage>   _pages  = new List<IUiSandboxPage>();
        private UiSandboxTestScene              _scene;
        private IDisposable                     _scope;
        private UiSandboxScenarioSO             _template;
        private bool                            _disposed;

        /****************************************
        *              Properties
        ****************************************/

        public double Now { get; set; }

        public bool Playing { get; set; }

        public UiSandboxContext Context { get; }

        public Scene Scene
        {
            get { return _scene.Scene; }
        }

        public UiSandboxRequestRecord LastRecord
        {
            get { return Context.History[Context.History.Count - 1]; }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 미리보기 씬이라 사용자 씬과 활성 씬에 손대지 않는다. 중간에 실패하면 이미 만든 것만 거꾸로 정리하고 예외를 다시 던진다
        public EconomyTestHarness()
        {
            Now     = START_TIME;
            Playing = true;

            try
            {
                _scene    = UiSandboxTestScene.CreatePreview();
                _scope    = EconomyFixtureFactory.BeginTestScope(_scene.Scene);
                _template = ScriptableObject.CreateInstance<UiSandboxScenarioSO>();
                Context   = new UiSandboxContext(_template, null, () => Now, () => Playing, () => null);
            }
            catch
            {
                Cleanup();
                throw;
            }
        }

        public GameObject Spawn(string name)
        {
            return _scene.Spawn(name);
        }

        /// <summary> 맥락의 루트 목록에 오브젝트를 더한다 </summary>
        public void AddRoot(GameObject root)
        {
            List<GameObject> roots = new List<GameObject>(Context.Roots);
            roots.Add(root);
            Context.Rebind(roots.ToArray());
        }

        public void SetMode(EUiSandboxResponseMode mode, float delay = 0.0f, string error = "")
        {
            Context.Scenario.SetString(UiSandboxScenarioKeys.RESPONSE_MODE, mode.ToString());
            Context.Scenario.SetFloat(UiSandboxScenarioKeys.RESPONSE_DELAY, delay);
            Context.Scenario.SetString(UiSandboxScenarioKeys.RESPONSE_ERROR, error);
        }

        /// <summary> 창이 하는 순서 그대로 연결하고(Attach), 편집 모드에서 안 도는 Awake를 대신 돌린 뒤 값을 화면에 적용한다(Apply) </summary>
        public void AttachAndApply(IUiSandboxPage page)
        {
            if (!_pages.Contains(page))
            {
                _pages.Add(page);
            }

            page.Attach(Context);
            RunAwakesOnRoots();
            page.Apply(Context);
        }

        /// <summary> 현재 시각으로 한 번 처리한다 </summary>
        public void Tick()
        {
            Context.Tick(Now);
        }

        public void Advance(double seconds)
        {
            Now += seconds;
            Context.Tick(Now);
        }

        public void RunAwakesOnRoots()
        {
            for (int i = 0; i < Context.Roots.Count; ++i)
            {
                if (Context.Roots[i] != null)
                {
                    RunAwakes(Context.Roots[i]);
                }
            }
        }

        // 편집 모드에서는 SetActive해도 Awake가 돌지 않는다. 자식부터 한 번씩만 돌려 Play Mode 진입과 같은 연결 상태를 만든다
        public void RunAwakes(GameObject root)
        {
            if (Application.isPlaying)
            {
                return;
            }

            MonoBehaviour[] all = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = all.Length - 1; i >= 0; --i)
            {
                MonoBehaviour behaviour = all[i];
                if ((behaviour == null) || (behaviour.GetType().Namespace == null) || (!behaviour.GetType().Namespace.StartsWith(UI_NAMESPACE, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (!_awoken.Add(behaviour))
                {
                    continue;
                }

                MethodInfo awake = behaviour.GetType().GetMethod("Awake",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (awake != null)
                {
                    awake.Invoke(behaviour, null);
                }
            }
        }

        /// <summary> field-like 이벤트에 걸린 구독자 수. 이벤트 이름과 같은 숨은 필드를 읽는다 </summary>
        public static int SubscriberCount(object target, string eventName)
        {
            FieldInfo field = target.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.NotNull(field, target.GetType().Name + "에 이벤트 " + eventName + "이(가) 없다");
            Delegate handler = field.GetValue(target) as Delegate;
            return (handler == null) ? 0 : handler.GetInvocationList().Length;
        }

        /// <summary> 이름이 같은 자식 컴포넌트를 찾는다. 꺼진 오브젝트도 본다 </summary>
        public static T Child<T>(Component root, string name) where T : Component
        {
            T[] all = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < all.Length; ++i)
            {
                if (all[i].gameObject.name == name)
                {
                    return all[i];
                }
            }

            Assert.Fail(root.name + " 아래에 " + typeof(T).Name + " '" + name + "'이(가) 없다");
            return null;
        }

        /// <summary> 실제 단추 클릭과 같은 경로인 onClick을 호출한다 </summary>
        public static void Click(Button button)
        {
            Assert.NotNull(button);
            button.onClick.Invoke();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Cleanup();
        }

        // 만들어진 것만 정리한다. 각 단계가 실패해도 다음 단계는 계속한다
        private void Cleanup()
        {
            for (int i = _pages.Count - 1; i >= 0; --i)
            {
                try
                {
                    _pages[i].Detach();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            _pages.Clear();

            if (_scope != null)
            {
                _scope.Dispose();
                _scope = null;
            }

            if (Context != null)
            {
                Context.Dispose();
            }

            if (_scene != null)
            {
                _scene.Dispose();
                _scene = null;
            }

            if (_template != null)
            {
                UnityEngine.Object.DestroyImmediate(_template);
                _template = null;
            }
        }
    }
}
