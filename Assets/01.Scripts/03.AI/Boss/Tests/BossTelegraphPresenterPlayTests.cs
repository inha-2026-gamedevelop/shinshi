// System
using System.Collections;
using System.Collections.Generic;

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
    // 임시 표현 프리팹을 실제로 생성해 플레이 모드에서 재생한다. 서버 어셈블리를 참조하지 않으므로 표현 진입점이
    // 서버 없이 동작하는지도 함께 보인다. 화면에 나온 모양과 색은 이 테스트가 확인하지 않는다
    public class BossTelegraphPresenterPlayTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string SAMPLE_PREFAB_PATH = "Assets/01.Scripts/03.AI/Boss/VFX/Generated/PF_BossTelegraphSample.prefab";
        private const int    POOL_CAPACITY      = 4;
        private const int    BOSS_ID            = 1;
        private const float  DASH_SPEED         = 8f / 0.45f;
        private const float  TOLERANCE          = 0.05f;
        private const float  WAIT_LIMIT         = 4f;

        /****************************************
        *                Fields
        ****************************************/

        private GameObject                _root;
        private BossTelegraphPresenter    _presenter;
        private SectorVFXPool             _sectorPool;
        private RectTelegraphVFXPool      _rectPool;
        private AreaGroundVFXPool         _areaPool;
        private double                    _clock;
        private bool                      _useRealClock;
        private readonly List<string>     _events = new List<string>();

        private double Now => _useRealClock ? Time.timeAsDouble : _clock;

        /****************************************
        *               Set Up
        ****************************************/

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(SAMPLE_PREFAB_PATH);
#else
            GameObject prefab = null;
