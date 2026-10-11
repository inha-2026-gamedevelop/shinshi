// System
using System.Collections;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.Tests
{
    public sealed class DamageNumberPoolPlayModeTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int EPOCH = 1;
        private const double NOW = 1.0;
        private GameObject _root;
        private GameObject _cameraRoot;
        private DamageNumberPool _pool;

        /****************************************
        *                Setup
        ****************************************/

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _cameraRoot = new GameObject("DamageNumberTestCamera", typeof(Camera));
            Camera camera = _cameraRoot.GetComponent<Camera>();
            camera.transform.position = new Vector3(0f, 1.5f, -10f);
            _root = new GameObject("DamageNumberTestPool", typeof(RectTransform));
            _pool = _root.AddComponent<DamageNumberPool>();
            _pool.Initialize(camera);
            Assert.IsTrue(_pool.IsInitialized);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_root);
            Object.Destroy(_cameraRoot);
            yield return null;
        }

        /****************************************
        *                Tests
        ****************************************/

        [UnityTest]
        public IEnumerator Show_BeyondCapacity_DropsDisplayAndExpiresWithoutGrowing()
        {
            int children = _root.GetComponentsInChildren<RectTransform>(true).Length;
            for (int i = 0; i < _pool.Capacity; ++i)
            {
                DamageFeedback feedback = Feedback((uint)(i + 1));
                Assert.IsTrue(_pool.Show(in feedback));
            }
            DamageFeedback overflow = Feedback((uint)(_pool.Capacity + 1));
            Assert.IsFalse(_pool.Show(in overflow));
            Assert.AreEqual(_pool.Capacity, _pool.ActiveCount);
            Assert.AreEqual(children, _root.GetComponentsInChildren<RectTransform>(true).Length);
            yield return new WaitForSecondsRealtime(1f);
            Assert.AreEqual(0, _pool.ActiveCount);
            Assert.IsFalse(_pool.enabled);
            Assert.AreEqual(children, _root.GetComponentsInChildren<RectTransform>(true).Length);
        }

        [UnityTest]
        public IEnumerator Show_RepeatedUse_ReusesTweenAndDisableClearsSlots()
        {
            DamageFeedback feedback = Feedback(1);
            Assert.IsTrue(_pool.Show(in feedback));
            object firstTween = FirstTween();
            _pool.Clear();
            Assert.AreEqual(0, _pool.ActiveCount);
            Assert.IsFalse(_pool.enabled);
            Assert.IsTrue(_pool.Show(in feedback));
            Assert.AreSame(firstTween, FirstTween());
            _root.SetActive(false);
            yield return null;
            Assert.AreEqual(0, _pool.ActiveCount);
            _root.SetActive(true);
            yield return null;
            Assert.IsFalse(_pool.enabled);
        }

        [UnityTest]
        public IEnumerator Show_BehindCameraAndInvalidValues_RejectsWithoutActivatingSlots()
        {
            DamageFeedback behind = new DamageFeedback(EPOCH, 1, 2, new Vector3(0f, 0f, -20f), 100f, false, NOW);
            DamageFeedback invalid = new DamageFeedback(EPOCH, 2, 2, Vector3.zero, float.NaN, false, NOW);
            Assert.IsFalse(_pool.Show(in behind));
            Assert.IsFalse(_pool.Show(in invalid));
            Assert.AreEqual(0, _pool.ActiveCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Destroy_ActivePool_KillsOwnedTweens()
        {
            DamageFeedback feedback = Feedback(1);
            Assert.IsTrue(_pool.Show(in feedback));
            object tween = FirstTween();
            Object.Destroy(_root);
            yield return null;
            // 설치 DLL의 active는 필드가 아니다. 공개 확장 API를 호출해 종료 의미를 검증한다
            System.Type extensions = tween.GetType().Assembly.GetType("DG.Tweening.TweenExtensions");
            MethodInfo isActive = extensions.GetMethod("IsActive", BindingFlags.Static | BindingFlags.Public);
            Assert.IsNotNull(isActive);
            Assert.IsFalse((bool)isActive.Invoke(null, new[] { tween }));
            _root = null;
        }

        [UnityTest]
        public IEnumerator BindCamera_AfterMissingCamera_ShowsWithoutCreatingAnotherPool()
        {
            Camera camera = _cameraRoot.GetComponent<Camera>();
            _pool.BindCamera(null);
            int children = _root.GetComponentsInChildren<RectTransform>(true).Length;
            DamageFeedback feedback = Feedback(1);
            Assert.IsFalse(_pool.Show(in feedback));
            Assert.IsFalse(_pool.HasCamera);
            _pool.BindCamera(camera);
            Assert.IsTrue(_pool.HasCamera);
            Assert.IsTrue(_pool.Show(in feedback));
            Assert.AreEqual(children, _root.GetComponentsInChildren<RectTransform>(true).Length);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BindCamera_AfterDestroyedCamera_RecoversWithExistingSlots()
        {
            int children = _root.GetComponentsInChildren<RectTransform>(true).Length;
            DamageFeedback feedback = Feedback(1);
            Assert.IsTrue(_pool.Show(in feedback));
            Object.Destroy(_cameraRoot);
            yield return null;
            Assert.IsFalse(_pool.HasCamera);
            Assert.IsFalse(_pool.Show(in feedback));
            _cameraRoot = new GameObject("ReplacementDamageCamera", typeof(Camera));
            Camera replacement = _cameraRoot.GetComponent<Camera>();
            replacement.transform.position = new Vector3(0f, 1.5f, -10f);
            _pool.BindCamera(replacement);
            Assert.IsTrue(_pool.IsBoundToCamera(replacement));
            Assert.IsTrue(_pool.Show(in feedback));
            Assert.AreEqual(children, _root.GetComponentsInChildren<RectTransform>(true).Length);
        }

        [UnityTest]
        public IEnumerator Initialize_RecreatedPool_PreservesCapacityWithoutAccumulatingReservation()
        {
            System.Type dotween = FirstTween().GetType().Assembly.GetType("DG.Tweening.DOTween");
            PropertyInfo tweeners = dotween.GetProperty("tweenersCapacity", BindingFlags.Static | BindingFlags.Public);
            PropertyInfo sequences = dotween.GetProperty("sequencesCapacity", BindingFlags.Static | BindingFlags.Public);
            int beforeTweeners = (int)tweeners.GetValue(null);
            int beforeSequences = (int)sequences.GetValue(null);
            var extraRoot = new GameObject("ExtraDamageNumberPool", typeof(RectTransform));
            DamageNumberPool extra = extraRoot.AddComponent<DamageNumberPool>();
            extra.Initialize(_cameraRoot.GetComponent<Camera>());
            int reservedTweeners = (int)tweeners.GetValue(null);
            int reservedSequences = (int)sequences.GetValue(null);
            Assert.GreaterOrEqual(reservedTweeners, beforeTweeners);
            Assert.GreaterOrEqual(reservedSequences, beforeSequences);
            Object.Destroy(extraRoot);
            yield return null;
            extraRoot = new GameObject("RecreatedDamageNumberPool", typeof(RectTransform));
            try
            {
                extra = extraRoot.AddComponent<DamageNumberPool>();
                extra.Initialize(_cameraRoot.GetComponent<Camera>());
                Assert.AreEqual(reservedTweeners, (int)tweeners.GetValue(null));
                Assert.AreEqual(reservedSequences, (int)sequences.GetValue(null));
            }
            finally
            {
                Object.Destroy(extraRoot);
            }
            yield return null;
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static DamageFeedback Feedback(uint sequence) => new DamageFeedback(EPOCH, sequence, 2, Vector3.zero, 100f, true, NOW);

        private object FirstTween()
        {
            var slots = (System.Array)typeof(DamageNumberPool).GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_pool);
            object slot = slots.GetValue(0);
            return slot.GetType().GetField("Tween").GetValue(slot);
        }
    }
}
