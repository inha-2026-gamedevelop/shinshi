// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    // 돌진 정지와 그로기 사건이 서버 시각으로 무철 모션 구간을 끊고 끝내는지 재생기 상태(IsActive, IsGroggy)로 확인한다.
    // EditMode라 Animator는 재생하지 않는다. 클립 재생 모양은 PlayMode나 실제 보스 연결에서 따로 확인해야 한다
    public class MucheolMotionStopTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CONTROLLER_PATH = "Assets/01.Scripts/03.AI/Boss/Animation/AC_Mucheol.controller";
        private const double START           = 100.0;
        private const float  DASH_SECONDS    = 0.5f;
        private const float  DASH_RANGE      = 9f;
        private const double AFTER           = 0.01; // 초, 구간 끝을 막 지난 시각
        private const int    CAST_ID         = 7;
        private const int    OTHER_CAST_ID   = 8;

        /****************************************
        *                Fields
        ****************************************/

        private readonly List<GameObject> _objects = new List<GameObject>();
        private double _now;

        /****************************************
        *              Lifecycle
        ****************************************/

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _objects.Count; ++i)
            {
                Object.DestroyImmediate(_objects[i]);
            }

            _objects.Clear();
        }

        /****************************************
        *            Director Tests
        ****************************************/

        // 미리 잡은 정지보다 서버 정지가 늦으면 원래 들이받는 구간부터 다시 끊어 늦은 정지까지 달린다
        [Test]
        public void ReplaceRunFrom_LaterServerStop_ExtendsRunUntilServerStop()
        {
            MucheolMotionDirector director = CreateDirector();
            MucheolMotionTrack run = ResolveRun();
            director.PlayCast(CAST_ID, new List<MucheolMotionTrack> { run }, false, START);

            Assert.IsTrue(director.ReplaceFrom(CAST_ID, START + 0.2, StopTail(run, START + 0.2), START));
            Assert.IsTrue(director.ReplaceRunFrom(CAST_ID, run, START + 0.4, StopTail(run, START + 0.4), START + 0.1));

            director.Tick(START + 0.2 + MucheolMotionCatalog.DASH_STOP_SECONDS + AFTER);
            Assert.IsTrue(director.IsActive, "앞서 잡은 정지 끝이 지나도 서버 정지까지 달린다");

            director.Tick(START + 0.4 + MucheolMotionCatalog.DASH_STOP_SECONDS + AFTER);
            Assert.IsFalse(director.IsActive);
        }

        [Test]
        public void ReplaceRunFrom_EarlierServerStopOrOtherCast_HandlesEach()
        {
            MucheolMotionDirector director = CreateDirector();
            MucheolMotionTrack run = ResolveRun();
            director.PlayCast(CAST_ID, new List<MucheolMotionTrack> { run }, false, START);
            Assert.IsTrue(director.ReplaceFrom(CAST_ID, START + 0.3, StopTail(run, START + 0.3), START));

            Assert.IsFalse(director.ReplaceRunFrom(OTHER_CAST_ID, run, START + 0.1, StopTail(run, START + 0.1), START));
            Assert.IsTrue(director.ReplaceRunFrom(CAST_ID, run, START + 0.1, StopTail(run, START + 0.1), START));

            director.Tick(START + 0.1 + MucheolMotionCatalog.DASH_STOP_SECONDS + AFTER);
            Assert.IsFalse(director.IsActive, "이른 서버 정지 끝에서 Idle로 돌아간다");
        }

        // 시전 종료와 취소는 그로기를 끊지 않고 그로기 취소만 끊는다
        [Test]
        public void CancelGroggy_OnlyMatchingCastEndsGroggy()
        {
            MucheolMotionDirector director = CreateDirector();
            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.Event = START;
            MucheolMotionTrack down = MucheolMotionPlan.Resolve(MucheolMotionCatalog.GroggyDownOpen, in anchors);
            director.PlayGroggy(CAST_ID, new List<MucheolMotionTrack> { down }, true, START);
            Assert.IsTrue(director.IsGroggy);

            director.Finish(CAST_ID);
            director.Cancel(CAST_ID);
            director.CancelGroggy(OTHER_CAST_ID);
            Assert.IsTrue(director.IsGroggy);

            director.CancelGroggy(CAST_ID);
            Assert.IsFalse(director.IsActive);
        }

        /****************************************
        *              View Tests
        ****************************************/

        // 받은 순간이 아니라 서버 정지 시각에서 제동한다
        [Test]
        public void StopDash_ServerTime_BrakesAtServerTimeNotReceiveTime()
        {
            MucheolMotionView view = CreateView();
            BeginFullDash(view);

            _now = START + 0.3;
            view.StopDash(CAST_ID, 0, START + 0.2);

            view.Director.Tick(START + 0.2 + MucheolMotionCatalog.DASH_STOP_SECONDS + AFTER);
            Assert.IsFalse(view.Director.IsActive);
        }

        // 시각이 없는 정지 사건은 받은 순간에 제동한다
        [Test]
        public void StopDash_NaN_FallsBackToReceiveTime()
        {
            MucheolMotionView view = CreateView();
            BeginFullDash(view);

            _now = START + 0.3;
            view.StopDash(CAST_ID, 0, double.NaN);

            view.Director.Tick(START + 0.2 + MucheolMotionCatalog.DASH_STOP_SECONDS + AFTER);
            Assert.IsTrue(view.Director.IsActive);
            view.Director.Tick(START + 0.3 + MucheolMotionCatalog.DASH_STOP_SECONDS + AFTER);
            Assert.IsFalse(view.Director.IsActive);
        }

        // 시작 거리로 미리 잡은 정지보다 서버 정지가 한 틱 넘게 늦으면 서버 시각으로 다시 끊는다
        [Test]
        public void StopDash_LaterThanEstimate_ReplacesEstimatedStop()
        {
            MucheolMotionView view = CreateView();
            float speed = DASH_RANGE / DASH_SECONDS;
            float shortDistance = speed * 0.2f; // 시작 0.2초 뒤에 닿을 거리
            BeginDash(view, shortDistance);

            _now = START + 0.3;
            view.StopDash(CAST_ID, 0, START + 0.3);

            view.Director.Tick(START + 0.2 + MucheolMotionCatalog.DASH_STOP_SECONDS + AFTER);
            Assert.IsTrue(view.Director.IsActive, "미리 잡은 정지 끝을 지나도 서버 정지까지 간다");
            view.Director.Tick(START + 0.3 + MucheolMotionCatalog.DASH_STOP_SECONDS + AFTER);
            Assert.IsFalse(view.Director.IsActive);
        }

        // 그로기 대기는 끝을 모른 채 유지하고, 끝 시각을 받으면 그 시각에 일어선다
        [Test]
        public void Groggy_PendingThenEnd_HoldsUntilEnd()
        {
            MucheolMotionView view = CreateView();
            // 무철 클립은 Git에서 제외된 외부 애니메이션 에셋이라 없는 PC에서는 그로기 구간을 만들 수 없다
            Assume.That(view.Director.HasClip(MucheolMotionCatalog.STUN_START_CLIP), "무철 그로기 클립이 없다");
            _now = START;
            view.BeginGroggy(CAST_ID, START, double.NaN);
            Assert.IsTrue(view.Director.IsGroggy);

            view.Director.Tick(START + 30.0);
            Assert.IsTrue(view.Director.IsGroggy, "끝 시각을 받기 전에는 내려앉은 자세로 기다린다");

            view.CancelGroggy(OTHER_CAST_ID);
            Assert.IsTrue(view.Director.IsGroggy);
            view.CancelGroggy(CAST_ID);
            Assert.IsFalse(view.Director.IsActive);
        }

        /****************************************
        *               Helpers
        ****************************************/

        private MucheolMotionDirector CreateDirector()
        {
            return new MucheolMotionDirector(CreateAnimator());
        }

        private Animator CreateAnimator()
        {
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CONTROLLER_PATH);
            Assert.IsNotNull(controller, CONTROLLER_PATH);

            var root = new GameObject("MucheolMotionStopTests");
            _objects.Add(root);
            Animator animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            return animator;
        }

        private MucheolMotionView CreateView()
        {
            Animator animator = CreateAnimator();
            MucheolMotionView view = animator.gameObject.AddComponent<MucheolMotionView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("_animator").objectReferenceValue = animator;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ViewTestUtil.Awake(view);
            view.SetServerClock(() => _now);
            Assert.IsNotNull(view.Director);
            return view;
        }

        private void BeginFullDash(MucheolMotionView view)
        {
            BeginDash(view, DASH_RANGE);
        }

        private void BeginDash(MucheolMotionView view, float distance)
        {
            _now = START;
            view.BeginTelegraph(CAST_ID, MucheolMotionCatalog.P1_P06_ID, START - 1.0, 1f);
            view.BeginDash(CAST_ID, distance, DASH_SECONDS, DASH_RANGE / DASH_SECONDS, START);
        }

        private static MucheolMotionTrack ResolveRun()
        {
            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.DashStart = START;
            anchors.DashEnd   = START + DASH_SECONDS;
            return MucheolMotionPlan.Resolve(MucheolMotionCatalog.DashRun, in anchors);
        }

        private static MucheolMotionTrack StopTail(MucheolMotionTrack run, double at)
        {
            MucheolMotionPlan.Sample(run, at, out float clipAt, out _);
            return MucheolMotionPlan.Span(MucheolMotionCatalog.DASH_CLIP, at, clipAt, MucheolMotionCatalog.DashRunEndClip,
                MucheolMotionCatalog.DASH_STOP_SECONDS);
        }
    }
}
