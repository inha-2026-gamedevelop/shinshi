// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.Tests
{
    public sealed class DamageNumberBinderPlayModeTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int EPOCH = 7;
        private const int TARGET_ID = 2;
        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<OwnerFixture> _owners = new List<OwnerFixture>();
        private GameObject _cameraRoot;
        private Camera _camera;

        private sealed class OwnerFixture
        {
            public GameObject Root;
            public NetworkIdentity Identity;
            public PlayerInputState Input;
            public DamageNumberBinder Binder;
        }

        /****************************************
        *                Setup
        ****************************************/

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _cameraRoot = new GameObject("DamageNumberBinderTestCamera", typeof(Camera));
            _camera = _cameraRoot.GetComponent<Camera>();
            _camera.transform.position = new Vector3(0f, 1.5f, -10f);
            // 이 시험은 표현 소유권과 수명을 본다. 투영 카메라는 풀의 공개 계약으로 고정한다
            _camera.enabled = false;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = 0; i < _owners.Count; ++i)
            {
                OwnerFixture owner = _owners[i];
                if (owner.Binder != null)
                {
                    owner.Binder.Unbind();
                }
                if (owner.Root != null)
                {
                    UnityEngine.Object.Destroy(owner.Root);
                }
            }
            _owners.Clear();
            if (_cameraRoot != null)
            {
                UnityEngine.Object.Destroy(_cameraRoot);
            }
            yield return null;
        }

        /****************************************
        *                Tests
        ****************************************/

        [UnityTest]
        public IEnumerator Bind_TwoOwnersWithSameEpochAndSequence_ActivatesOnlyReceivingOwnersPool()
        {
            OwnerFixture ownerA = CreateOwner("DamageOwnerA");
            OwnerFixture ownerB = CreateOwner("DamageOwnerB");
            DamageNumberPool poolA = Pool(ownerA.Binder);
            DamageNumberPool poolB = Pool(ownerB.Binder);
            Assert.AreNotSame(poolA, poolB);
            Assert.AreEqual(1, SubscriberCount(ownerA));
            Assert.AreEqual(1, SubscriberCount(ownerB));

            ReceiveFeedback(ownerA.Input, 1);
            Assert.AreEqual(1, poolA.ActiveCount);
            Assert.AreEqual(0, poolB.ActiveCount);
            ReceiveFeedback(ownerB.Input, 1);
            Assert.AreEqual(1, poolA.ActiveCount);
            Assert.AreEqual(1, poolB.ActiveCount, "다른 소유자의 같은 순번은 독립적으로 승인한다");
            ReceiveFeedback(ownerA.Input, 2);
            Assert.AreEqual(2, poolA.ActiveCount);
            Assert.AreEqual(1, poolB.ActiveCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OnStopAuthority_RemovesSubscriptionAndDestroysOwnedPool()
        {
            yield return StopAndAssertCleanup(true);
        }

        [UnityTest]
        public IEnumerator OnStopClient_RemovesSubscriptionAndDestroysOwnedPool()
        {
            yield return StopAndAssertCleanup(false);
        }

        [UnityTest]
        public IEnumerator Unbind_RebindInSameEpoch_DoesNotReplayOldFeedbackOrDuplicateSubscription()
        {
            OwnerFixture owner = CreateOwner("DamageOwnerRebind");
            DamageNumberPool firstPool = Pool(owner.Binder);
            GameObject retiredRoot = firstPool.gameObject;
            ReceiveFeedback(owner.Input, 7);
            Assert.AreEqual(1, firstPool.ActiveCount);

            owner.Binder.Unbind();
            Assert.AreEqual(0, SubscriberCount(owner));
            Assert.IsNull(Pool(owner.Binder));
            Assert.AreEqual(0, firstPool.ActiveCount);
            Assert.IsFalse(retiredRoot.activeSelf);

            owner.Binder.Bind(owner.Input);
            DamageNumberPool rebound = Pool(owner.Binder);
            Assert.IsNotNull(rebound);
            Assert.AreNotSame(firstPool, rebound);
            rebound.BindCamera(_camera);
            owner.Binder.Bind(owner.Input);
            Assert.AreSame(rebound, Pool(owner.Binder), "같은 소스의 중복 Bind는 풀을 추가하지 않는다");
            Assert.AreEqual(1, SubscriberCount(owner));
            ReceiveFeedback(owner.Input, 7);
            Assert.AreEqual(0, rebound.ActiveCount, "풀을 다시 만들더라도 같은 epoch의 표시 순번을 되감지 않는다");
            ReceiveFeedback(owner.Input, 8);
            Assert.AreEqual(1, rebound.ActiveCount);
            Assert.AreEqual(0, firstPool.ActiveCount);

            yield return null;
            Assert.IsTrue(retiredRoot == null, "퇴역 풀은 프레임이 끝나면 실제로 파괴된다");
            Assert.IsTrue(rebound != null);
            owner.Binder.Unbind();
            Assert.AreEqual(0, SubscriberCount(owner));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private OwnerFixture CreateOwner(string name)
        {
            var owner = new OwnerFixture();
            owner.Root = new GameObject(name);
            _owners.Add(owner);
            owner.Root.SetActive(false);
            owner.Identity = owner.Root.AddComponent<NetworkIdentity>();
            owner.Input = owner.Root.AddComponent<PlayerInputState>();
            owner.Input.enabled = false;
            owner.Root.GetComponent<PlayerCombatState>().enabled = false;
            owner.Binder = owner.Root.AddComponent<DamageNumberBinder>();
            owner.Root.SetActive(true);

            // 기존 미스폰 Mirror fixture와 같이 연결 역할만 설정한다. 입력 Update나 실제 연결은 실행하지 않는다
            PropertyInfo identityProperty = typeof(NetworkBehaviour).GetProperty("netIdentity");
            identityProperty.SetValue(owner.Input, owner.Identity);
            identityProperty.SetValue(owner.Root.GetComponent<PlayerCombatState>(), owner.Identity);
            typeof(NetworkIdentity).GetProperty("isClient").SetValue(owner.Identity, true);
            typeof(NetworkIdentity).GetProperty("isOwned").SetValue(owner.Identity, true);
            typeof(PlayerInputState).GetField("_sessionEpoch", PRIVATE_INSTANCE).SetValue(owner.Input, EPOCH);
            owner.Binder.Bind(owner.Input);
            DamageNumberPool pool = Pool(owner.Binder);
            if (pool != null)
            {
                Assert.IsTrue(pool.IsInitialized);
                pool.BindCamera(_camera);
            }
            return owner;
        }

        private IEnumerator StopAndAssertCleanup(bool authority)
        {
            OwnerFixture owner = CreateOwner(authority ? "DamageAuthorityStop" : "DamageClientStop");
            DamageNumberPool pool = Pool(owner.Binder);
            Assert.IsNotNull(pool);
            GameObject retiredRoot = pool.gameObject;
            ReceiveFeedback(owner.Input, 1);
            Assert.AreEqual(1, pool.ActiveCount);

            if (authority)
            {
                typeof(NetworkIdentity).GetProperty("isOwned").SetValue(owner.Identity, false);
                owner.Input.OnStopAuthority();
            }
            else
            {
                owner.Input.OnStopClient();
                typeof(NetworkIdentity).GetProperty("isClient").SetValue(owner.Identity, false);
            }
            Assert.AreEqual(0, SubscriberCount(owner), "종료 콜백이 표시 이벤트 구독까지 해제한다");
            Assert.IsNull(Pool(owner.Binder));
            Assert.IsFalse(retiredRoot.activeSelf);
            Assert.AreEqual(0, pool.ActiveCount);

            // 수신 이벤트 자체는 도착하게 해 구독 해제를 권한 가드의 결과와 구별한다
            typeof(NetworkIdentity).GetProperty("isOwned").SetValue(owner.Identity, true);
            int delivered = 0;
            owner.Input.DamageFeedbackReceived += feedback => ++delivered;
            ReceiveFeedback(owner.Input, 2);
            Assert.AreEqual(1, delivered);
            Assert.AreEqual(0, SubscriberCount(owner));
            Assert.AreEqual(0, pool.ActiveCount);
            Assert.IsNull(Pool(owner.Binder));
            owner.Binder.Unbind();
            yield return null;
            Assert.IsTrue(retiredRoot == null, "종료 콜백이 해제한 풀은 다음 프레임까지 남지 않는다");
        }

        private static DamageNumberPool Pool(DamageNumberBinder binder)
        {
            return (DamageNumberPool)typeof(DamageNumberBinder).GetField("_pool", PRIVATE_INSTANCE).GetValue(binder);
        }

        private static int SubscriberCount(OwnerFixture owner)
        {
            var callback = (Delegate)typeof(PlayerInputState).GetField("DamageFeedbackReceived", PRIVATE_INSTANCE).GetValue(owner.Input);
            if (callback == null)
            {
                return 0;
            }
            Delegate[] handlers = callback.GetInvocationList();
            int count = 0;
            for (int i = 0; i < handlers.Length; ++i)
            {
                if (ReferenceEquals(handlers[i].Target, owner.Binder))
                {
                    ++count;
                }
            }
            return count;
        }

        private static void ReceiveFeedback(PlayerInputState input, uint sequence)
        {
            MethodInfo body = null;
            MethodInfo[] methods = typeof(PlayerInputState).GetMethods(PRIVATE_INSTANCE | BindingFlags.Public);
            for (int i = 0; i < methods.Length; ++i)
            {
                if (methods[i].Name.StartsWith("UserCode_TargetDamageFeedback", StringComparison.Ordinal))
                {
                    body = methods[i];
                    break;
                }
            }
            Assert.IsNotNull(body, "Mirror Weaver의 실제 RPC 수신 본문이 있어야 한다");
            body.Invoke(input, new object[] { EPOCH, sequence, TARGET_ID, Vector3.zero, 100f, false, NetworkTime.time });
        }
    }
}
