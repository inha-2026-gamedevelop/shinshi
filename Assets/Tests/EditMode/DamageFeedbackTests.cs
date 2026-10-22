// System.Reflection
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Art;

namespace Shinshi.Tests
{
    public sealed class DamageFeedbackTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int EPOCH = 5;
        private const double NOW = 10.0;
        private const int TARGET_ID = 2;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryAccept_DuplicateAndOlderSequence_AcceptsOnlyFirst()
        {
            var receiver = new DamageFeedbackReceiver();
            DamageFeedback first = Feedback(2);
            DamageFeedback older = Feedback(1);
            Assert.IsTrue(receiver.TryAccept(in first, EPOCH, NOW));
            Assert.IsFalse(receiver.TryAccept(in first, EPOCH, NOW));
            Assert.IsFalse(receiver.TryAccept(in older, EPOCH, NOW));
        }

        [Test]
        public void TryAccept_WrongEpochExpiredAndFuture_RejectsWithoutBlockingValidSequence()
        {
            var receiver = new DamageFeedbackReceiver();
            DamageFeedback wrongEpoch = Feedback(1, epoch: EPOCH - 1);
            DamageFeedback expired = Feedback(1, serverTime: NOW - DamageFeedbackRules.MAX_AGE_SECONDS - 0.01);
            DamageFeedback future = Feedback(1, serverTime: NOW + DamageFeedbackRules.FUTURE_TOLERANCE_SECONDS + 0.01);
            DamageFeedback valid = Feedback(1);
            Assert.IsFalse(receiver.TryAccept(in wrongEpoch, EPOCH, NOW));
            Assert.IsFalse(receiver.TryAccept(in expired, EPOCH, NOW));
            Assert.IsFalse(receiver.TryAccept(in future, EPOCH, NOW));
            Assert.IsTrue(receiver.TryAccept(in valid, EPOCH, NOW));
        }

        [TestCase(0.033)]
        [TestCase(0.25)]
        [TestCase(0.8)]
        [TestCase(1.5)]
        public void ReceptionTime_DynamicInterpolationBuffer_AcceptsFreshAndRejectsExpired(double buffer)
        {
            double received = DamageFeedbackRules.ReceptionTime(NOW - buffer, buffer);
            var receiver = new DamageFeedbackReceiver();
            DamageFeedback fresh = Feedback(1);
            DamageFeedback expired = Feedback(2, serverTime: NOW - DamageFeedbackRules.MAX_AGE_SECONDS - 0.01);
            DamageFeedback future = Feedback(2, serverTime: NOW + DamageFeedbackRules.FUTURE_TOLERANCE_SECONDS + 0.01);
            Assert.AreEqual(NOW, received, 0.000001);
            Assert.IsTrue(receiver.TryAccept(in fresh, EPOCH, received));
            Assert.IsFalse(receiver.TryAccept(in expired, EPOCH, received));
            Assert.IsFalse(receiver.TryAccept(in future, EPOCH, received));
        }

