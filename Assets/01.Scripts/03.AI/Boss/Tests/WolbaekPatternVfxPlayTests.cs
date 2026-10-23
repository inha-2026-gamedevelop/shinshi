// System
using System.Collections;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Art;

namespace Shinshi.Tests
{
    // 정식 월백 프리팹(PF_WolbaekBoss)에 서버 사건만 넣어 패턴 VFX가 서버 값의 위치와 크기와 시각으로 나오는지 확인한다
    // 서버 시계는 테스트가 직접 올린다. 서버 연결과 복제, 다인 접속, 화면에 나온 모양은 이 테스트가 다루지 않는다
    public class WolbaekPatternVfxPlayTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string BOSS_PREFAB_PATH = "Assets/02.Prefab/Boss/PF_WolbaekBoss.prefab";
        private const int    BOSS_ID          = 1;
        private const int    CAST_ID          = 7;
        private const double START_CLOCK      = 100.0;
        private const float  TOLERANCE        = 0.05f;
        private const float  LOOSE_TOLERANCE  = 1.5f;

        /****************************************
        *                Fields
        ****************************************/

        private GameObject             _root;
        private GameObject             _presenterRoot;
        private BossTelegraphPresenter _presenter;
        private BossView               _view;
        private double                 _clock;

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

            ClearLeftoverEffects();

            _clock = START_CLOCK;
            _presenterRoot = new GameObject("TestPresenter");
            _presenter = _presenterRoot.AddComponent<BossTelegraphPresenter>();
            _presenter.SetServerClock(() => _clock);

            // 운영 프리팹의 예고 표현은 별형 표현을 달고 있다, 같은 구성을 시험 표현에도 붙인다
            BossStarView stars = _presenterRoot.AddComponent<BossStarView>();
            typeof(BossTelegraphPresenter).GetField("_starView", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_presenter, stars);

