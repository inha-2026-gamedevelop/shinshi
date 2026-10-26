// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// Third Party
using NUnit.Framework;

namespace Shinshi.Tests
{
    public sealed class DevHitReactionTests
    {
        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;
        private const float TEST_ORIGIN = 10000f;
        private readonly List<GameObject> _objects = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject item in _objects)
            {
                UnityEngine.Object.Destroy(item);
            }
            _objects.Clear();
            yield return null;
        }

        [Test]
        public void Recoil_OppositeHitsRestorePoseAndKeepRootFixed()
        {
            GameObject root = Track(new GameObject("RecoilTest"));
            Animator animator = root.AddComponent<Animator>();
            animator.enabled = false;
            Transform spine = Bone(root.transform, "Spine");
            Transform head = Bone(spine, "Head");
            Bone(spine, "LeftArm");
            Bone(spine, "RightArm");
            Component recoil = root.AddComponent(DevType("DevMonsterHitRecoil"));
            Invoke(recoil, "Init", animator);
            Quaternion initial = spine.localRotation;
            Invoke(recoil, "Play", Vector3.right, false);
            SetField(recoil, "_phase", 0.16f);
            InvokePrivate(recoil, "LateUpdate");
            Vector3 rightPose = spine.up;
            Assert.That(rightPose.x, Is.GreaterThan(0.1f));
            Assert.That(Quaternion.Angle(head.localRotation, Quaternion.identity), Is.GreaterThan(0.1f));
            Assert.That(root.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(root.transform.rotation, Is.EqualTo(Quaternion.identity));
            Invoke(recoil, "Play", Vector3.left, false);
            SetField(recoil, "_phase", 0.16f);
            InvokePrivate(recoil, "LateUpdate");
            Assert.That(spine.up.x, Is.EqualTo(-rightPose.x).Within(0.001f));
            Invoke(recoil, "StopAndRestore");
            Assert.That(Quaternion.Angle(spine.localRotation, initial), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(head.localRotation, Quaternion.identity), Is.LessThan(0.001f));
        }

        [UnityTest]
        public IEnumerator Recoil_HitLagHoldsTimeAndDisableRestoresBones()
        {
            GameObject root = Track(new GameObject("RecoilLagTest"));
            Animator animator = root.AddComponent<Animator>();
            animator.enabled = false;
            Transform spine = Bone(root.transform, "Spine");
            Component recoil = root.AddComponent(DevType("DevMonsterHitRecoil"));
            Invoke(recoil, "Init", animator);
            Invoke(recoil, "Play", Vector3.right, false);
            Invoke(recoil, "SetLagged", true);
            yield return new WaitForSeconds(0.15f);
            float held = (float)recoil.GetType().GetField("_phase", PRIVATE_INSTANCE).GetValue(recoil);
            Assert.That(held, Is.EqualTo(0f).Within(0.001f));
            Invoke(recoil, "SetLagged", false);
            yield return new WaitForSeconds(0.07f);
            float resumed = (float)recoil.GetType().GetField("_phase", PRIVATE_INSTANCE).GetValue(recoil);
            Assert.That(resumed, Is.GreaterThan(0.05f).And.LessThan(1f));
            // 코루틴은 Update와 LateUpdate 사이에 재개되므로 렌더 단계의 자세를 여기서 평가한다.
            InvokePrivate(recoil, "LateUpdate");
            Assert.That(spine.up.x, Is.GreaterThan(0.05f));
            ((Behaviour)recoil).enabled = false;
            Assert.That(Quaternion.Angle(spine.localRotation, Quaternion.identity), Is.LessThan(0.001f));
        }

        [UnityTest]
        public IEnumerator Landing_ActualGroundContactPlaysOnceAcrossSecondFall()
        {
            Component fx = CreateFx();
            Rigidbody body = CreateBody();
            Component landing = body.gameObject.AddComponent(DevType("DevCorpseLanding"));
            Invoke(landing, "BeginDirect", fx, new[] { body.GetComponent<Collider>() });
            yield return WaitForLanding(fx);
            Assert.That(ReadCount(fx), Is.EqualTo(1));
            body.position = new Vector3(TEST_ORIGIN, 3f, TEST_ORIGIN);
            body.linearVelocity = Vector3.zero;
            body.WakeUp();
            yield return new WaitForSeconds(1f);
            Assert.That(ReadCount(fx), Is.EqualTo(1));
            Assert.That((bool)landing.GetType().GetProperty("HasLanded").GetValue(landing), Is.True);
        }

        [UnityTest]
        public IEnumerator Landing_DisabledMarkerDoesNotPlay()
        {
            Component fx = CreateFx();
            Rigidbody body = CreateBody();
            Component landing = body.gameObject.AddComponent(DevType("DevCorpseLanding"));
            Invoke(landing, "BeginDirect", fx, new[] { body.GetComponent<Collider>() });
            ((Behaviour)landing).enabled = false;
            yield return new WaitForSeconds(1f);
            Assert.That(body.position.y, Is.LessThan(1f));
            Assert.That(ReadCount(fx), Is.Zero);
        }

        [UnityTest]
        public IEnumerator Landing_WallContactIsIgnoredThenGroundPlays()
        {
            Component fx = CreateFx();
            Rigidbody body = CreateBody();
            GameObject wall = Track(new GameObject("LandingTestWall"));
            wall.transform.position = new Vector3(TEST_ORIGIN + 2f, 3f, TEST_ORIGIN);
            wall.AddComponent<BoxCollider>().size = new Vector3(0.5f, 5f, 5f);
            Component landing = body.gameObject.AddComponent(DevType("DevCorpseLanding"));
            Invoke(landing, "BeginDirect", fx, new[] { body.GetComponent<Collider>() });
            body.useGravity = false;
            body.linearVelocity = Vector3.right * 5f;
            yield return new WaitForSeconds(0.6f);
            Assert.That(body.position.x, Is.GreaterThan(TEST_ORIGIN + 0.5f));
            Assert.That(ReadCount(fx), Is.Zero);
            body.linearVelocity = Vector3.zero;
            body.useGravity = true;
            yield return WaitForLanding(fx);
            Assert.That(ReadCount(fx), Is.EqualTo(1));
        }

        private Component CreateFx()
        {
            GameObject prefab = Resources.Load<GameObject>("DevHitReactions/PF_DevCorpseLandingFx");
            Assert.That(prefab, Is.Not.Null);
            return Track(UnityEngine.Object.Instantiate(prefab)).GetComponent(DevType("DevCorpseLandingFx"));
        }

        [UnityTest]
        public IEnumerator Landing_MonsterWithoutModelUsesFirstContactFeedback()
        {
            Component fx = CreateFx();
            Rigidbody initialBody = CreateBody();
            GameObject item = initialBody.gameObject;
            UnityEngine.Object.DestroyImmediate(initialBody);
            MeshRenderer renderer = item.AddComponent<MeshRenderer>();
            GameObject reference = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            reference.transform.position = Vector3.one * (TEST_ORIGIN * 2f);
            renderer.sharedMaterial = reference.GetComponent<Renderer>().sharedMaterial;
            Component monster = item.AddComponent(DevType("DevHitFeedbackMonster"));
            Invoke(monster, "Init", 1, null, 0f, null, 0f, 0f);
            Invoke(monster, "SetLandingFeedback", fx);
            ScriptableObject profile = ScriptableObject.CreateInstance(DevType("HitFeedbackProfile"));
            try
            {
                profile.GetType().GetField("_flyMultiplier", PRIVATE_INSTANCE).SetValue(profile, 0.4f);
                object[] args = { item.transform.position - Vector3.forward, profile, false, false };
                monster.GetType().GetMethod("TakeHit").Invoke(monster, args);
                Assert.That((bool)args[3], Is.True);
                yield return WaitForLanding(fx);
                Assert.That(ReadCount(fx), Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.Destroy(profile);
            }
        }

        private Rigidbody CreateBody()
        {
            GameObject floor = Track(new GameObject("LandingTestFloor"));
            floor.transform.position = new Vector3(TEST_ORIGIN, -0.5f, TEST_ORIGIN);
            floor.AddComponent<BoxCollider>().size = new Vector3(20f, 1f, 20f);
            GameObject item = Track(new GameObject("LandingTestBody"));
            item.transform.position = new Vector3(TEST_ORIGIN, 3f, TEST_ORIGIN);
            item.AddComponent<BoxCollider>();
            return item.AddComponent<Rigidbody>();
        }

        private IEnumerator WaitForLanding(Component fx)
        {
            float deadline = Time.time + 2f;
            while ((ReadCount(fx) == 0) && (Time.time < deadline))
            {
                yield return null;
            }
        }

        private static Type DevType(string name)
        {
            // 개발 어셈블리의 테스트 전용 참조를 런타임 어셈블리 계약에 추가하지 않는다.
            Type type = Type.GetType("Shinshi.Dev." + name + ", Shinshi.Dev");
            Assert.That(type, Is.Not.Null);
            return type;
        }

        private static int ReadCount(Component fx) { return (int)fx.GetType().GetProperty("PlayedCount").GetValue(fx); }
        private static void Invoke(Component target, string name, params object[] args) { target.GetType().GetMethod(name).Invoke(target, args); }
        private static void InvokePrivate(Component target, string name) { target.GetType().GetMethod(name, PRIVATE_INSTANCE).Invoke(target, null); }
        private static void SetField(Component target, string name, object value) { target.GetType().GetField(name, PRIVATE_INSTANCE).SetValue(target, value); }
        private GameObject Track(GameObject item) { _objects.Add(item); return item; }

        private static Transform Bone(Transform parent, string name)
        {
            Transform bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            return bone;
        }
    }
}
