using System;
using System.Collections.Generic;
using System.Reflection;
using Shinshi.Editor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.Tests
{
    /// <summary> Presentation 페이지 테스트가 함께 쓰는 결정적 컨텍스트 묶음. 시계와 Play 여부를 직접 쥐고 있어 지연 응답을 Tick으로 진행한다 </summary>
    internal sealed class PresentationTestHarness : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const double START_TIME = 100.0;

        private readonly List<GameObject>     _spawned = new List<GameObject>();
        private readonly List<IUiSandboxPage> _pages   = new List<IUiSandboxPage>();

        private double _now     = START_TIME;
        private bool   _playing = true;

        private UiSandboxScenarioSO     _template;
        private UiSandboxContext        _context;
        private PresentationFixtureScope _scope;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 시험 배치를 만들 임시 프리뷰 씬 범위. 열지 않은 하네스에서는 null </summary>
        public PresentationFixtureScope Scope
        {
            get { return _scope; }
        }

        public UiSandboxContext Context
        {
            get { return _context; }
        }

        public UiSandboxScenarioSO Scenario
        {
            get { return _context.Scenario; }
        }

        public bool Playing
        {
            get { return _playing; }
            set { _playing = value; }
        }

        /****************************************
        *                Methods
        ****************************************/

        public PresentationTestHarness() : this(true) { }

        /// <param name="openScope"> false면 시험 범위를 열지 않는다. 시험 배치 거절을 확인하는 테스트용 </param>
        public PresentationTestHarness(bool openScope)
        {
            if (openScope)
            {
                _scope = PresentationFixtureFactory.OpenTestScope();
            }

            _template = ScriptableObject.CreateInstance<UiSandboxScenarioSO>();
            _context  = new UiSandboxContext(_template, null, () => _now, () => _playing);
        }

        public T Track<T>(T page) where T : IUiSandboxPage
        {
            _pages.Add(page);
            return page;
        }

        /// <summary> 테스트용 씬 객체를 만든다. 범위가 열려 있으면 그 프리뷰 씬으로 옮겨 활성 씬을 건드리지 않는다 </summary>
        public GameObject Spawn(string name)
        {
            GameObject go = new GameObject(name);
            if ((_scope != null) && (_scope.IsOpen))
            {
                SceneManager.MoveGameObjectToScene(go, _scope.Scene);
            }

            _spawned.Add(go);
            return go;
        }

        public UiSandboxContext CreateContext(GameObject[] roots)
        {
            return new UiSandboxContext(_template, roots, () => _now, () => _playing);
        }

        /// <summary> 대기 중인 모의 응답을 지금 시각으로 완료시킨다 </summary>
        public void Tick()
        {
            _context.Tick(_now);
        }

        public void Advance(double seconds)
        {
            _now += seconds;
            _context.Tick(_now);
        }

        public void SetMode(EUiSandboxResponseMode mode, string error = "")
        {
            _context.Scenario.SetString(UiSandboxScenarioKeys.RESPONSE_MODE, mode.ToString());
            _context.Scenario.SetString(UiSandboxScenarioKeys.RESPONSE_ERROR, error);
        }

        /// <summary> 필드형 이벤트의 현재 구독자 수. 해제 여부를 View 파괴 뒤에도 확인할 수 있다 </summary>
        public static int SubscriberCount(object target, string eventName)
        {
            FieldInfo field   = target.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
            Delegate  handler = (field == null) ? null : field.GetValue(target) as Delegate;
            return (handler == null) ? 0 : handler.GetInvocationList().Length;
        }

        /// <summary> 필드형 이벤트를 직접 발생시킨다. 실제 버튼 클릭과 같은 구독자 경로를 탄다 </summary>
        public static void RaiseEvent(object target, string eventName, params object[] args)
        {
            FieldInfo field   = target.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
            Delegate  handler = (field == null) ? null : field.GetValue(target) as Delegate;
            if (handler != null)
            {
                handler.DynamicInvoke(args);
            }
        }

        /// <summary> 시험 캔버스 수. 이름만 같은 사용자 객체는 세지 않도록 숨김 플래그와 루트 여부를 함께 본다 </summary>
        public static int CountFixtureCanvases()
        {
            int          count = 0;
            GameObject[] all   = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < all.Length; ++i)
            {
                if ((all[i].scene.IsValid()) && (all[i].transform.parent == null) &&
                    ((all[i].hideFlags & HideFlags.DontSaveInEditor) != 0) && (all[i].name == PresentationFixtureFactory.CANVAS_NAME))
                {
                    ++count;
                }
            }

            return count;
        }

        public void Dispose()
        {
            for (int i = 0; i < _pages.Count; ++i)
            {
                _pages[i].Detach();
            }

            _pages.Clear();

            if (_context != null)
            {
                _context.Dispose();
                _context = null;
            }

            for (int i = 0; i < _spawned.Count; ++i)
            {
                if (_spawned[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();

            if (_template != null)
            {
                UnityEngine.Object.DestroyImmediate(_template);
                _template = null;
            }

            PresentationFixtureFactory.ReleaseCanvasIfUnused();

            if (_scope != null)
            {
                _scope.Dispose();
                _scope = null;
            }
        }
    }
}