            _root = Object.Instantiate(prefab);
            _view = _root.GetComponent<BossView>();
            _view.Bind(_presenter, () => _clock);
            _view.SetPose(Vector3.zero, Vector3.forward);
            yield return null;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_root);
            Object.Destroy(_presenterRoot);
            ClearLeftoverEffects();
        }

        // 이전 시험이 남긴 서드파티 효과 사본이 개수와 위치 확인에 섞이지 않게 지운다
        private static void ClearLeftoverEffects()
        {
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if ((t != null) && t.name.EndsWith("(Clone)") && (t.name.StartsWith("SB_") || t.name.StartsWith("PF_VFX_")))
                {
                    Object.DestroyImmediate(t.gameObject);
                }
            }
        }

        /****************************************
        *               Helpers
        ****************************************/

        private IEnumerator SetClock(double clock)
        {
            _clock = clock;
            yield return null;
        }

        private static int CountNamed(string namePrefix)
        {
            int count = 0;
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name.StartsWith(namePrefix) && t.name.EndsWith("(Clone)"))
                {
                    ++count;
                }
            }

            return count;
        }

        private static Transform FindNamed(string namePrefix)
        {
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.name.StartsWith(namePrefix) && t.name.EndsWith("(Clone)"))
                {
                    return t;
                }
            }

            return null;
        }

        private static int CountRunning(WolbaekFoxfirePlayer[] players, EWolbaekFoxfireState state)
        {
            int count = 0;
            foreach (WolbaekFoxfirePlayer player in players)
            {
                if (player.IsRunning && (player.State == state))
                {
                    ++count;
                }
            }

            return count;
        }

        private WolbaekFoxfirePlayer[] FoxfirePlayers()
        {
            return Object.FindObjectsByType<WolbaekFoxfirePlayer>(FindObjectsSortMode.None);
        }

        private WolbaekBeamPlayer BeamPlayer(EWolbaekBeamKind kind)
        {
            foreach (WolbaekBeamPlayer player in Object.FindObjectsByType<WolbaekBeamPlayer>(FindObjectsSortMode.None))
            {
                if (player.Kind == kind)
                {
                    return player;
                }
            }

            return null;
        }

        // 선 하나가 내부 연출(코어, 베일, 꽃잎)까지 켜졌는지 렌더러 활성 상태로 센다. 예고는 바닥만 켜진다
        private static int CountFullyDrawn(WolbaekBeamPlayer player)
        {
            int count = 0;
            foreach (WolbaekBeamLineView view in player.GetComponentsInChildren<WolbaekBeamLineView>(true))
            {
                var renderers = (Renderer[])typeof(WolbaekBeamLineView).GetField("_renderers", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
                if (renderers[0].enabled && renderers[1].enabled && renderers[3].enabled)
                {
                    ++count;
                }
            }

            return count;
        }

        /****************************************
        *            Projectile Tests
        ****************************************/

        // 서버 발사 사건의 방향, 속도, 사거리로 비행하고 서버 소멸 사건의 위치에서 타격을 낸다
        [UnityTest]
        public IEnumerator Projectile_ServerLaunchAndEnd_FliesAlongServerValuesAndImpactsAtServerPosition()
        {
            var start = new BossProjectileStart(BOSS_ID, 21, CAST_ID, Vector3.zero, Vector3.forward, 10f, 1f, 16f, _clock);
            Assert.That(_presenter.LaunchProjectile(start), Is.True);
            yield return null;

            WolbaekFoxfirePlayer[] players = FoxfirePlayers();
            Assert.That(CountRunning(players, EWolbaekFoxfireState.Flight), Is.EqualTo(1), "발사 사건에서 비행 효과 1개");

            yield return SetClock(START_CLOCK + 0.8);
            WolbaekFoxfirePlayer flight = null;
            foreach (WolbaekFoxfirePlayer player in players)
            {
                if (player.IsRunning && (player.State == EWolbaekFoxfireState.Flight))
                {
                    flight = player;
                }
            }

            Assert.That(flight, Is.Not.Null);
            Assert.That(flight.transform.position.z, Is.InRange(8f - LOOSE_TOLERANCE, 8f + LOOSE_TOLERANCE), "서버 속도 10m/초로 0.8초 뒤 약 절반");
            Assert.That(Mathf.Abs(flight.transform.position.x), Is.LessThan(LOOSE_TOLERANCE), "서버 방향으로 날아간다");

            Assert.That(_presenter.EndProjectile(21, EBossProjectileFate.Hit, new Vector3(0.5f, 1f, 9f)), Is.True);
            yield return null;

            Assert.That(CountRunning(players, EWolbaekFoxfireState.Flight), Is.EqualTo(0), "서버 소멸 사건에서 비행 효과를 끈다");
            Assert.That(CountRunning(players, EWolbaekFoxfireState.Impact), Is.EqualTo(1), "서버가 알린 위치에서 타격");

            foreach (WolbaekFoxfirePlayer player in players)
            {
                if (player.IsRunning && (player.State == EWolbaekFoxfireState.Impact))
                {
                    Assert.That(player.transform.position.x, Is.EqualTo(0.5f).Within(TOLERANCE));
                    Assert.That(player.transform.position.z, Is.EqualTo(9f).Within(TOLERANCE));
                }
            }
        }

        // 서버가 투사체를 취소하면 타격 없이 끝낸다
        [UnityTest]
        public IEnumerator Projectile_Cancelled_EndsWithoutImpact()
        {
            var start = new BossProjectileStart(BOSS_ID, 22, CAST_ID, Vector3.zero, Vector3.forward, 10f, 1f, 16f, _clock);
            _presenter.LaunchProjectile(start);
            yield return null;

            Assert.That(_presenter.CancelProjectile(22), Is.True);
            yield return null;

            WolbaekFoxfirePlayer[] players = FoxfirePlayers();
            Assert.That(CountRunning(players, EWolbaekFoxfireState.Flight), Is.EqualTo(0));
            Assert.That(CountRunning(players, EWolbaekFoxfireState.Impact), Is.EqualTo(0));
        }

        /****************************************
        *            Beam Release Test
        ****************************************/

        // 대형 방출은 예고 사건의 직사각형으로 바닥 예고를 보이고 서버 타격 사건을 받은 뒤에 확정한다
        [UnityTest]
        public IEnumerator BeamRelease_ServerTelegraphAndStrike_RunsOnServerEvents()
        {
            WolbaekBeamPlayer release = BeamPlayer(EWolbaekBeamKind.Release);
            Assert.That(release, Is.Not.Null);

            var start = BossTelegraphStart.Line(BOSS_ID, CAST_ID, "wolbaek_p06", Vector3.zero, Vector3.forward, 14f, 4f, _clock, 1.6f);
            Assert.That(_presenter.BeginTelegraph(start), Is.True);
            yield return null;

            Assert.That(release.IsRunning, Is.True, "예고 사건에서 바닥 예고 시작");
            Assert.That(release.CastId, Is.EqualTo((ulong)CAST_ID));
            Assert.That(release.VisibleLineCount, Is.EqualTo(1));

            // 예고 중 서버가 알린 조준 방향 갱신을 따라가도 예고는 이어진다
            _presenter.UpdateAim(CAST_ID, new Vector3(1f, 0f, 1f).normalized, 14f, _clock);
            yield return null;
            yield return null;
            Assert.That(release.IsRunning, Is.True);
            Assert.That(release.VisibleLineCount, Is.EqualTo(1));

            yield return SetClock(START_CLOCK + 1.6);
            Assert.That(_presenter.Strike(CAST_ID, _clock), Is.True);
            yield return null;
            Assert.That(release.IsRunning, Is.True, "서버 타격 확정 뒤에도 파면이 이어진다");
        }

        /****************************************
        *           Fan And Circle Tests
        ****************************************/

        // 부채꼴은 서버 타격 확정 사건에서 좌우 두 장이 나온다
        [UnityTest]
        public IEnumerator Fan_ServerStrike_SpawnsTwoSectorsScaledByServerRadius()
        {
            var start = BossTelegraphStart.Fan(BOSS_ID, CAST_ID, "wolbaek_p02", Vector3.zero, Vector3.forward, 6f, 100f, _clock, 1f);
            _presenter.BeginTelegraph(start);
            yield return null;
            Assert.That(CountNamed("SB_Shockwave_Cone_Blue"), Is.EqualTo(0), "서버 타격 확정 전에는 나오지 않는다");

            yield return SetClock(START_CLOCK + 1.0);
            _presenter.Strike(CAST_ID, _clock);
            yield return null;

            Assert.That(CountNamed("SB_Shockwave_Cone_Blue"), Is.EqualTo(2));
            Transform cone = FindNamed("SB_Shockwave_Cone_Blue");
            Assert.That(cone.position.y, Is.EqualTo(2f).Within(TOLERANCE), "높이 2m");
        }

        // 원형 폭발은 서버 반경에 비례한 크기로 서버 타격 확정 사건에서 나온다
        [UnityTest]
        public IEnumerator Circle_ServerStrike_SpawnsBurstScaledByServerRadius()
        {
            var start = BossTelegraphStart.Circle(BOSS_ID, CAST_ID, "wolbaek_p03", Vector3.zero, 5f, _clock, 1.2f);
            _presenter.BeginTelegraph(start);
            yield return null;
            Assert.That(CountNamed("SB_Shockwave_Flash_Blue"), Is.EqualTo(0));

            yield return SetClock(START_CLOCK + 1.2);
            _presenter.Strike(CAST_ID, _clock);
            yield return null;

            Assert.That(CountNamed("SB_Shockwave_Flash_Blue"), Is.EqualTo(1));
            Transform burst = FindNamed("SB_Shockwave_Flash_Blue");
            Assert.That(burst.localScale.x, Is.EqualTo(1.92f).Within(0.01f), "반경 5m 기준 크기");
        }

        /****************************************
        *                Zone Tests
        ****************************************/

        // 장판은 활성 사건의 중심과 반경으로 켜고 소멸 사건에서 끈다
        [UnityTest]
        public IEnumerator Zone_ServerActivateAndExpire_FollowsServerCenterAndRadius()
        {
            var start = new BossAreaStart(BOSS_ID, 31, new Vector3(4f, 0f, 6f), 6f, 12f, 1f, _clock);
            Assert.That(_presenter.ActivateArea(start), Is.True);
            yield return null;

            Transform zone = FindNamed("SB_Vortex_Blue");
            Assert.That(zone, Is.Not.Null);
            Assert.That(zone.position.x, Is.EqualTo(4f).Within(TOLERANCE));
            Assert.That(zone.position.z, Is.EqualTo(6f).Within(TOLERANCE));
            Assert.That(zone.localScale.x, Is.EqualTo(2f).Within(0.01f), "서버 반경 6m는 크기 2");

            Assert.That(_presenter.EndArea(31, EBossAreaFate.Cancelled), Is.True);
            yield return null;
            yield return null;
            Assert.That(CountNamed("SB_Vortex_Blue"), Is.EqualTo(0), "취소되면 즉시 지운다");
        }

        /****************************************
        *            Parallel Beam Test
        ****************************************/

        // 평행 빔은 서버 예고 사건으로 바닥 예고를 보이고 서버 활성 사건에서 확정한다
        [UnityTest]
        public IEnumerator ParallelBeams_ServerStartAndActivate_RunsOnServerEvents()
        {
            WolbaekBeamPlayer sustain = BeamPlayer(EWolbaekBeamKind.Sustain);
            Assert.That(sustain, Is.Not.Null);

            var shapeA = new BossBeamShape(Vector3.zero, 18f, Vector3.forward, -5f, 2f);
            var shapeB = new BossBeamShape(Vector3.zero, 18f, Vector3.forward, 5f, 2f);
            Assert.That(_presenter.BeginBeam(new BossBeamStart(BOSS_ID, 41, CAST_ID, shapeA, _clock, 1.5f, 4f, 1f)), Is.True);
            Assert.That(_presenter.BeginBeam(new BossBeamStart(BOSS_ID, 42, CAST_ID, shapeB, _clock, 1.5f, 4f, 1f)), Is.True);
            yield return null;

            Assert.That(sustain.IsRunning, Is.True);
            Assert.That(sustain.VisibleLineCount, Is.EqualTo(2), "두 줄 예고");

            yield return SetClock(START_CLOCK + 1.5);
            _presenter.ActivateBeam(41, _clock);
            _presenter.ActivateBeam(42, _clock);
            yield return null;
            Assert.That(sustain.IsRunning, Is.True);
        }

        // 회전 별 빔은 서버 예고 사건으로 5선 예고를 보이고 서버 활성 사건에서 확정해 그린다
        [UnityTest]
        public IEnumerator RotatingStar_ServerStartAndActivate_ShowsConfirmedLines()
        {
            WolbaekBeamPlayer star = BeamPlayer(EWolbaekBeamKind.Star);
            Assert.That(star, Is.Not.Null);

            BossStarShape shape = BossStarGeometry.Place(Vector3.zero, 18f, Vector3.forward, 14f, 5, 2, 1.5f, 20f, true);
            var start = new BossStarStart(BOSS_ID, 81, CAST_ID, in shape, _clock, 2f, 6f, 0.5f);
            Assert.That(_presenter.BeginStar(in start), Is.True, "예고 시작");
            yield return null;

            Assert.That(star.IsRunning, Is.True, "재생 중");
            Assert.That(star.VisibleLineCount, Is.EqualTo(5), "5선 예고");

            yield return SetClock(START_CLOCK + 2.0);
            Assert.That(_presenter.ActivateStar(81, _clock), Is.True, "활성 사건");
            yield return null;
            yield return SetClock(START_CLOCK + 2.5);

            Assert.That(star.IsRunning, Is.True);
            Assert.That(star.VisibleLineCount, Is.EqualTo(5), "5선 확정");
        }

        /****************************************
        *      Server Confirm Ahead Of Clock Tests
        ****************************************/

        // 서버 확정 시각이 표시 시계보다 50ms 앞서 도착해도 사건을 잃지 않고 그 시각이 되면 내부 연출까지 켠다
        [UnityTest]
        public IEnumerator BeamRelease_StrikeAheadOfClock_ConfirmsWhenClockReaches()
        {
            WolbaekBeamPlayer release = BeamPlayer(EWolbaekBeamKind.Release);
            _presenter.BeginTelegraph(BossTelegraphStart.Line(BOSS_ID, CAST_ID, "wolbaek_p06", Vector3.zero, Vector3.forward, 14f, 4f, _clock, 1.6f));
            yield return null;

            yield return SetClock(START_CLOCK + 1.55);
            _presenter.Strike(CAST_ID, START_CLOCK + 1.6);
            yield return null;
            Assert.That(CountFullyDrawn(release), Is.EqualTo(0), "시각 도달 전에는 예고만");

            yield return SetClock(START_CLOCK + 1.62);
            yield return null;
            Assert.That(CountFullyDrawn(release), Is.EqualTo(1), "시각 도달 뒤 내부 연출까지");
        }

        // 보류 중 시전이 취소되면 시각이 지나도 부활하지 않는다
        [UnityTest]
        public IEnumerator BeamRelease_CancelledWhileDeferred_DoesNotRevive()
        {
            WolbaekBeamPlayer release = BeamPlayer(EWolbaekBeamKind.Release);
            _presenter.BeginTelegraph(BossTelegraphStart.Line(BOSS_ID, CAST_ID, "wolbaek_p06", Vector3.zero, Vector3.forward, 14f, 4f, _clock, 1.6f));
            yield return null;

            yield return SetClock(START_CLOCK + 1.55);
            _presenter.Strike(CAST_ID, START_CLOCK + 1.6);
            yield return null;
            _view.PatternVfx.OnCastEnded(CAST_ID, true);

            yield return SetClock(START_CLOCK + 1.7);
            yield return null;
            Assert.That(release.IsRunning, Is.False);
            Assert.That(CountFullyDrawn(release), Is.EqualTo(0), "취소 뒤 부활 없음");
        }

        [UnityTest]
        public IEnumerator ParallelBeams_ActivateAheadOfClock_ConfirmsWhenClockReaches()
        {
            WolbaekBeamPlayer sustain = BeamPlayer(EWolbaekBeamKind.Sustain);
            var shapeA = new BossBeamShape(Vector3.zero, 18f, Vector3.forward, -5f, 2f);
            var shapeB = new BossBeamShape(Vector3.zero, 18f, Vector3.forward, 5f, 2f);
            _presenter.BeginBeam(new BossBeamStart(BOSS_ID, 41, CAST_ID, shapeA, _clock, 1.5f, 4f, 1f));
            _presenter.BeginBeam(new BossBeamStart(BOSS_ID, 42, CAST_ID, shapeB, _clock, 1.5f, 4f, 1f));
            yield return null;

            yield return SetClock(START_CLOCK + 1.45);
            _presenter.ActivateBeam(41, START_CLOCK + 1.5);
            _presenter.ActivateBeam(42, START_CLOCK + 1.5);
            yield return null;
            Assert.That(CountFullyDrawn(sustain), Is.EqualTo(0), "시각 도달 전에는 예고만");

            yield return SetClock(START_CLOCK + 1.52);
            yield return null;
            Assert.That(CountFullyDrawn(sustain), Is.EqualTo(2), "두 줄 모두 내부 연출까지");
        }

        [UnityTest]
        public IEnumerator RotatingStar_ActivateAheadOfClock_ConfirmsWhenClockReaches()
        {
            WolbaekBeamPlayer star = BeamPlayer(EWolbaekBeamKind.Star);
            BossStarShape shape = BossStarGeometry.Place(Vector3.zero, 18f, Vector3.forward, 14f, 5, 2, 1.5f, 20f, true);
            var start = new BossStarStart(BOSS_ID, 81, CAST_ID, in shape, _clock, 2f, 6f, 0.5f);
            _presenter.BeginStar(in start);
            yield return null;

            yield return SetClock(START_CLOCK + 1.95);
            _presenter.ActivateStar(81, START_CLOCK + 2.0);
            yield return null;
            Assert.That(CountFullyDrawn(star), Is.EqualTo(0), "시각 도달 전에는 예고만");

            yield return SetClock(START_CLOCK + 2.02);
            yield return null;
            Assert.That(CountFullyDrawn(star), Is.EqualTo(5), "5선 내부 연출까지");
        }

        [UnityTest]
        public IEnumerator HardStarStage_StruckAheadOfClock_ConfirmsWhenClockReaches()
        {
            WolbaekBeamPlayer star = BeamPlayer(EWolbaekBeamKind.Star);
            BossStarShape shape = BossStarGeometry.Place(Vector3.zero, 18f, Vector3.forward, 14f, 5, 2, 1.5f, 0f, true);
            var start = new BossStarStageStart(BOSS_ID, CAST_ID, 71, shape, 30f, 3, _clock, 2f, 0.8f);
            _view.PatternVfx.OnStarStageStarted(in start);
            yield return null;

            yield return SetClock(START_CLOCK + 1.95);
            _view.PatternVfx.OnStarStageStruck(CAST_ID, 0, START_CLOCK + 2.0);
            yield return null;
            Assert.That(CountFullyDrawn(star), Is.EqualTo(0), "시각 도달 전에는 예고만");

            yield return SetClock(START_CLOCK + 2.02);
            yield return null;
            Assert.That(CountFullyDrawn(star), Is.EqualTo(5), "0단계 5선 내부 연출까지");
        }

        [UnityTest]
        public IEnumerator HardGrid_StruckAheadOfClock_ConfirmsWhenClockReaches()
        {
            WolbaekBeamPlayer grid = BeamPlayer(EWolbaekBeamKind.Grid);
            var start = new BossGridBeamStart(BOSS_ID, CAST_ID, Vector3.zero, 18f, 12, 3f, 3f, _clock, 1f, 0.5f, 1.5f);
            _view.PatternVfx.OnGridBeamStarted(in start);
            _view.PatternVfx.OnGridBeamLineTelegraphed(CAST_ID, 0, true, 0, START_CLOCK, START_CLOCK + 1.0);
            yield return null;

            yield return SetClock(START_CLOCK + 0.95);
            _view.PatternVfx.OnGridBeamLineStruck(CAST_ID, 0, true, 0, START_CLOCK + 1.0);
            yield return null;
            Assert.That(CountFullyDrawn(grid), Is.EqualTo(0), "시각 도달 전에는 예고만");

            yield return SetClock(START_CLOCK + 1.02);
            yield return null;
            Assert.That(CountFullyDrawn(grid), Is.EqualTo(1), "줄 내부 연출까지");
        }

        /****************************************
        *        Dash Telegraph And Sweep Tests
        ****************************************/

        // 돌진 예고 사건에서 발밑 예고 효과가 한 번 나온다
        [UnityTest]
        public IEnumerator Dash_ServerTelegraph_SpawnsFootEffect()
        {
            var start = BossTelegraphStart.Dash(BOSS_ID, CAST_ID, "wolbaek_p01", new Vector3(2f, 0f, 3f), Vector3.forward, 8f, 2f, _clock, 0.8f);
            _presenter.BeginTelegraph(start);
            yield return null;

            Transform effect = FindNamed("PF_VFX_WolbaekP01Telegraph");
            Assert.That(effect, Is.Not.Null);
            Assert.That(effect.position.x, Is.EqualTo(2f).Within(TOLERANCE));
            Assert.That(effect.position.z, Is.EqualTo(3f).Within(TOLERANCE));
        }

        // 이지 2P P03 반월은 구역 폭발 사건마다 그 구역에 부채꼴이 나온다
        [UnityTest]
        public IEnumerator Sweep_ZoneBlast_SpawnsSectorAtZone()
        {
            var zone = BossTelegraphStart.Fan(BOSS_ID, 61, "wolbaek_ph2_p03", Vector3.zero, new Vector3(1f, 0f, 1f), 6f, 45f, _clock, 1.2f);
            _presenter.BeginTelegraph(zone);
            yield return null;
            Assert.That(CountNamed("SB_Shockwave_Cone_Blue"), Is.EqualTo(0));

            yield return SetClock(START_CLOCK + 1.2);
            _presenter.Strike(61, _clock);
            yield return null;

            Assert.That(CountNamed("SB_Shockwave_Cone_Blue"), Is.EqualTo(1), "구역 하나당 한 장");
        }

        /****************************************
        *            Hard Phase Tests
        ****************************************/

        // 하드 단계 별은 단계마다 확정하고 다음 단계를 예고한다
        [UnityTest]
        public IEnumerator HardStarStage_ServerEvents_ShowsStageLines()
        {
            WolbaekBeamPlayer star = BeamPlayer(EWolbaekBeamKind.Star);
            BossStarShape shape = BossStarGeometry.Place(Vector3.zero, 18f, Vector3.forward, 14f, 5, 2, 1.5f, 0f, true);
            var start = new BossStarStageStart(BOSS_ID, CAST_ID, 71, shape, 30f, 3, _clock, 2f, 0.8f);
            _view.PatternVfx.OnStarStageStarted(in start);
            yield return null;

            Assert.That(star.IsRunning, Is.True);
            Assert.That(star.VisibleLineCount, Is.EqualTo(10), "0단계와 1단계 예고가 두 묶음으로 보인다");

            yield return SetClock(START_CLOCK + 2.0);
            _view.PatternVfx.OnStarStageStruck(CAST_ID, 0, _clock);
            yield return null;
            Assert.That(star.IsRunning, Is.True);

            _view.PatternVfx.OnHardCancelled(CAST_ID);
            yield return null;
            Assert.That(star.IsRunning, Is.False, "취소되면 끈다");
        }

        // 하드 격자는 서버가 알린 줄마다 예고와 타격을 그린다
        [UnityTest]
        public IEnumerator HardGrid_ServerLineEvents_ShowsLines()
        {
            WolbaekBeamPlayer grid = BeamPlayer(EWolbaekBeamKind.Grid);
            var start = new BossGridBeamStart(BOSS_ID, CAST_ID, Vector3.zero, 18f, 12, 3f, 3f, _clock, 1f, 0.5f, 1.5f);
            _view.PatternVfx.OnGridBeamStarted(in start);
            _view.PatternVfx.OnGridBeamLineTelegraphed(CAST_ID, 0, true, 0, _clock, _clock + 1.0);
            yield return null;

            Assert.That(grid.IsRunning, Is.True);
            Assert.That(grid.VisibleLineCount, Is.EqualTo(1));

            yield return SetClock(START_CLOCK + 1.0);
            _view.PatternVfx.OnGridBeamLineStruck(CAST_ID, 0, true, 0, _clock);
            yield return null;
            Assert.That(grid.IsRunning, Is.True);
        }

        // 하드 잔류 부채꼴은 서버 구역 배치대로 구역 방향에 효과를 낸다
        [UnityTest]
        public IEnumerator HardChainFan_ZoneBlast_UsesServerZoneDirection()
        {
            var start = new BossChainFanStart(BOSS_ID, CAST_ID, Vector3.zero, Vector3.back, 12f, 7, 45f, _clock, 0.5f, 2.5f, 0.5f, 3f);
            _view.PatternVfx.OnChainFanStarted(in start);
            _view.PatternVfx.OnChainFanBlasted(CAST_ID, 0, _clock);
            yield return null;

            Transform cone = FindNamed("SB_Shockwave_Cone_Blue");
            Assert.That(cone, Is.Not.Null);

            // 피난 각도 45도의 절반 + 구역 각도의 절반 = 45도, 뒤쪽 피난 방향에서 시계 방향으로 이어진다
            Vector3 expected = Quaternion.AngleAxis(45f, Vector3.up) * Vector3.back;
            Assert.That(Vector3.Dot(-cone.forward, expected), Is.GreaterThan(0.99f));
            Assert.That(cone.localScale.x, Is.EqualTo(6f).Within(0.01f), "사거리 12m는 크기 6");
        }

        // 하드 원형 폭발은 서버가 알린 원마다 같은 순간에 나온다
        [UnityTest]
        public IEnumerator HardCircleBurst_ServerStrike_SpawnsOnePerCircle()
        {
            var start = new BossCircleBurstStart(BOSS_ID, CAST_ID, new[] { new Vector3(0f, 0f, 0f), new Vector3(6f, 0f, 6f) },
                new[] { 5.5f, 3f }, _clock, 2f);
            _view.PatternVfx.OnCircleBurstStarted(in start);
            yield return null;
            Assert.That(CountNamed("SB_Shockwave_Flash_Blue"), Is.EqualTo(0));

            _view.PatternVfx.OnCircleBurstStruck(CAST_ID, _clock);
            yield return null;
            Assert.That(CountNamed("SB_Shockwave_Flash_Blue"), Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator HardRestore_MidCast_KeepsCurrentStarAndDoesNotReplayPastImpacts()
        {
            BossStarShape shape = BossStarGeometry.Place(Vector3.zero, 18f, Vector3.forward, 14f, 5, 2, 1.5f, 0f, true);
            var starStart = new BossStarStageStart(BOSS_ID, CAST_ID, 71, shape, 30f, 3, START_CLOCK, 2f, 0.8f);
            var fanStart = new BossChainFanStart(BOSS_ID, CAST_ID + 1, Vector3.zero, Vector3.back, 12f, 7, 45f,
                START_CLOCK, 0.5f, 2.5f, 0.5f, 3f);
            var circleStart = new BossCircleBurstStart(BOSS_ID, CAST_ID + 2, new[] { Vector3.zero }, new[] { 5f },
                START_CLOCK, 2f);

            yield return SetClock(START_CLOCK + 2.4);
            _view.PatternVfx.RestoreStarStage(new BossStarStageSnapshot { Start = starStart, StruckCount = 1 });
            _view.PatternVfx.RestoreChainFan(new BossChainFanSnapshot { Start = fanStart, BlastedCount = 2 });
            _view.PatternVfx.RestoreCircleBurst(new BossCircleBurstSnapshot { Start = circleStart, IsStruck = true });
            yield return null;

            Assert.That(BeamPlayer(EWolbaekBeamKind.Star).IsRunning, Is.True, "진행 중인 단계 별은 이어 그린다");
            Assert.That(CountNamed("SB_Shockwave_Cone_Blue"), Is.EqualTo(0), "이미 폭발한 부채꼴은 다시 만들지 않는다");
            Assert.That(CountNamed("SB_Shockwave_Flash_Blue"), Is.EqualTo(0), "이미 타격한 원형 폭발은 다시 만들지 않는다");

            _view.PatternVfx.OnChainFanBlasted(CAST_ID + 1, 2, START_CLOCK + 3.5);
            yield return null;
            Assert.That(CountNamed("SB_Shockwave_Cone_Blue"), Is.EqualTo(1), "복원 뒤 새 서버 사건은 재생한다");
        }

        // 난사 시전의 투사체는 손이 아니라 가슴에서 나간다
        [UnityTest]
        public IEnumerator HardBarrage_Projectile_StartsAtChest()
        {
            Animator animator = _root.GetComponentInChildren<Animator>();
            var barrage = new BossBarrageStart(BOSS_ID, CAST_ID, Vector3.zero, _clock, 8, 1f, 0.2f, 1f);
            _view.PatternVfx.OnBarrageStarted(in barrage);

            _presenter.LaunchProjectile(new BossProjectileStart(BOSS_ID, 81, CAST_ID, Vector3.zero, Vector3.right, 10f, 1f, 10f, _clock));
            yield return null;

            WolbaekFoxfirePlayer flight = null;
            foreach (WolbaekFoxfirePlayer player in FoxfirePlayers())
            {
                if (player.IsRunning && (player.State == EWolbaekFoxfireState.Flight))
                {
                    flight = player;
                }
            }

            Assert.That(flight, Is.Not.Null);
            Vector3 chest = animator.GetBoneTransform(HumanBodyBones.Chest).position;
            Vector3 hand = animator.GetBoneTransform(HumanBodyBones.RightHand).position;
            Assert.That(Vector3.Distance(flight.transform.position, chest), Is.LessThan(Vector3.Distance(flight.transform.position, hand)));
        }

        // 사건 종류로 정하는 패턴 모션은 첫 판정 시각에 힘 순간이 오게 시작한다
        [UnityTest]
        public IEnumerator EventMotion_GridBeam_StartsAtStrikeMinusPeak()
        {
            Animator animator = _root.GetComponentInChildren<Animator>();
            _view.BeginEventMotion(CAST_ID, EWolbaekEventMotion.GridBeam, _clock, 1f);

            yield return SetClock(START_CLOCK + 0.1);
            yield return null;
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("attack04_inplace") || animator.GetNextAnimatorStateInfo(0).IsName("attack04_inplace"), Is.False);

            yield return SetClock(START_CLOCK + 0.3);
            yield return null;
            yield return null;
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("attack04_inplace") || animator.GetNextAnimatorStateInfo(0).IsName("attack04_inplace"), Is.True, "지연 0.2초 뒤 시작");
        }

        /****************************************
        *                Star Test
        ****************************************/

        // 별 빔은 서버 예고 사건으로 5선을 보이고 서버 활성 사건에서 확정해 회전한다
        [UnityTest]
        public IEnumerator Star_ServerStartAndActivate_ShowsFiveLines()
        {
            WolbaekBeamPlayer star = BeamPlayer(EWolbaekBeamKind.Star);
            Assert.That(star, Is.Not.Null);

            BossStarShape shape = BossStarGeometry.Place(Vector3.zero, 18f, Vector3.forward, 14f, 5, 2, 1.5f, 20f, true);
            Assert.That(_presenter.BeginStar(new BossStarStart(BOSS_ID, 51, CAST_ID, shape, _clock, 2f, 6f, 1f)), Is.True);
            yield return null;

            Assert.That(star.IsRunning, Is.True);
            Assert.That(star.VisibleLineCount, Is.EqualTo(5));

            yield return SetClock(START_CLOCK + 2.0);
            Assert.That(_presenter.ActivateStar(51, _clock), Is.True);
            yield return SetClock(START_CLOCK + 3.0);
            Assert.That(star.IsRunning, Is.True);

            Assert.That(_presenter.CancelStar(51), Is.True);
            yield return null;
            Assert.That(star.IsRunning, Is.False, "취소되면 끈다");
        }
    }
}
