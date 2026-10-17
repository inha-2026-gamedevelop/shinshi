// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;

using Samples = Shinshi.Tests.BossWolbaekGimmickSamples;

namespace Shinshi.Tests
{
    public class BossWolbaekGimmickBinderTests
    {
        // 클라이언트 쪽 연결을 본다: 복제 상태의 월백 기록이 바인더를 거쳐 표현에 복원되는지,
        // 50% 성공 뒤 그로기가 페이즈 세대로 끝나 성공 Rpc와 세대 SyncVar의 도착 순서에 영향을 받지 않는지,
        // 참가자 앵커 추적기가 스폰된 참가자의 전투 번호와 Transform을 표현에 넣는지

        /****************************************
        *                Fields
        ****************************************/

        private const int    CAST_ID      = 61;
        private const double FAR_FUTURE   = 1000000000.0; // 시험 시계와 상관없이 아직 오지 않은 판정 시각
        private const ushort SUCCESS_GEN  = 0;

        private static readonly FieldInfo  G_Generation = typeof(BossState).GetField("_phaseGeneration",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo G_GenerationHook = typeof(BossState).GetMethod("OnPhaseGenerationSynced",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private GameObject            _root;
        private BossState             _state;
        private BossNetworkViewBinder _binder;
        private WolbaekColorWardView  _ward;

        /****************************************
        *               Set Up
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _root   = new GameObject("BossWolbaekGimmickBinderTests");
            _root.AddComponent<NetworkIdentity>();
            _state  = _root.AddComponent<BossState>();
            _ward   = _root.AddComponent<WolbaekColorWardView>();
            _binder = _root.AddComponent<BossNetworkViewBinder>();
            typeof(BossNetworkViewBinder).GetField("_colorWardView", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_binder, _ward);
        }

        [TearDown]
        public void TearDown()
        {
            if (_binder != null)
            {
                _binder.Unbind();
            }

            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        /****************************************
        *               Contract
        ****************************************/

        [Test]
        public void Binder_ImplementsTheWolbaekGimmickStateContract()
        {
            Assert.That(_binder is IBossWolbaekGimmickStateListener, Is.True);
            Assert.That(_state is IBossWolbaekGimmickEventListener, Is.True);
        }

        /****************************************
        *          50% Groggy Generation
        ****************************************/

        [Test]
        public void WardSucceeded_ThenGenerationRises_EndsGroggyOnlyAfterTheRise()
        {
            StartWard();
            IBossWolbaekGimmickEventListener listener = _binder;

            listener.OnWardSucceeded(CAST_ID, SUCCESS_GEN);
            Assert.IsTrue(_ward.IsAwaitingPhaseTransition, "성공 사건만으로 그로기를 끝내지 않는다");

            SetGeneration(SUCCESS_GEN + 1);

            Assert.IsFalse(_ward.IsAwaitingPhaseTransition);
        }

        [Test]
        public void GenerationRises_BeforeWardSucceededArrives_EndsGroggyImmediately()
        {
            StartWard();
            SetGeneration(SUCCESS_GEN + 1);
            IBossWolbaekGimmickEventListener listener = _binder;

            listener.OnWardSucceeded(CAST_ID, SUCCESS_GEN);

            Assert.IsFalse(_ward.IsAwaitingPhaseTransition, "세대 SyncVar가 성공 Rpc보다 먼저 와도 그로기가 남지 않는다");
        }

        [Test]
        public void GenerationEqualToTheSuccessValue_KeepsGroggy()
        {
            StartWard();
            IBossWolbaekGimmickEventListener listener = _binder;

            listener.OnWardSucceeded(CAST_ID, SUCCESS_GEN + 1);
            SetGeneration(SUCCESS_GEN + 1);

            Assert.IsTrue(_ward.IsAwaitingPhaseTransition, "성공 순간과 같은 세대는 전환이 아니다");
        }

        /****************************************
        *              Late Join
        ****************************************/

        [Test]
        public void Bind_LateClient_RestoresWardStageAndThePendingStrikeFromTheLedger()
        {
            WolbaekWardStart  start  = Samples.Ward(CAST_ID);
            WolbaekWardStage  stage  = new WolbaekWardStage(0, FAR_FUTURE - 10.0, FAR_FUTURE - 5.0, 2f, 3, Vector3.right,
                Vector3.left, 3f);
            WolbaekWardStrike strike = new WolbaekWardStrike(0, 0, FAR_FUTURE - 6.0, FAR_FUTURE, Vector3.forward, 1f);
            _state.ClientWolbaekLedger.OnWardStarted(in start);
            _state.ClientWolbaekLedger.OnStageStarted(CAST_ID, in stage);
            _state.ClientWolbaekLedger.OnStrikeTelegraphed(CAST_ID, in strike);
            _state.OnStartClient();

            _binder.Bind(_state);

            Assert.IsTrue(_ward.IsActive);
            Assert.IsFalse(_ward.IsGathering);
            Assert.AreEqual(0, _ward.CurrentStage.Index);
            Assert.IsTrue(_ward.HasTelegraph, "진행 중인 광역 예고를 복원 기록으로 다시 그린다");
        }

        /****************************************
        *            Anchor Tracker
        ****************************************/

        [Test]
        public void AnchorTracker_SpawnedPlayers_InjectsAddsAndRemovesByCombatId()
        {
            var anchors = new Dictionary<int, Transform>();
            var tracker = new BossParticipantAnchorTracker((combatId, anchor) =>
            {
                if (anchor == null)
                {
                    anchors.Remove(combatId);
                }
                else
                {
                    anchors[combatId] = anchor;
                }
            });
            var spawned = new Dictionary<uint, NetworkIdentity>();
            GameObject first  = MakePlayer(spawned, 1, Samples.MEMBER_A);
            GameObject second = MakePlayer(spawned, 2, Samples.MEMBER_B);
            GameObject other  = new GameObject("NotAPlayer");
            spawned[3] = other.AddComponent<NetworkIdentity>();

            try
            {
                tracker.Refresh(spawned);
                Assert.AreEqual(2, anchors.Count);
                Assert.AreSame(first.transform, anchors[Samples.MEMBER_A]);

                anchors.Clear();
                tracker.InjectAll();
                Assert.AreEqual(2, anchors.Count, "표현이 앵커를 비운 뒤 전부 다시 넣는다");

                spawned.Remove(2);
                tracker.Refresh(spawned);
                Assert.IsFalse(anchors.ContainsKey(Samples.MEMBER_B), "사라진 참가자의 앵커를 지운다");
                Assert.AreEqual(1, tracker.Count);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(other);
            }
        }

        /****************************************
        *               Helpers
        ****************************************/

        private void StartWard()
        {
            _binder.Bind(_state);
            WolbaekWardStart start = Samples.Ward(CAST_ID);
            _binder.OnWardStarted(in start);
            Assert.IsTrue(_ward.IsActive);
        }

        // 클라이언트가 SyncVar를 받은 것처럼 값을 바꾸고 hook을 부른다
        private void SetGeneration(int generation)
        {
            ushort previous = _state.PhaseGeneration;
            G_Generation.SetValue(_state, (ushort)generation);
            G_GenerationHook.Invoke(_state, new object[] { previous, (ushort)generation });
        }

        private static GameObject MakePlayer(Dictionary<uint, NetworkIdentity> spawned, uint netId, int combatId)
        {
            var player = new GameObject("Player" + combatId);
            NetworkIdentity identity = player.AddComponent<NetworkIdentity>();
            PlayerCombatState state  = player.AddComponent<PlayerCombatState>();
            typeof(PlayerCombatState).GetField("_combatId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state, combatId);
            spawned[netId] = identity;
            return player;
        }
    }
}
