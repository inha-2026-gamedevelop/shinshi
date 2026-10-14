// System
using System;
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Tests
{
    public sealed class ClientPrewarmRunnerTests
    {
        private sealed class CountingTarget : MonoBehaviour, IPrewarmTarget
        {
            public static int BeginCount;
            public static int EndCount;

            public void BeginPrewarm() { ++BeginCount; }
            public void TickPrewarm(float elapsedSeconds) { }
            public void EndPrewarm() { ++EndCount; }
        }

        private sealed class ThrowingTarget : MonoBehaviour, IPrewarmTarget
        {
            public void BeginPrewarm() { throw new InvalidOperationException("begin"); }
            public void TickPrewarm(float elapsedSeconds) { }
            public void EndPrewarm() { }
        }

        private const int   VISIBLE_FRAMES     = 2;
        private const float BUDGET_MS          = 8f;
        private const float LONG_TIMEOUT       = 30f;
        private const int   CONCURRENT         = 4;
        private const int   MAX_WAIT_FRAMES    = 600;
        private const int   MANY_ITEMS         = 8;
        private const int   VARIANTS_PER_FRAME = 1;

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        private PrewarmCatalogSO _lastCatalog;

        [SetUp]
        public void SetUp()
        {
            CountingTarget.BeginCount = 0;
            CountingTarget.EndCount   = 0;

            // 실패 경로 시험이 오류 로그를 남기므로 로그를 시험 실패로 치지 않는다
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            for (int i = 0; i < _created.Count; ++i)
            {
                if (_created[i] != null)
                {
                    UnityEngine.Object.Destroy(_created[i]);
                }
            }

            _created.Clear();
        }

        private GameObject MakePrefab(string objectName, Type targetType)
        {
            var go = new GameObject(objectName);
            if (targetType != null)
            {
                go.AddComponent(targetType);
            }

            go.SetActive(false);
            _created.Add(go);
            return go;
        }

        private ClientPrewarmRunner MakeRunner(float timeoutSeconds, params PrewarmCatalogEntry[] entries)
        {
            PrewarmCatalogSO catalog = ScriptableObject.CreateInstance<PrewarmCatalogSO>();
            catalog.Configure(entries, BUDGET_MS, timeoutSeconds, CONCURRENT);
            _created.Add(catalog);
            _lastCatalog = catalog;

            var cameraObject = new GameObject("PrewarmTestCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            _created.Add(cameraObject);

            var runnerObject = new GameObject("PrewarmTestRunner");
            _created.Add(runnerObject);
            ClientPrewarmRunner runner = runnerObject.AddComponent<ClientPrewarmRunner>();
            runner.Configure(catalog, camera);
            return runner;
        }

        private static PrewarmCatalogEntry Entry(GameObject prefab, EPrewarmKind kind)
        {
            return new PrewarmCatalogEntry(prefab, kind, VISIBLE_FRAMES, null, false, false);
        }

        private static IEnumerator WaitUntilFinished(ClientPrewarmRunner runner)
        {
            for (int i = 0; i < MAX_WAIT_FRAMES; ++i)
            {
                if (!runner.IsRunning)
                {
                    yield break;
                }

                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Run_NormalItems_InvokesCallbackOnce()
        {
            ClientPrewarmRunner runner = MakeRunner(LONG_TIMEOUT,
                Entry(MakePrefab("Model", null), EPrewarmKind.Model),
                Entry(MakePrefab("Scripted", typeof(CountingTarget)), EPrewarmKind.Scripted));
            int calls = 0;

            runner.Run(() => ++calls);
            yield return WaitUntilFinished(runner);
            yield return null;

            Assert.AreEqual(1, calls);
            Assert.AreEqual(1, CountingTarget.BeginCount);
            Assert.AreEqual(1, CountingTarget.EndCount);
        }

        [UnityTest]
        public IEnumerator Run_ThrowingTarget_ContinuesToNextItemAndInvokesCallbackOnce()
        {
            ClientPrewarmRunner runner = MakeRunner(LONG_TIMEOUT,
                Entry(MakePrefab("Throwing", typeof(ThrowingTarget)), EPrewarmKind.Scripted),
                Entry(MakePrefab("Scripted", typeof(CountingTarget)), EPrewarmKind.Scripted));
            int calls = 0;

            runner.Run(() => ++calls);
            yield return WaitUntilFinished(runner);
            yield return null;

            Assert.AreEqual(1, calls);
            Assert.AreEqual(1, CountingTarget.BeginCount);
            Assert.AreEqual(1, CountingTarget.EndCount);
        }

        [UnityTest]
        public IEnumerator Run_ZeroTimeout_SkipsItemsAndInvokesCallbackOnce()
        {
            ClientPrewarmRunner runner = MakeRunner(0f,
                Entry(MakePrefab("Scripted", typeof(CountingTarget)), EPrewarmKind.Scripted),
                Entry(MakePrefab("Model", null), EPrewarmKind.Model));
            int calls = 0;

            runner.Run(() => ++calls);
            yield return WaitUntilFinished(runner);
            yield return null;

            Assert.AreEqual(1, calls);
            Assert.AreEqual(0, CountingTarget.BeginCount);
        }

        [UnityTest]
        public IEnumerator Run_ShaderPhaseTimedOut_SkipsAllItemsAndInvokesCallbackOnce()
        {
            // 제한 시간 0이면 셰이더 단계는 워밍 전에 타임아웃으로 끝난다, 이후 스폰 단계가 멈추지 않아야 한다
            ClientPrewarmRunner runner = MakeRunner(0f,
                Entry(MakePrefab("Scripted", typeof(CountingTarget)), EPrewarmKind.Scripted),
                Entry(MakePrefab("Model", null), EPrewarmKind.Model));
            _lastCatalog.ConfigureShaderWarmup(new ShaderVariantCollection(), VARIANTS_PER_FRAME);
            int calls = 0;

            runner.Run(() => ++calls);
            yield return WaitUntilFinished(runner);
            yield return null;

            Assert.IsFalse(runner.IsRunning);
            Assert.AreEqual(1, calls);
            Assert.AreEqual(0, CountingTarget.BeginCount);
        }

        [UnityTest]
        public IEnumerator Run_CalledAfterFinished_InvokesCallbackImmediately()
        {
            ClientPrewarmRunner runner = MakeRunner(LONG_TIMEOUT,
                Entry(MakePrefab("Model", null), EPrewarmKind.Model));
            int first = 0;
            int second = 0;

            runner.Run(() => ++first);
            yield return WaitUntilFinished(runner);
            runner.Run(() => ++second);

            Assert.AreEqual(1, first);
            Assert.AreEqual(1, second);
        }

        [UnityTest]
        public IEnumerator Run_RunnerDeactivatedThenReactivated_InvokesCallbackOnce()
        {
            var entries = new PrewarmCatalogEntry[MANY_ITEMS];
            for (int i = 0; i < MANY_ITEMS; ++i)
            {
                entries[i] = Entry(MakePrefab("Model" + i, null), EPrewarmKind.Model);
            }

            ClientPrewarmRunner runner = MakeRunner(LONG_TIMEOUT, entries);
            int calls = 0;

            runner.Run(() => ++calls);
            runner.gameObject.SetActive(false);
            yield return null;
            Assert.AreEqual(0, calls);

            runner.gameObject.SetActive(true);
            yield return WaitUntilFinished(runner);
            yield return null;

            Assert.IsFalse(runner.IsRunning);
            Assert.AreEqual(1, calls);
        }

        [UnityTest]
        public IEnumerator Run_RunnerDestroyedMidRun_DoesNotInvokeCallback()
        {
            ClientPrewarmRunner runner = MakeRunner(LONG_TIMEOUT,
                Entry(MakePrefab("Model", null), EPrewarmKind.Model));
            int calls = 0;

            runner.Run(() => ++calls);
            yield return null;
            UnityEngine.Object.Destroy(runner.gameObject);
            yield return null;
            yield return null;

            Assert.AreEqual(0, calls);
        }
    }
}