#endif
            Assert.IsNotNull(prefab, "임시 표현 프리팹이 없다, Shinshi/VFX/Create Boss Telegraph Prefabs를 먼저 실행한다");

            _root = Object.Instantiate(prefab);
            _root.GetComponent<BossTelegraphSampleInput>().enabled = false; // 시각표 재생은 끄고 테스트가 직접 부른다
            _presenter  = _root.GetComponent<BossTelegraphPresenter>();
            _sectorPool = _root.GetComponentInChildren<SectorVFXPool>();
            _rectPool   = _root.GetComponentInChildren<RectTelegraphVFXPool>();
            _areaPool   = _root.GetComponentInChildren<AreaGroundVFXPool>();

            _clock        = 100.0;
            _useRealClock = false;
            _events.Clear();
            _presenter.SetServerClock(() => Now);
            _presenter.PhaseChanged += (castId, phase) => _events.Add(castId + ":" + phase);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
            }
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static Vector3 Polar(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }

        private BossTelegraphStart Fan(int castId, float seconds, int bossId = BOSS_ID, double? start = null)
        {
            return BossTelegraphStart.Fan(bossId, castId, "wolbaek_p02", Vector3.zero, Vector3.forward, 6f, 100f,
                start ?? Now, seconds);
        }

        private BossTelegraphStart Circle(int castId, float seconds, int bossId = BOSS_ID)
        {
            return BossTelegraphStart.Circle(bossId, castId, "wolbaek_p03", Vector3.zero, 5f, Now, seconds);
        }

        private BossTelegraphStart Line(int castId, int bossId = BOSS_ID)
        {
            return BossTelegraphStart.Line(bossId, castId, "wolbaek_p06", Vector3.zero, Vector3.forward, 14f, 4f, Now, 1.6f);
        }

        private BossTelegraphStart Dash(int castId, int bossId = BOSS_ID)
        {
            return BossTelegraphStart.Dash(bossId, castId, "wolbaek_p01", Vector3.zero, Vector3.forward, 8f, 2f, Now, 0.8f);
        }

        private void Visuals(int castId, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect)
        {
            Assert.IsTrue(_presenter.TryGetVisuals(castId, out cast, out sector, out rect), "진행 중인 표현이 없다 " + castId);
        }

        private BossProjectileStart Shot(int id, float pathLength = 35f, double? launch = null, int bossId = BOSS_ID,
            Vector3? direction = null)
        {
            return new BossProjectileStart(bossId, id, Vector3.zero, direction ?? Vector3.forward, 10f, 1f, pathLength, launch ?? Now);
        }

        private BossTelegraphStart ProjectileTelegraph(int castId, int bossId = BOSS_ID)
        {
            return BossTelegraphStart.Projectile(bossId, castId, "wolbaek_p04", Vector3.zero, Vector3.forward, 35f, 1f, Now, 0.9f);
        }

        private void ProjectileVisual(int id, out BossProjectileTrack track, out RectTelegraphVFXController rect)
        {
            Assert.IsTrue(_presenter.TryGetProjectile(id, out track, out rect), "날고 있는 투사체가 없다 " + id);
        }

        private BossAreaStart AreaShot(int id, Vector3? center = null, double? activate = null, int bossId = BOSS_ID)
        {
            return new BossAreaStart(bossId, id, center ?? new Vector3(4f, 0f, 5f), 3f, 6f, 1f, activate ?? Now);
        }

        private void AreaVisual(int id, out BossAreaTrack track, out AreaGroundVFXController visual)
        {
            Assert.IsTrue(_presenter.TryGetArea(id, out track, out visual), "활성된 장판이 없다 " + id);
        }

        private IEnumerator WaitUntil(System.Func<bool> condition)
        {
            float until = Time.realtimeSinceStartup + WAIT_LIMIT;
            while ((!condition()) && (Time.realtimeSinceStartup < until))
            {
                yield return null;
            }

            Assert.IsTrue(condition(), "시간 안에 조건이 만족되지 않았다");
        }

        private void AssertAllReturned()
        {
            Assert.AreEqual(0, _presenter.ActiveCount);
            Assert.AreEqual(0, _presenter.ProjectileCount);
            Assert.AreEqual(0, _presenter.AreaCount);
            Assert.AreEqual(0, _areaPool.LeasedCount);
            Assert.AreEqual(POOL_CAPACITY, _areaPool.AvailableCount);
            Assert.AreEqual(0, _sectorPool.LeasedCount);
            Assert.AreEqual(POOL_CAPACITY, _sectorPool.AvailableCount);
            Assert.AreEqual(0, _rectPool.LeasedCount);
            Assert.AreEqual(POOL_CAPACITY, _rectPool.AvailableCount);
        }

        /****************************************
        *            Fan And Circle
        ****************************************/

        // 충전이 끝나도 서버 확정이 오기 전에는 폭발하지 않고 멈춰 있다가, 확정이 오면 이어서 재생하고 끝나면 풀로 돌아온다
        [UnityTest]
        public IEnumerator Fan_HoldsAtChargeEnd_UntilStrike_ThenPlaysAndReturnsToPool()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(Fan(1, 0.2f)));
            yield return new WaitForSeconds(0.45f);

            Visuals(1, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect);
            Assert.IsNotNull(sector);
            Assert.IsNull(rect);
            Assert.IsTrue(sector.IsHolding, "충전 끝에서 멈춰 있어야 한다");
            Assert.LessOrEqual(sector.Elapsed, 0.2f);

            Assert.IsTrue(_presenter.Strike(1, Now));
            yield return new WaitForSeconds(0.1f);
            Assert.Greater(sector.Elapsed, 0.2f, "확정 뒤에는 폭발로 이어진다");
            Assert.IsFalse(sector.IsHolding);

            yield return WaitUntil(() => _presenter.ActiveCount == 0);
            yield return null;
            AssertAllReturned();
            CollectionAssert.Contains(_events, "1:Execute");
            CollectionAssert.Contains(_events, "1:Finished");
        }

        [UnityTest]
        public IEnumerator Fan_ReceivedLate_ContinuesFromElapsedServerTime()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(Fan(1, 0.4f, start: Now - 0.3)));
            yield return null;

            Visuals(1, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect);
            Assert.GreaterOrEqual(sector.Elapsed, 0.3f, "이미 흐른 시간만큼 건너뛰어 시작해야 한다");
        }

        [UnityTest]
        public IEnumerator Fan_NeverConfirmed_IsClearedAfterGrace_AndLateStrikeCannotRevive()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(Fan(1, 0.1f)));

            yield return WaitUntil(() => _presenter.ActiveCount == 0);
            yield return null;

            AssertAllReturned();
            Assert.IsFalse(_presenter.Strike(1, Now));
            Assert.IsFalse(_presenter.BeginTelegraph(Fan(1, 0.1f)), "끝난 시전 번호의 시작은 받아들이지 않는다");
            CollectionAssert.Contains(_events, "1:Cancelled");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Cancel_BeforeStrike_StopsVisualAndBlocksLateNotifications(bool isFan)
        {
            _useRealClock = true;
            BossTelegraphStart start = isFan ? Fan(1, 1f) : Circle(1, 1.2f);
            Assert.IsTrue(_presenter.BeginTelegraph(start));
            Visuals(1, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect);
            Assert.IsTrue(sector.IsPlaying);

            Assert.IsTrue(_presenter.Cancel(1));

            Assert.IsFalse(sector.IsPlaying);
            AssertAllReturned();
            Assert.IsFalse(_presenter.UpdateAim(1, Polar(30f), 6f, Now));
            Assert.IsFalse(_presenter.LockDirection(1, Polar(30f), 6f));
            Assert.IsFalse(_presenter.Strike(1, Now));
            Assert.IsFalse(_presenter.BeginDash(1, 8f, 0.45f, DASH_SPEED, Now));
            Assert.IsFalse(_presenter.Cancel(1));
            Assert.IsFalse(_presenter.BeginTelegraph(start), "취소된 시전 번호의 중복 시작은 되살리지 못한다");
            Assert.AreEqual(0, _presenter.ActiveCount);
            Assert.AreEqual(0, _sectorPool.LeasedCount);
        }

        // 부채꼴과 원형이 정의의 반경과 각도로 만든 메시를 그린다
        [Test]
        public void FanAndCircle_BuildMeshesWithDefinitionSizes()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(Fan(1, 1f, bossId: 1)));
            Assert.IsTrue(_presenter.BeginTelegraph(Circle(2, 1.2f, bossId: 2)));

            Visuals(1, out BossTelegraphCast fanCast, out SectorVFXController fan, out RectTelegraphVFXController r1);
            Visuals(2, out BossTelegraphCast circleCast, out SectorVFXController circle, out RectTelegraphVFXController r2);

            Bounds fanBounds    = fan.GetComponent<MeshFilter>().sharedMesh.bounds;
            Bounds circleBounds = circle.GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.AreEqual(6f * Mathf.Sin(50f * Mathf.Deg2Rad) * 2f, fanBounds.size.x, TOLERANCE);
            Assert.AreEqual(6f, fanBounds.max.z, TOLERANCE);
            Assert.AreEqual(10f, circleBounds.size.x, TOLERANCE);
            Assert.AreEqual(10f, circleBounds.size.z, TOLERANCE);
        }

        /****************************************
        *            Line And Dash
        ****************************************/

        [UnityTest]
        public IEnumerator Line_ShowsDefinitionSize_FollowsAim_ThenFlashesAndReturns()
        {
            Assert.IsTrue(_presenter.BeginTelegraph(Line(1)));
            yield return null;
            Visuals(1, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect);
            Assert.IsNull(sector);
            Assert.IsTrue(rect.IsShown);
            Assert.AreEqual(4f, rect.transform.localScale.x, 0.0001f);
            Assert.AreEqual(14f, rect.transform.localScale.z, 0.0001f);

            _clock += 0.2;
            Assert.IsTrue(_presenter.UpdateAim(1, Polar(40f), 14f, _clock));
            yield return new WaitForSeconds(0.3f);
            Assert.Less(Quaternion.Angle(rect.transform.rotation, Quaternion.LookRotation(Polar(40f))), 0.5f);

            _clock += 0.4;
            Assert.IsTrue(_presenter.LockDirection(1, Polar(50f), 14f));
            Assert.IsFalse(_presenter.UpdateAim(1, Polar(10f), 14f, _clock + 0.1), "고정 뒤 갱신은 무시한다");
            _clock += 1.0;
            Assert.IsTrue(_presenter.Strike(1, _clock));
            yield return null;
            Assert.AreEqual(0f, Quaternion.Angle(rect.transform.rotation, Quaternion.LookRotation(Polar(50f))), 0.5f);

            _clock += rect.FlashSeconds + rect.DissolveSeconds + 0.1;
            yield return null;
            yield return null;

            AssertAllReturned();
            Assert.IsFalse(rect.IsShown);
            CollectionAssert.Contains(_events, "1:Finished");
        }

        [UnityTest]
        public IEnumerator Dash_ShowsWallLimitedLength_MovesAtServerSpeed_ThenStopsAndReturns()
        {
            Assert.IsTrue(_presenter.BeginTelegraph(Dash(1)));
            yield return null;
            Visuals(1, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect);
            Assert.AreEqual(2f, rect.transform.localScale.x, 0.0001f);
            Assert.AreEqual(8f, rect.transform.localScale.z, 0.0001f);

            _clock += 0.2;
            Assert.IsTrue(_presenter.UpdateAim(1, Polar(20f), 5f, _clock));
            yield return null;
            Assert.AreEqual(5f, rect.transform.localScale.z, 0.0001f, "서버가 보낸 벽 제한 길이를 그대로 표시한다");

            _clock += 0.2;
            Assert.IsTrue(_presenter.LockDirection(1, Polar(20f), 5f));
            double dashStart = _clock + 0.4;
            _clock = dashStart + 0.1;
            Assert.IsTrue(_presenter.BeginDash(1, 5f, 0.45f, DASH_SPEED, dashStart));
            Assert.IsFalse(_presenter.Strike(1, _clock), "돌진은 타격 알림으로 시작하지 않는다");
            yield return null;
            Assert.AreEqual(DASH_SPEED * 0.1f, cast.GetDashTraveled(_clock), 0.01f);

            Assert.IsTrue(_presenter.StopDash(1, Polar(20f) * 5f, true));
            Assert.AreEqual(5f, cast.GetDashTraveled(_clock + 5.0), 0.01f);

            _clock = dashStart + 0.45;
            yield return null;
            _clock += rect.DissolveSeconds + 0.1;
            yield return null;
            yield return null;

            AssertAllReturned();
            Assert.IsFalse(rect.IsShown);
        }

        [UnityTest]
        public IEnumerator Dash_CancelledMidDash_HidesImmediatelyAndStaysCancelled()
        {
            Assert.IsTrue(_presenter.BeginTelegraph(Dash(1)));
            _clock += 0.8;
            Assert.IsTrue(_presenter.LockDirection(1, Vector3.forward, 8f));
            Assert.IsTrue(_presenter.BeginDash(1, 8f, 0.45f, DASH_SPEED, _clock));
            yield return null;
            Visuals(1, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect);
            Assert.IsTrue(rect.IsShown);

            Assert.IsTrue(_presenter.Cancel(1));

            Assert.IsFalse(rect.IsShown);
            AssertAllReturned();
            Assert.IsFalse(_presenter.StopDash(1, Vector3.forward * 2f, false));
            Assert.IsFalse(_presenter.BeginDash(1, 8f, 0.45f, DASH_SPEED, _clock));
            _clock += 5.0;
            yield return null;
            Assert.AreEqual(0, _presenter.ActiveCount);
        }

        /****************************************
        *               Restore
        ****************************************/

        [UnityTest]
        public IEnumerator Restore_MidDash_ContinuesFromServerTimeNotFromStart()
        {
            double dashStart = _clock - 0.2;
            BossTelegraphSnapshot snapshot = new BossTelegraphSnapshot
            {
                Start               = BossTelegraphStart.Dash(BOSS_ID, 5, "wolbaek_p01", Vector3.zero, Vector3.forward, 8f, 2f, dashStart - 0.8, 0.8f),
                Direction           = Vector3.forward,
                Length              = 8f,
                IsDirectionLocked   = true,
                Phase               = EBossTelegraphPhase.Execute,
                ExecuteServerTime   = dashStart,
                DashDistance        = 8f,
                DashSeconds         = 0.45f,
                DashSpeed           = DASH_SPEED,
                DashStoppedDistance = -1f
            };

            Assert.IsTrue(_presenter.Restore(in snapshot));
            yield return null;

            Visuals(5, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect);
            Assert.IsTrue(rect.IsShown);
            Assert.AreEqual(DASH_SPEED * 0.2f, cast.GetDashTraveled(_clock), 0.01f);
            Assert.IsFalse(_presenter.Restore(in snapshot), "같은 시전 번호의 중복 복원은 무시한다");
        }

        [UnityTest]
        public IEnumerator Restore_ExecutedFan_SkipsChargeAndDoesNotHold()
        {
            _useRealClock = true;
            double executeTime = Now - 0.1;
            BossTelegraphSnapshot snapshot = new BossTelegraphSnapshot
            {
                Start             = Fan(6, 0.2f, start: executeTime - 0.2),
                Direction         = Vector3.forward,
                Length            = 6f,
                IsDirectionLocked = true,
                Phase             = EBossTelegraphPhase.Execute,
                ExecuteServerTime = executeTime
            };

            Assert.IsTrue(_presenter.Restore(in snapshot));
            yield return null;

            Visuals(6, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect);
            Assert.GreaterOrEqual(sector.Elapsed, 0.2f + 0.1f - 0.02f);
            Assert.IsFalse(sector.IsHolding);

            yield return WaitUntil(() => _presenter.ActiveCount == 0);
            yield return null;
            AssertAllReturned();
        }

        [Test]
        public void Restore_EndedOrExpiredSnapshots_AreIgnoredAndCannotBeRestartedLater()
        {
            BossTelegraphSnapshot ended = new BossTelegraphSnapshot
            {
                Start = Line(8), Direction = Vector3.forward, Length = 14f, Phase = EBossTelegraphPhase.Cancelled, DashStoppedDistance = -1f
            };
            BossTelegraphSnapshot expired = new BossTelegraphSnapshot
            {
                Start = BossTelegraphStart.Line(BOSS_ID, 9, "wolbaek_p06", Vector3.zero, Vector3.forward, 14f, 4f, Now - 5.0, 1.6f),
                Direction = Vector3.forward, Length = 14f, Phase = EBossTelegraphPhase.Telegraph, DashStoppedDistance = -1f
            };

            Assert.IsFalse(_presenter.Restore(in ended));
            Assert.IsFalse(_presenter.Restore(in expired));
            Assert.IsFalse(_presenter.BeginTelegraph(Line(8)));
            Assert.IsFalse(_presenter.BeginTelegraph(BossTelegraphStart.Line(BOSS_ID, 9, "wolbaek_p06", Vector3.zero, Vector3.forward, 14f, 4f, Now - 5.0, 1.6f)));
            AssertAllReturned();
        }

        [Test]
        public void TryGetSnapshot_RoundTripsIntoRestoreAfterEnd()
        {
            Assert.IsTrue(_presenter.BeginTelegraph(Line(3)));
            Assert.IsTrue(_presenter.UpdateAim(3, Polar(25f), 14f, Now + 0.1));
            Assert.IsTrue(_presenter.TryGetSnapshot(3, out BossTelegraphSnapshot snapshot));

            Assert.AreEqual(3, snapshot.Start.CastId);
            Assert.AreEqual(25f, Vector3.SignedAngle(Vector3.forward, snapshot.Direction, Vector3.up), TOLERANCE);
            Assert.AreEqual(EBossTelegraphPhase.Telegraph, snapshot.Phase);
            Assert.IsFalse(snapshot.IsDirectionLocked);
        }

        /****************************************
        *          Duplicates And Reuse
        ****************************************/

        [Test]
        public void Begin_DuplicateCastId_IsIgnored()
        {
            Assert.IsTrue(_presenter.BeginTelegraph(Line(1)));
            Assert.IsFalse(_presenter.BeginTelegraph(Line(1)));

            Assert.AreEqual(1, _presenter.ActiveCount);
            Assert.AreEqual(1, _rectPool.LeasedCount);
        }

        // 취소가 시작보다 먼저 도착해도 나중에 온 시작이 표현을 만들지 않는다
        [Test]
        public void Cancel_ArrivingBeforeStart_BlocksTheLateStart()
        {
            Assert.IsFalse(_presenter.Cancel(77));

            Assert.IsFalse(_presenter.BeginTelegraph(Line(77)));
            AssertAllReturned();
        }

        [Test]
        public void Begin_NewCastOfSameBoss_RetiresPreviousVisual()
        {
            Assert.IsTrue(_presenter.BeginTelegraph(Fan(1, 1f)));
            Assert.AreEqual(1, _sectorPool.LeasedCount);

            Assert.IsTrue(_presenter.BeginTelegraph(Line(2)));

            Assert.AreEqual(1, _presenter.ActiveCount);
            Assert.AreEqual(0, _sectorPool.LeasedCount);
            Assert.AreEqual(1, _rectPool.LeasedCount);
            Assert.IsFalse(_presenter.UpdateAim(1, Polar(10f), 6f, Now + 0.1), "이전 시전은 끝났다");
        }

        [UnityTest]
        public IEnumerator Reuse_ManyCasts_ResetsStateAndLeaksNothing()
        {
            _useRealClock = true;
            for (int i = 1; i <= 12; ++i)
            {
                bool useFan = (i % 2) == 1;
                Assert.IsTrue(_presenter.BeginTelegraph(useFan ? Fan(i, 0.5f) : Line(i)));
                yield return null;

                Visuals(i, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect);
                if (useFan)
                {
                    Assert.Less(sector.Elapsed, 0.2f, "재사용한 연출은 처음부터 시작해야 한다");
                    Assert.IsFalse(sector.IsHolding);
                }
                else
                {
                    Assert.IsTrue(rect.IsShown);
                    Assert.AreEqual(14f, rect.Length, 0.0001f);
                    Assert.AreEqual(4f, rect.Width, 0.0001f);
                }

                Assert.IsTrue(_presenter.Cancel(i));
                AssertAllReturned();
            }
        }

        [UnityTest]
        public IEnumerator Disable_ReleasesEverythingAndPresenterWorksAgainAfterEnable()
        {
            Assert.IsTrue(_presenter.BeginTelegraph(Fan(1, 1f, bossId: 1)));
            Assert.IsTrue(_presenter.BeginTelegraph(Line(2, bossId: 2)));
            Visuals(2, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect);

            _root.SetActive(false);
            yield return null;
            Assert.IsFalse(rect.IsShown);
            Assert.AreEqual(0, _presenter.ActiveCount);

            _root.SetActive(true);
            yield return null;
            AssertAllReturned();
            Assert.IsTrue(_presenter.BeginTelegraph(Line(3)));
            Assert.AreEqual(1, _presenter.ActiveCount);
        }

        // 풀이 비어도 예외 없이 모델만 따라가고 표현만 생략한다
        [Test]
        public void PoolExhausted_SkipsVisualWithoutErrors()
        {
            _useRealClock = true;
            for (int i = 1; i <= POOL_CAPACITY + 1; ++i)
            {
                Assert.IsTrue(_presenter.BeginTelegraph(Fan(i, 1f, bossId: i)));
            }

            Assert.AreEqual(POOL_CAPACITY + 1, _presenter.ActiveCount);
            Visuals(POOL_CAPACITY + 1, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect);
            Assert.IsNull(sector);
            Assert.IsTrue(_presenter.Strike(POOL_CAPACITY + 1, Now));

            _presenter.ClearAll();
            AssertAllReturned();
        }

        [Test]
        public void ClearAll_StopsEveryVisualAndLateNotificationsCannotRevive()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(Fan(1, 1f, bossId: 1)));
            Assert.IsTrue(_presenter.BeginTelegraph(Dash(2, bossId: 2)));

            _presenter.ClearAll();

            AssertAllReturned();
            Assert.IsFalse(_presenter.Strike(1, Now));
            Assert.IsFalse(_presenter.BeginDash(2, 8f, 0.45f, DASH_SPEED, Now));
            Assert.IsFalse(_presenter.BeginTelegraph(Fan(1, 1f)));
        }

        /****************************************
        *          Projectile (P04)
        ****************************************/

        // 서버가 보낸 속도로 날고, 서버가 알린 위치에서 멈추고, 소멸 연출 뒤 풀로 돌아온다
        [UnityTest]
        public IEnumerator Projectile_FliesAtServerSpeed_EndsAtServerHit_ThenReturnsToPool()
        {
            BossProjectileStart start = Shot(1);
            Assert.IsTrue(_presenter.LaunchProjectile(in start));
            yield return null;
            ProjectileVisual(1, out BossProjectileTrack track, out RectTelegraphVFXController rect);
            Assert.IsTrue(rect.IsShown);
            Assert.AreEqual(1f, rect.Width, 0.0001f);

            _clock += 0.5;
            yield return null;
            Assert.AreEqual(5f, track.GetDistance(_clock), 0.01f);
            Assert.AreEqual(5f - 1.5f, rect.transform.position.z, 0.01f, "머리가 서버 거리에 있다");
            Assert.AreEqual(1.5f, rect.transform.localScale.z, 0.01f);

            Assert.IsTrue(_presenter.EndProjectile(1, EBossProjectileFate.Hit, new Vector3(0f, 0f, 6f)));
            _clock += 1.0;
            yield return null;
            Assert.AreEqual(6f, track.GetDistance(_clock), 0.01f, "서버가 알린 위치에서 멈춘다");

            _clock += rect.DissolveSeconds + 0.1;
            yield return null;
            yield return null;

            AssertAllReturned();
            Assert.IsFalse(rect.IsShown);
        }

        // 벽 앞 경로 길이를 넘어 날지 않고, 서버 알림이 없어도 경로 끝에서 사라진다
        [UnityTest]
        public IEnumerator Projectile_NeverPassesServerPathLength_AndEndsWithoutServerMessage()
        {
            BossProjectileStart start = Shot(1, pathLength: 5f);
            Assert.IsTrue(_presenter.LaunchProjectile(in start));
            _clock += 3.0;
            yield return null;

            ProjectileVisual(1, out BossProjectileTrack track, out RectTelegraphVFXController rect);
            Assert.AreEqual(5f, track.GetDistance(_clock), 0.0001f);
            Assert.AreEqual(5f - 1.5f, rect.transform.position.z, 0.01f);

            _clock += rect.DissolveSeconds + 0.1;
            yield return null;
            yield return null;
            AssertAllReturned();
        }

        // 예고 표현은 발사로 마무리되고, 패턴 종료와 같은 보스의 다음 시전이 투사체를 지우지 않는다
        [UnityTest]
        public IEnumerator Projectile_SurvivesTelegraphFinishAndNextCastOfSameBoss()
        {
            Assert.IsTrue(_presenter.BeginTelegraph(ProjectileTelegraph(1)));
            _clock += 0.9;
            BossProjectileStart start = Shot(1, launch: _clock);
            Assert.IsTrue(_presenter.LaunchProjectile(in start));
            yield return null;
            Visuals(1, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController telegraphRect);
            Assert.AreEqual(EBossTelegraphPhase.Execute, cast.Phase, "발사와 함께 예고는 발동 단계가 된다");
            Assert.AreEqual(1, _presenter.ProjectileCount);

            Assert.IsTrue(_presenter.Finish(1));
            Assert.AreEqual(1, _presenter.ProjectileCount, "패턴 정상 종료는 투사체를 지우지 않는다");

            _clock += 0.6;
            Assert.IsTrue(_presenter.BeginTelegraph(BossTelegraphStart.Line(BOSS_ID, 2, "wolbaek_p06", Vector3.zero, Vector3.right,
                14f, 4f, _clock, 1.6f)));
            yield return null;

            Assert.AreEqual(1, _presenter.ProjectileCount, "같은 보스의 다음 시전이 시작돼도 기존 투사체는 유지된다");
            Assert.IsTrue(_presenter.TryGetProjectile(1, out BossProjectileTrack track, out RectTelegraphVFXController rect));
            Assert.IsFalse(track.IsEnded);
        }

        [UnityTest]
        public IEnumerator Projectile_CancelInFlight_HidesAtOnce_AndLateNotificationsCannotRevive()
        {
            BossProjectileStart start = Shot(1);
            Assert.IsTrue(_presenter.LaunchProjectile(in start));
            yield return null;
            ProjectileVisual(1, out BossProjectileTrack track, out RectTelegraphVFXController rect);

            Assert.IsTrue(_presenter.CancelProjectile(1));

            Assert.IsFalse(rect.IsShown);
            AssertAllReturned();
            Assert.IsFalse(_presenter.EndProjectile(1, EBossProjectileFate.Hit, Vector3.forward * 3f));
            Assert.IsFalse(_presenter.CancelProjectile(1));
            Assert.IsFalse(_presenter.LaunchProjectile(in start), "취소된 번호의 중복 발사는 되살리지 못한다");
            RestoreCancelled(start);
            Assert.AreEqual(0, _presenter.ProjectileCount);
        }

        private void RestoreCancelled(BossProjectileStart start)
        {
            BossProjectileSnapshot snapshot = new BossProjectileSnapshot { Start = start, Fate = EBossProjectileFate.Flying };
            Assert.IsFalse(_presenter.RestoreProjectile(in snapshot));
        }

        // 소멸 알림이 발사보다 먼저 도착해도 나중에 온 발사가 투사체를 만들지 않는다
        [Test]
        public void Projectile_EndArrivingBeforeLaunch_BlocksTheLateLaunch()
        {
            Assert.IsFalse(_presenter.EndProjectile(77, EBossProjectileFate.Hit, Vector3.forward));

            BossProjectileStart start = Shot(77);
            Assert.IsFalse(_presenter.LaunchProjectile(in start));
            AssertAllReturned();
        }

        [Test]
        public void Projectile_DuplicateLaunch_IsIgnored()
        {
            BossProjectileStart start = Shot(1);
            Assert.IsTrue(_presenter.LaunchProjectile(in start));
            Assert.IsFalse(_presenter.LaunchProjectile(in start));

            Assert.AreEqual(1, _presenter.ProjectileCount);
            Assert.AreEqual(1, _rectPool.LeasedCount);
        }

        // 늦게 받은 발사는 처음부터 날리지 않고 서버 시각 기준 위치에서 이어서 그린다
        [UnityTest]
        public IEnumerator Projectile_LateLaunch_ContinuesFromServerTime_AndTooLateIsSkipped()
        {
            BossProjectileStart late = Shot(1, launch: Now - 1.0);
            Assert.IsTrue(_presenter.LaunchProjectile(in late));
            yield return null;
            ProjectileVisual(1, out BossProjectileTrack track, out RectTelegraphVFXController rect);
            Assert.AreEqual(10f, track.GetDistance(Now), 0.01f);
            Assert.AreEqual(10f - 1.5f, rect.transform.position.z, 0.01f);

            BossProjectileStart tooLate = Shot(2, pathLength: 5f, launch: Now - 5.0);
            Assert.IsFalse(_presenter.LaunchProjectile(in tooLate));
            Assert.IsFalse(_presenter.TryGetProjectile(2, out BossProjectileTrack none, out RectTelegraphVFXController noRect));
        }

        [UnityTest]
        public IEnumerator Projectile_Restore_ContinuesFromServerTime_AndEndedSnapshotsAreIgnored()
        {
            BossProjectileSnapshot flying = new BossProjectileSnapshot
            {
                Start = Shot(1, launch: Now - 1.5), Fate = EBossProjectileFate.Flying
            };
            Assert.IsTrue(_presenter.RestoreProjectile(in flying));
            yield return null;
            ProjectileVisual(1, out BossProjectileTrack track, out RectTelegraphVFXController rect);
            Assert.AreEqual(15f, track.GetDistance(Now), 0.01f);
            Assert.IsFalse(_presenter.RestoreProjectile(in flying));

            BossProjectileSnapshot cancelled = new BossProjectileSnapshot { Start = Shot(2), Fate = EBossProjectileFate.Cancelled };
            Assert.IsFalse(_presenter.RestoreProjectile(in cancelled));
            BossProjectileSnapshot ended = new BossProjectileSnapshot
            {
                Start = Shot(3, pathLength: 5f, launch: Now - 9.0), Fate = EBossProjectileFate.Hit, EndDistance = 4f
            };
            Assert.IsFalse(_presenter.RestoreProjectile(in ended));
            Assert.IsTrue(_presenter.TryGetProjectileSnapshot(1, out BossProjectileSnapshot back));
            Assert.AreEqual(1, back.Start.ProjectileId);
            Assert.AreEqual(1, _presenter.ProjectileCount);
        }

        [UnityTest]
        public IEnumerator Projectile_PoolReuse_ManyShotsLeakNothingAndRestartFromOrigin()
        {
            for (int i = 1; i <= 12; ++i)
            {
                BossProjectileStart start = Shot(i, pathLength: 35f, direction: Polar(i * 10f));
                Assert.IsTrue(_presenter.LaunchProjectile(in start));
                yield return null;

                ProjectileVisual(i, out BossProjectileTrack track, out RectTelegraphVFXController rect);
                Assert.IsTrue(rect.IsShown);
                Assert.AreEqual(0f, track.GetDistance(Now), 0.01f);
                Assert.AreEqual(0f, rect.transform.position.magnitude, 0.01f, "재사용한 표현은 발사 위치에서 시작한다");
                Assert.IsTrue(_presenter.CancelProjectile(i));
                AssertAllReturned();
            }
        }

        [UnityTest]
        public IEnumerator Projectile_ClearAllAndDisable_ReleaseEveryProjectileRect()
        {
            BossProjectileStart a = Shot(1, bossId: 1);
            BossProjectileStart b = Shot(2, bossId: 2);
            Assert.IsTrue(_presenter.LaunchProjectile(in a));
            Assert.IsTrue(_presenter.LaunchProjectile(in b));
            Assert.AreEqual(2, _rectPool.LeasedCount);

            _presenter.ClearAll();
            AssertAllReturned();
            Assert.IsFalse(_presenter.LaunchProjectile(in a));

            BossProjectileStart c = Shot(3);
            Assert.IsTrue(_presenter.LaunchProjectile(in c));
            _root.SetActive(false);
            yield return null;
            _root.SetActive(true);
            yield return null;
            AssertAllReturned();
        }

        // 풀이 비어도 예외 없이 모델만 따라가고 표현만 생략한다
        [Test]
        public void Projectile_PoolExhausted_SkipsVisualWithoutErrors()
        {
            for (int i = 1; i <= POOL_CAPACITY + 1; ++i)
            {
                BossProjectileStart start = Shot(i, bossId: i);
                Assert.IsTrue(_presenter.LaunchProjectile(in start));
            }

            Assert.AreEqual(POOL_CAPACITY + 1, _presenter.ProjectileCount);
            Assert.IsTrue(_presenter.TryGetProjectile(POOL_CAPACITY + 1, out BossProjectileTrack track, out RectTelegraphVFXController rect));
            Assert.IsNull(rect);

            _presenter.ClearAll();
            AssertAllReturned();
        }

        /****************************************
        *            Area (P05)
        ****************************************/

        // 서버 활성 확정 없이 예고 종료만으로는 장판을 그리지 않고, 활성이 오면 예고를 마무리하며 장판을 따로 그린다
        [UnityTest]
        public IEnumerator Area_NotShownWithoutServerActivation_ThenShownOnActivation()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(BossTelegraphStart.Circle(BOSS_ID, 1, "wolbaek_p05", new Vector3(4f, 0f, 5f), 3f, Now, 0.2f)));
            yield return new WaitForSeconds(0.4f);

            Assert.AreEqual(0, _presenter.AreaCount, "예고가 끝났어도 서버 확정 전에는 장판이 없다");
            Assert.AreEqual(0, _areaPool.LeasedCount);
            Visuals(1, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect);
            Assert.IsTrue(sector.IsHolding);

            BossAreaStart start = AreaShot(1);
            Assert.IsTrue(_presenter.ActivateArea(in start));
            yield return null;

            Assert.AreEqual(1, _presenter.AreaCount);
            Assert.AreEqual(EBossTelegraphPhase.Execute, cast.Phase);
            Assert.IsFalse(sector.IsHolding);
            AreaVisual(1, out BossAreaTrack track, out AreaGroundVFXController visual);
            Assert.IsTrue(visual.IsShown);
            Assert.AreEqual(3f, visual.Radius, 0.0001f);
            Assert.AreEqual(6f, visual.transform.localScale.x, 0.0001f, "지름만큼의 사각형을 셰이더가 원으로 오린다");
            Assert.AreEqual(new Vector3(4f, visual.transform.position.y, 5f), visual.transform.position);
        }

        [UnityTest]
        public IEnumerator Area_FullLifecycle_TicksPulseThenFadesAtDurationEndAndReturnsToPool()
        {
            BossAreaStart start = AreaShot(1);
            Assert.IsTrue(_presenter.ActivateArea(in start));
            yield return null;
            AreaVisual(1, out BossAreaTrack track, out AreaGroundVFXController visual);

            for (int i = 0; i < 6; ++i)
            {
                _clock = start.ActivateServerTime + i;
                Assert.IsTrue(_presenter.TickArea(1, i, _clock));
                yield return null;
                Assert.AreEqual(i, track.LastTickIndex);
                Assert.IsTrue(visual.IsShown);
            }

            Assert.IsFalse(_presenter.TickArea(1, 5, _clock), "같은 회차는 다시 표시하지 않는다");
            Assert.AreEqual(0.5f, track.GetLifeRatio(start.ActivateServerTime + 3.0), 0.0001f);

            _clock = start.ActivateServerTime + 6.0;
            yield return null;
            Assert.AreEqual(1, _presenter.AreaCount, "지속 시간이 끝나면 옅어지는 동안만 남는다");

            _clock += visual.FadeSeconds + 0.1;
            yield return null;
            yield return null;

            AssertAllReturned();
            Assert.IsFalse(visual.IsShown);
        }

        [UnityTest]
        public IEnumerator Area_ServerExpiredMessage_FadesAndReturns_NoFurtherTicks()
        {
            BossAreaStart start = AreaShot(1);
            Assert.IsTrue(_presenter.ActivateArea(in start));
            yield return null;

            Assert.IsTrue(_presenter.EndArea(1, EBossAreaFate.Expired));
            Assert.IsFalse(_presenter.TickArea(1, 2, _clock));
            yield return null;
            _clock += 1.0; // 소멸을 처음 본 시각부터 옅어지는 시간이 흐른다
            yield return null;
            yield return null;

            AssertAllReturned();
        }

        [UnityTest]
        public IEnumerator Area_Cancel_HidesAtOnce_AndLateNotificationsCannotRevive()
        {
            BossAreaStart start = AreaShot(1);
            Assert.IsTrue(_presenter.ActivateArea(in start));
            yield return null;
            AreaVisual(1, out BossAreaTrack track, out AreaGroundVFXController visual);

            Assert.IsTrue(_presenter.CancelArea(1));

            Assert.IsFalse(visual.IsShown);
            AssertAllReturned();
            Assert.IsFalse(_presenter.TickArea(1, 1, _clock));
            Assert.IsFalse(_presenter.EndArea(1, EBossAreaFate.Expired));
            Assert.IsFalse(_presenter.CancelArea(1));
            Assert.IsFalse(_presenter.ActivateArea(in start), "취소된 번호의 중복 활성은 되살리지 못한다");
            BossAreaSnapshot snapshot = new BossAreaSnapshot { Start = start, Fate = EBossAreaFate.Active, LastTickIndex = -1 };
            Assert.IsFalse(_presenter.RestoreArea(in snapshot));
        }

        // 취소나 소멸이 활성보다 먼저 도착해도 나중에 온 활성이 장판을 만들지 않는다
        [Test]
        public void Area_EndArrivingBeforeActivation_BlocksTheLateActivation()
        {
            Assert.IsFalse(_presenter.EndArea(77, EBossAreaFate.Cancelled));

            BossAreaStart start = AreaShot(77);
            Assert.IsFalse(_presenter.ActivateArea(in start));
            AssertAllReturned();
        }

        [Test]
        public void Area_DuplicateActivation_IsIgnored()
        {
            BossAreaStart start = AreaShot(1);
            Assert.IsTrue(_presenter.ActivateArea(in start));
            Assert.IsFalse(_presenter.ActivateArea(in start));

            Assert.AreEqual(1, _presenter.AreaCount);
            Assert.AreEqual(1, _areaPool.LeasedCount);
        }

        // 패턴의 정상 종료와 같은 보스의 다음 패턴 시작이 장판을 지우지 않고, 장판은 따로 최대 개수만큼 유지된다
        [UnityTest]
        public IEnumerator Area_SurvivesTelegraphFinishAndNextPatternOfSameBoss_ThreeAtOnce()
        {
            for (int i = 1; i <= 3; ++i)
            {
                Assert.IsTrue(_presenter.BeginTelegraph(BossTelegraphStart.Circle(BOSS_ID, i, "wolbaek_p05", new Vector3(i * 4f, 0f, 5f), 3f, Now, 1f)));
                BossAreaStart start = AreaShot(i, new Vector3(i * 4f, 0f, 5f));
                Assert.IsTrue(_presenter.ActivateArea(in start));
                Assert.IsTrue(_presenter.Finish(i));
                _clock += 1.5;
                yield return null;
            }

            Assert.AreEqual(3, _presenter.AreaCount);
            Assert.AreEqual(3, _areaPool.LeasedCount);

            Assert.IsTrue(_presenter.BeginTelegraph(BossTelegraphStart.Line(BOSS_ID, 9, "wolbaek_p06", Vector3.zero, Vector3.right, 14f, 4f, _clock, 1.6f)));
            yield return null;

            Assert.AreEqual(3, _presenter.AreaCount, "다음 패턴의 시작이 기존 장판을 지우지 않는다");
            for (int i = 1; i <= 3; ++i)
            {
                AreaVisual(i, out BossAreaTrack track, out AreaGroundVFXController visual);
                Assert.IsTrue(visual.IsShown);
                Assert.AreEqual(i * 4f, visual.transform.position.x, 0.001f, "장판마다 자기 위치를 유지한다");
            }
        }

        // 늦게 받은 활성은 처음부터 다시 그리지 않고 서버 시각 기준으로 이어서 그리며, 이미 끝난 것은 그리지 않는다
        [Test]
        public void Area_LateActivation_ContinuesFromServerTime_AndTooLateIsSkipped()
        {
            BossAreaStart late = AreaShot(1, activate: Now - 2.5);
            Assert.IsTrue(_presenter.ActivateArea(in late));
            AreaVisual(1, out BossAreaTrack track, out AreaGroundVFXController visual);
            Assert.AreEqual(2.5f, track.GetAge(Now), 0.01f);
            Assert.AreEqual(1f - (2.5f / 6f), track.GetLifeRatio(Now), 0.01f);

            BossAreaStart tooLate = AreaShot(2, activate: Now - 9.0);
            Assert.IsFalse(_presenter.ActivateArea(in tooLate));
            Assert.IsFalse(_presenter.TryGetArea(2, out BossAreaTrack none, out AreaGroundVFXController noVisual));
        }

        [Test]
        public void Area_Restore_ContinuesAndDoesNotReplayShownTicks_EndedSnapshotsIgnored()
        {
            BossAreaSnapshot flying = new BossAreaSnapshot
            {
                Start = AreaShot(1, activate: Now - 3.2), Fate = EBossAreaFate.Active, LastTickIndex = 3, LastTickServerTime = Now - 0.2
            };
            Assert.IsTrue(_presenter.RestoreArea(in flying));
            Assert.IsFalse(_presenter.RestoreArea(in flying));
            Assert.IsFalse(_presenter.TickArea(1, 3, Now));
            Assert.IsTrue(_presenter.TickArea(1, 4, Now));

            BossAreaSnapshot cancelled = new BossAreaSnapshot { Start = AreaShot(2), Fate = EBossAreaFate.Cancelled, LastTickIndex = -1 };
            Assert.IsFalse(_presenter.RestoreArea(in cancelled));
            BossAreaSnapshot ended = new BossAreaSnapshot { Start = AreaShot(3, activate: Now - 9.0), Fate = EBossAreaFate.Expired, LastTickIndex = 5 };
            Assert.IsFalse(_presenter.RestoreArea(in ended));
            Assert.IsTrue(_presenter.TryGetAreaSnapshot(1, out BossAreaSnapshot back));
            Assert.AreEqual(4, back.LastTickIndex);
            Assert.AreEqual(1, _presenter.AreaCount);
        }

        [UnityTest]
        public IEnumerator Area_PoolReuse_ManyAreasLeakNothingAndStartFromTheirOwnCenter()
        {
            for (int i = 1; i <= 12; ++i)
            {
                Vector3 center = new Vector3(i, 0f, -i);
                BossAreaStart start = AreaShot(i, center);
                Assert.IsTrue(_presenter.ActivateArea(in start));
                yield return null;

                AreaVisual(i, out BossAreaTrack track, out AreaGroundVFXController visual);
                Assert.IsTrue(visual.IsShown);
                Assert.AreEqual(center.x, visual.transform.position.x, 0.001f);
                Assert.AreEqual(center.z, visual.transform.position.z, 0.001f);
                Assert.IsTrue(_presenter.CancelArea(i));
                AssertAllReturned();
            }
        }

        [UnityTest]
        public IEnumerator Area_ClearAllAndDisable_ReleaseEveryAreaVisual_ProjectilesAndAreasCoexist()
        {
            BossAreaStart a = AreaShot(1, bossId: 1);
            BossAreaStart b = AreaShot(2, bossId: 1);
            BossProjectileStart shot = Shot(3);
            Assert.IsTrue(_presenter.ActivateArea(in a));
            Assert.IsTrue(_presenter.ActivateArea(in b));
            Assert.IsTrue(_presenter.LaunchProjectile(in shot));
            Assert.AreEqual(2, _areaPool.LeasedCount);
            Assert.AreEqual(1, _rectPool.LeasedCount);

            _presenter.ClearAll();
            AssertAllReturned();
            Assert.IsFalse(_presenter.ActivateArea(in a));

            BossAreaStart c = AreaShot(4);
            Assert.IsTrue(_presenter.ActivateArea(in c));
            _root.SetActive(false);
            yield return null;
            _root.SetActive(true);
            yield return null;
            AssertAllReturned();
        }

        // 풀이 비어도 예외 없이 모델만 따라가고 표현만 생략한다
        [Test]
        public void Area_PoolExhausted_SkipsVisualWithoutErrors()
        {
            for (int i = 1; i <= POOL_CAPACITY + 1; ++i)
            {
                BossAreaStart start = AreaShot(i);
                Assert.IsTrue(_presenter.ActivateArea(in start));
            }

            Assert.AreEqual(POOL_CAPACITY + 1, _presenter.AreaCount);
            Assert.IsTrue(_presenter.TryGetArea(POOL_CAPACITY + 1, out BossAreaTrack track, out AreaGroundVFXController visual));
            Assert.IsNull(visual);

            _presenter.ClearAll();
            AssertAllReturned();
        }
    }
}