        [TestCase(-0.01)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void ReceptionTime_InvalidBuffer_Rejects(double buffer)
        {
            double received = DamageFeedbackRules.ReceptionTime(NOW, buffer);
            DamageFeedback feedback = Feedback(1);
            Assert.IsFalse(new DamageFeedbackReceiver().TryAccept(in feedback, EPOCH, received));
        }

        [Test]
        public void ReceptionTime_InvalidTimelineAndOverflow_Rejects()
        {
            Assert.IsTrue(double.IsNaN(DamageFeedbackRules.ReceptionTime(double.NaN, 0)));
            Assert.IsTrue(double.IsNaN(DamageFeedbackRules.ReceptionTime(double.MaxValue, double.MaxValue)));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(0f)]
        [TestCase(-1f)]
        public void IsValid_InvalidAmount_Rejects(float amount)
        {
            DamageFeedback feedback = Feedback(1, amount: amount);
            Assert.IsFalse(DamageFeedbackRules.IsValid(in feedback));
        }

        [Test]
        public void IsValid_InvalidPositionTimeAndIds_Rejects()
        {
            DamageFeedback badPosition = new DamageFeedback(EPOCH, 1, TARGET_ID, new Vector3(float.NaN, 0f, 0f), 1f, false, NOW);
            DamageFeedback badTime = Feedback(1, serverTime: double.NaN);
            DamageFeedback badSequence = Feedback(0);
            DamageFeedback badTarget = new DamageFeedback(EPOCH, 1, 0, Vector3.zero, 1f, false, NOW);
            Assert.IsFalse(DamageFeedbackRules.IsValid(in badPosition));
            Assert.IsFalse(DamageFeedbackRules.IsValid(in badTime));
            Assert.IsFalse(DamageFeedbackRules.IsValid(in badSequence));
            Assert.IsFalse(DamageFeedbackRules.IsValid(in badTarget));
        }

        [Test]
        public void TryAccept_NewEpoch_RestartsSequenceAndRejectsOldEpoch()
        {
            var receiver = new DamageFeedbackReceiver();
            DamageFeedback first = Feedback(10);
            DamageFeedback next = Feedback(1, epoch: EPOCH + 1);
            Assert.IsTrue(receiver.TryAccept(in first, EPOCH, NOW));
            Assert.IsTrue(receiver.TryAccept(in next, EPOCH + 1, NOW));
            Assert.IsFalse(receiver.TryAccept(in first, EPOCH + 1, NOW));
        }

        [Test]
        public void TryTake_OwnerBudgetAndWindow_UsesFiniteCapacityAndResumesNextWindow()
        {
            var budget = new DamageFeedbackBudget(1);
            uint previous = 0;
            for (int i = 0; i < DamageFeedbackBudget.OWNER_PER_SECOND; ++i)
            {
                Assert.IsTrue(budget.TryTake(1, NOW, out uint sequence));
                Assert.Greater(sequence, previous);
                previous = sequence;
            }
            Assert.IsFalse(budget.TryTake(1, NOW, out uint blocked));
            Assert.AreEqual(0u, blocked);
            Assert.IsFalse(budget.TryTake(2, NOW, out blocked));
            Assert.IsTrue(budget.TryTake(2, NOW + 1.0, out uint resumed));
            Assert.Greater(resumed, previous);
        }

        [Test]
        public void TryTake_ServerBudget_BlocksTotalAcrossOwners()
        {
            var budget = new DamageFeedbackBudget(5);
            for (int i = 0; i < DamageFeedbackBudget.SERVER_PER_SECOND; ++i)
            {
                int owner = (i / DamageFeedbackBudget.OWNER_PER_SECOND) + 1;
                Assert.IsTrue(budget.TryTake(owner, NOW, out uint sequence));
            }
            Assert.IsFalse(budget.TryTake(5, NOW, out uint ignored));
            Assert.IsTrue(budget.TryTake(5, NOW + 1.0, out uint next));
        }

        [Test]
        public void TryTake_InvalidOwnerAndTime_RejectsWithoutConsumingBudget()
        {
            var budget = new DamageFeedbackBudget(1);
            Assert.IsFalse(budget.TryTake(0, NOW, out uint ignored));
            Assert.IsFalse(budget.TryTake(1, double.PositiveInfinity, out ignored));
            Assert.IsTrue(budget.TryTake(1, NOW, out uint first));
            Assert.AreEqual(1u, first);
        }

        [TestCase(0.5f, 0.5f, 1f, true)]
        [TestCase(0.5f, 0.5f, -1f, false)]
        [TestCase(1.1f, 0.5f, 1f, false)]
        [TestCase(0.5f, -0.1f, 1f, false)]
        public void IsVisibleProjection_BehindAndOutsideViewport_Rejects(float x, float y, float z, bool expected)
        {
            Assert.AreEqual(expected, DamageFeedbackRules.IsVisibleProjection(new Vector3(x, y, z)));
        }

        [Test]
        public void TargetDamageFeedback_UsesOwnerTargetRpcAndAddsNoCommand()
        {
            MethodInfo rpc = typeof(PlayerInputState).GetMethod("TargetDamageFeedback", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(rpc);
            Assert.IsNotNull(rpc.GetCustomAttribute<TargetRpcAttribute>());
            Assert.IsNull(rpc.GetCustomAttribute<CommandAttribute>());
            ParameterInfo[] arguments = rpc.GetParameters();
            Assert.AreEqual(8, arguments.Length);
            Assert.AreEqual(typeof(Vector3), arguments[3].ParameterType);
            Assert.AreEqual(typeof(float), arguments[4].ParameterType);
            Assert.AreEqual(typeof(bool), arguments[5].ParameterType);
            Assert.AreEqual(typeof(bool), arguments[7].ParameterType);
        }

        [Test]
        public void TargetDamageFeedback_RemoteObject_DoesNotRaiseLocalEvent()
        {
            var root = new GameObject("RemoteDamageFeedback", typeof(NetworkIdentity));
            try
            {
                PlayerInputState input = root.AddComponent<PlayerInputState>();
                NetworkIdentity identity = root.GetComponent<NetworkIdentity>();
                // EditMode 미스폰 객체는 NetworkIdentity Awake를 실행하지 않아 내부 참조를 직접 연결한다
                PropertyInfo identityProperty = typeof(NetworkBehaviour).GetProperty("netIdentity");
                identityProperty.SetValue(input, identity);
                identityProperty.SetValue(root.GetComponent<PlayerCombatState>(), identity);
                int calls = 0;
                input.DamageFeedbackReceived += feedback => ++calls;
                // Mirror Weaver가 분리한 사용자 본문을 읽어 실제 수신 가드를 검사한다
                MethodInfo body = null;
                MethodInfo[] methods = typeof(PlayerInputState).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                for (int i = 0; i < methods.Length; ++i)
                {
                    if (methods[i].Name.StartsWith("UserCode_TargetDamageFeedback"))
                    {
                        body = methods[i];
                        break;
                    }
                }
                Assert.IsNotNull(body, "Mirror weaving must have completed");
                body.Invoke(input, new object[] { EPOCH, 1u, TARGET_ID, Vector3.zero, 1f, false, NOW, true });
                Assert.AreEqual(0, calls);
                Assert.IsNull(root.GetComponent<DamageNumberBinder>());
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /****************************************
        *                Helpers
        ****************************************/

        [TestCase(true)]
        [TestCase(false)]
        public void TargetDamageFeedback_Owner_KeepsNumberEventAndSuppressesOnlyDotShake(bool isDot)
        {
            var player = new GameObject("DotCameraOwner", typeof(NetworkIdentity));
            var cameraRoot = new GameObject("DotCamera");
            try
            {
                PlayerInputState input = player.AddComponent<PlayerInputState>();
                NetworkIdentity identity = player.GetComponent<NetworkIdentity>();
                PlayerCombatState combat = player.GetComponent<PlayerCombatState>();
                typeof(NetworkBehaviour).GetProperty("netIdentity").SetValue(input, identity);
                typeof(NetworkBehaviour).GetProperty("netIdentity").SetValue(combat, identity);
                typeof(NetworkIdentity).GetProperty("isOwned").GetSetMethod(true).Invoke(identity, new object[] { true });
                BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
                typeof(PlayerInputState).GetField("_sessionEpoch", flags).SetValue(input, EPOCH);
                FieldInfo cameraField = typeof(CombatCameraFeedback).GetField("_camera", flags);
                Component camera = cameraRoot.AddComponent(cameraField.FieldType);
                CombatCameraFeedback view = cameraRoot.AddComponent<CombatCameraFeedback>();
                cameraField.SetValue(view, camera);
                typeof(CombatCameraFeedback).GetField("_input", flags).SetValue(view, input);
                typeof(CombatCameraFeedback).GetField("_combat", flags).SetValue(view, combat);
                typeof(CombatCameraFeedback).GetField("_boundPlayer", flags).SetValue(view, identity);
                int numberEvents = 0;
                input.DamageFeedbackReceived += feedback =>
                {
                    ++numberEvents;
                    Assert.That(feedback.IsDamageOverTime, Is.EqualTo(isDot));
                    typeof(CombatCameraFeedback).GetMethod("OnDamageFeedback", flags).Invoke(view, new object[] { feedback });
                };
                MethodInfo body = null;
                foreach (MethodInfo method in typeof(PlayerInputState).GetMethods(flags | BindingFlags.Public))
                {
                    if (method.Name.StartsWith("UserCode_TargetDamageFeedback"))
                    {
                        body = method;
                        break;
                    }
                }

                Assert.That(body, Is.Not.Null);
                body.Invoke(input, new object[] { EPOCH, 1u, TARGET_ID, Vector3.forward, 1f, false,
                    DamageFeedbackRules.ReceptionTime(NetworkTime.time, NetworkClient.bufferTime), isDot });
                var effects = (CombatCameraEffects)typeof(CombatCameraFeedback).GetField("_effects", flags).GetValue(view);
                effects.Tick(Time.unscaledTimeAsDouble + 0.01, 0.01f);
                Assert.That(numberEvents, Is.EqualTo(1));
                Assert.That(effects.ShakeOffset.sqrMagnitude > 0f, Is.EqualTo(!isDot));
            }
            finally
            {
                Object.DestroyImmediate(cameraRoot);
                Object.DestroyImmediate(player);
            }
        }

        private static DamageFeedback Feedback(uint sequence, float amount = 1f, int epoch = EPOCH, double serverTime = NOW)
        {
            return new DamageFeedback(epoch, sequence, TARGET_ID, Vector3.zero, amount, false, serverTime);
        }
    }
}
