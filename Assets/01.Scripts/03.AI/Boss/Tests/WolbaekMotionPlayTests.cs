// System
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    // 정식 월백 프리팹(PF_WolbaekBoss)을 실제로 생성해, 서버 예고 사건이 확정한 모션과 P07 워프 표현을 서버 시각표대로 부르는지 확인한다
    // 서버 시계는 테스트가 직접 올린다. 서버 연결과 복제, 다인 접속은 이 테스트가 다루지 않고 화면에 나온 모양도 확인하지 않는다
    public class WolbaekMotionPlayTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string BOSS_PREFAB_PATH = "Assets/02.Prefab/Boss/PF_WolbaekBoss.prefab";
        private const int    BOSS_ID          = 1;
        private const int    CAST_ID          = 7;
        private const double START_CLOCK      = 100.0;
        private const float  POSITION_TOLERANCE = 0.05f;
        private const float  SPEED_TOLERANCE  = 0.01f;
        private const float  WARP_RADIUS      = 5f;
        private const float  WARP_TELEGRAPH   = 1.5f;
        private const string WARP_ID          = "wolbaek_ph2_p07";

        private static readonly Vector3 G_Departure = new Vector3(-6f, 0f, 2f);
        private static readonly Vector3 G_Landing   = new Vector3(8f, 0f, 4f);

        /****************************************
        *                Fields
        ****************************************/

        private GameObject             _root;
        private GameObject             _presenterRoot;
        private BossView               _view;
        private BossTelegraphPresenter _presenter;
        private Animator              _animator;
        private double                _clock;

        /****************************************
        *               Set Up
        ****************************************/

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(BOSS_PREFAB_PATH);
#else
            GameObject prefab = null;
#endif
            Assert.IsNotNull(prefab, "월백 보스 프리팹이 없다");

            _presenterRoot = new GameObject("TestPresenter");
            _presenter = _presenterRoot.AddComponent<BossTelegraphPresenter>();
            _clock = START_CLOCK;
            _presenter.SetServerClock(() => _clock);

            _root = Object.Instantiate(prefab);
            _view = _root.GetComponent<BossView>();
            _animator = _root.GetComponentInChildren<Animator>();
            _view.Bind(_presenter, () => _clock);
            _view.SetPose(G_Departure, Vector3.forward);

            // 서버 위치에 자리를 잡은 표시 위치를 한 번 기록하게 한다
            yield return null;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_root);
            Object.Destroy(_presenterRoot);
        }

        /****************************************
        *               Helpers
        ****************************************/

        private BossTelegraphStart WarpStart()
        {
            return BossTelegraphStart.Circle(BOSS_ID, CAST_ID, WARP_ID, G_Landing, WARP_RADIUS, _clock, WARP_TELEGRAPH);
        }

        private bool AreModelRenderersEnabled()
        {
            Renderer[] renderers = _view.ModelRenderers;
            Assert.That(renderers.Length, Is.GreaterThan(0));

            for (int i = 0; i < renderers.Length; ++i)
            {
                if (!renderers[i].enabled)
                {
                    return false;
                }
            }

            return true;
        }

        private IEnumerator SetClock(double clock)
        {
            _clock = clock;
            yield return null;
        }

        private bool IsPlaying(string stateName)
        {
            return _animator.GetCurrentAnimatorStateInfo(0).IsName(stateName) || _animator.GetNextAnimatorStateInfo(0).IsName(stateName);
        }

        /****************************************
        *            P07 Warp Tests
        ****************************************/

        // 서버 위치가 t=0에 착지 위치로 옮겨 가도 모델은 출발 위치에서 공중제비를 이어 가고, t=1.0에 숨고 t=1.4에 착지 위치에 보인다
        [UnityTest]
        public IEnumerator Warp_ServerJumpsAtStart_ModelStaysAtDepartureUntilBreakup()
        {
            _presenter.BeginTelegraph(WarpStart());
            _view.SetPose(G_Landing, Vector3.forward);
            yield return null;

            yield return SetClock(START_CLOCK + 0.5);
            Assert.That(Vector3.Distance(_root.transform.position, G_Departure), Is.LessThan(POSITION_TOLERANCE), "t=0.5 모델은 출발 위치");
            Assert.That(AreModelRenderersEnabled(), Is.True, "t=0.5 모델은 보인다");

            yield return SetClock(START_CLOCK + 1.05);
            Assert.That(AreModelRenderersEnabled(), Is.False, "t=1.05 분해되어 숨는다");
            Assert.That(Vector3.Distance(_root.transform.position, G_Landing), Is.LessThan(POSITION_TOLERANCE), "t=1.05 숨은 사이 모델은 착지 위치로 옮겨 간다");

            yield return SetClock(START_CLOCK + 1.2);
            Assert.That(AreModelRenderersEnabled(), Is.False, "t=1.2 재결합 중에는 아직 숨어 있다");

            yield return SetClock(START_CLOCK + 1.45);
            Assert.That(AreModelRenderersEnabled(), Is.True, "t=1.45 모델이 보인다");
            Assert.That(Vector3.Distance(_root.transform.position, G_Landing), Is.LessThan(POSITION_TOLERANCE), "t=1.45 착지 위치");

            // 시험 씬의 t=2.5 출발 위치 복귀는 옮기지 않았다, 모델은 착지 위치에 남는다
            yield return SetClock(START_CLOCK + 3.0);
            Assert.That(Vector3.Distance(_root.transform.position, G_Landing), Is.LessThan(POSITION_TOLERANCE), "복귀하지 않고 착지 위치에 남는다");
        }

        // 서버 타격 확정 사건을 받은 뒤에만 충격파를 재생한다, 예정 시각이 지나도 사건이 없으면 재생하지 않는다
        [UnityTest]
        public IEnumerator Warp_LandingEffectRunsFromStart_AndStrikeNeedsServerEvent()
        {
            WolbaekWarpView warp = _root.GetComponent<WolbaekWarpView>();
            Assert.That(warp, Is.Not.Null);

            _presenter.BeginTelegraph(WarpStart());
            _view.SetPose(G_Landing, Vector3.forward);
            yield return null;
            Assert.That(warp.IsActive, Is.True, "t=0 착지 예고 시작");

            yield return SetClock(START_CLOCK + 1.5);
            Assert.That(_presenter.TryGetVisuals(CAST_ID, out BossTelegraphCast cast, out _, out _), Is.True);
            Assert.That(cast.HasExecuted, Is.False, "서버 사건이 없으면 발동하지 않는다");

            Assert.That(_presenter.Strike(CAST_ID, _clock), Is.True);
            yield return null;
            Assert.That(cast.HasExecuted, Is.True, "서버 OnStruck 이후 발동");
        }

        // 취소되면 모델을 즉시 보이고 서버 위치(착지 지점)를 따른다, 출발 위치로 되돌리지 않는다
        [UnityTest]
        public IEnumerator Warp_Cancelled_ShowsModelAtServerPosition()
        {
            _presenter.BeginTelegraph(WarpStart());
            _view.SetPose(G_Landing, Vector3.forward);
            yield return SetClock(START_CLOCK + 0.4);

            Assert.That(_presenter.Cancel(CAST_ID), Is.True);
            yield return null;

            Assert.That(AreModelRenderersEnabled(), Is.True);
            Assert.That(Vector3.Distance(_root.transform.position, G_Landing), Is.LessThan(POSITION_TOLERANCE));
        }

        // 분해 시각이 지난 뒤 늦게 받으면 출발 위치를 쓰지 않고 모델을 착지 위치에 바로 보인다
        [UnityTest]
        public IEnumerator Warp_ReceivedAfterBreakup_ShowsModelAtLanding()
        {
            _clock = START_CLOCK + 1.3;
            _view.SetPose(G_Landing, Vector3.forward);
            _presenter.BeginTelegraph(BossTelegraphStart.Circle(BOSS_ID, CAST_ID, WARP_ID, G_Landing, WARP_RADIUS, START_CLOCK, WARP_TELEGRAPH));
            yield return null;
            yield return null;

            Assert.That(AreModelRenderersEnabled(), Is.True);
            Assert.That(Vector3.Distance(_root.transform.position, G_Landing), Is.LessThan(POSITION_TOLERANCE));
        }

        /****************************************
        *          Pattern Motion Tests
        ****************************************/

        // 1P P02는 판정 1.0초에 힘 순간 0.61초를 맞추려고 0.39초 뒤에 시작한다
        [UnityTest]
        public IEnumerator Motion_FanPattern_StartsAtStrikeMinusPeak()
        {
            var start = BossTelegraphStart.Fan(BOSS_ID, CAST_ID, "wolbaek_p02", Vector3.zero, Vector3.forward, 6f, 100f, _clock, 1f);
            _presenter.BeginTelegraph(start);

            yield return SetClock(START_CLOCK + 0.2);
            yield return null;
            Assert.That(IsPlaying("attack09_inplace"), Is.False, "t=0.2 아직 시작하지 않는다");

            yield return SetClock(START_CLOCK + 0.45);
            yield return null;
            yield return null;
            Assert.That(IsPlaying("attack09_inplace"), Is.True, "t=0.45 시작한다");
            Assert.That(_animator.speed, Is.EqualTo(1f).Within(SPEED_TOLERANCE));

            _presenter.Finish(CAST_ID);
            yield return null;
            Assert.That(_animator.speed, Is.EqualTo(1f).Within(SPEED_TOLERANCE), "끝나면 재생 속도를 되돌린다");
        }

        // P07 모션은 힘 순간 2.55초를 판정 1.5초에 맞추려고 1.7배속으로 바로 시작한다
        [UnityTest]
        public IEnumerator Motion_WarpPattern_PlaysAtOnePointSevenSpeed()
        {
            _presenter.BeginTelegraph(WarpStart());
            _view.SetPose(G_Landing, Vector3.forward);
            yield return SetClock(START_CLOCK + 0.1);
            yield return null;

            Assert.That(IsPlaying("attack06_inplace"), Is.True);
            Assert.That(_animator.speed, Is.EqualTo(1.7f).Within(SPEED_TOLERANCE));
        }

        // 돌진은 예고 동안 Idle이고 서버 돌진 시작 사건을 받으면 거리 / 속도에 맞춘 배속으로 재생한다
        [UnityTest]
        public IEnumerator Motion_Dash_StartsOnServerDashEvent()
        {
            var start = BossTelegraphStart.Dash(BOSS_ID, CAST_ID, "wolbaek_p01", Vector3.zero, Vector3.forward, 8f, 2f, _clock, 0.8f);
            _presenter.BeginTelegraph(start);
            yield return SetClock(START_CLOCK + 0.5);
            Assert.That(IsPlaying("move_Flying_front_inplace"), Is.False, "예고 동안 몸 모션은 Idle");

            yield return SetClock(START_CLOCK + 0.8);
            Assert.That(_presenter.BeginDash(CAST_ID, 8f, 0.45f, 8f / 0.45f, _clock), Is.True, "서버 돌진 시작 사건");
            yield return null;
            yield return null;

            // 예고 표현이 먼저 끝나 정상 종료 알림이 와도 돌진 모션은 서버가 정한 끝 시각까지 이어진다
            Assert.That(IsPlaying("move_Flying_front_inplace"), Is.True);
            Assert.That(_animator.speed, Is.EqualTo(1.333f / 0.45f).Within(0.05f));

            // 서버 돌진 끝 시각(0.45초)이 지나면 재생 속도를 1배까지 서서히 낮춘다
            yield return SetClock(START_CLOCK + 0.8 + 0.5);
            yield return new WaitForSeconds(WolbaekMotionPlan.DASH_RECOVER_SECONDS + 0.3f);
            Assert.That(_animator.speed, Is.EqualTo(1f).Within(SPEED_TOLERANCE), "복귀가 끝나면 재생 속도 1배");
        }

        // 서버가 사망을 알리면 확정한 처치 모션을 재생한다
        [UnityTest]
        public IEnumerator Motion_Defeated_PlaysDeathState()
        {
            _view.SetDefeated(true);
            yield return null;
            yield return null;

            Assert.That(IsPlaying("dead01_inplace"), Is.True);
        }

        [UnityTest]
        public IEnumerator Motion_Defeated_DissolvesAndResetRestoresMaterials()
        {
            WolbaekDissolveView dissolve = _root.GetComponent<WolbaekDissolveView>();
            Assert.That(dissolve, Is.Not.Null, "정식 프리팹에 소멸 표현이 연결돼야 한다");

            SkinnedMeshRenderer body = null;
            foreach (SkinnedMeshRenderer renderer in _root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (renderer.name == "SK_KITSUNEYUNA_BODY_PART_01")
                {
                    body = renderer;
                    break;
                }
            }

            Assert.That(body, Is.Not.Null);
            Material original = body.sharedMaterial;
            _view.SetDefeated(true);
            yield return new WaitForSeconds(4.2f);

            Assert.That(dissolve.Stage, Is.EqualTo(WolbaekDeathTimeline.EStage.Dissolving));
            Assert.That(body.sharedMaterial, Is.Not.SameAs(original), "Burn 재질 사본으로 소멸한다");
            float maskBefore = body.sharedMaterial.GetVector("_PositionVector").y;
            yield return new WaitForSeconds(0.4f);
            float maskAfter = body.sharedMaterial.GetVector("_PositionVector").y;
            Assert.That(maskAfter, Is.LessThan(maskBefore), "3초 동안 마스크 평면이 아래로 이동한다");

            _view.SetDefeated(false);
            yield return null;
            Assert.That(dissolve.Stage, Is.EqualTo(WolbaekDeathTimeline.EStage.None));
            Assert.That(body.sharedMaterial, Is.SameAs(original), "Reset 때 원래 재질로 돌아온다");
        }

        [UnityTest]
        public IEnumerator Motion_RestoredMidCast_SeeksForward()
        {
            _clock = START_CLOCK + 1.8;
            _view.BeginEventMotion(CAST_ID, EWolbaekEventMotion.StarStage, START_CLOCK, 2f);
            yield return null;
            yield return null;

            Assert.That(IsPlaying("attack_drain_inplace"), Is.True, "서버 시작 시각부터 흐른 만큼 진행된 모션을 보여 준다");
        }

        [UnityTest]
        public IEnumerator Motion_RestoredAfterEnd_SkipsAnExpiredAction()
        {
            _clock = START_CLOCK + 5.0;
            _view.BeginEventMotion(CAST_ID, EWolbaekEventMotion.StarStage, START_CLOCK, 2f);
            yield return null;
            yield return null;

            Assert.That(IsPlaying("attack_drain_inplace"), Is.False, "이미 지난 짧은 동작을 다시 시작하지 않는다");
        }

        // 서버 흐름 상태가 Appearing이면 등장 마법진이 나타나고, 전투 상태로 바뀌면 새 파티클 생성을 멈춘다
        [UnityTest]
        public IEnumerator Motion_AppearingFlow_ShowsEffectUntilCombat()
        {
            int before = Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Length;

            _view.SetFlowState(EBossFlowState.Appearing);
            yield return null;
            int during = Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Length;
            Assert.That(during, Is.GreaterThan(before), "등장 마법진 파티클이 생긴다");

            _view.SetFlowState(EBossFlowState.Combat);
            yield return null;
            ParticleSystem[] systems = Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None);
            int emitting = 0;
            for (int i = 0; i < systems.Length; ++i)
            {
                emitting += systems[i].emission.enabled && systems[i].isEmitting ? 1 : 0;
            }

            Assert.That(emitting, Is.EqualTo(0), "전투 상태가 되면 새 파티클 생성을 멈춘다");
        }

        // 서버 흐름 상태가 Appearing이면 등장 모션을 재생한다
        [UnityTest]
        public IEnumerator Motion_AppearingFlow_PlaysAppearState()
        {
            _view.SetFlowState(EBossFlowState.Appearing);
            yield return null;
            yield return null;

            Assert.That(IsPlaying("Appear"), Is.True);
        }
    }
}
