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
        private const int    RECT_POOL_CAPACITY = 8; // 직사각형 풀은 예고와 한 시전의 여러 발이 함께 쓴다
        private const int    AREA_POOL_CAPACITY = 6; // 장판 풀은 보스 전체 상한 3개와 옅어지는 장판이 함께 쓴다
        private const int    SECTOR_POOL_CAPACITY = 8; // 부채꼴 풀은 예고와 한 시전의 구역 4개가 함께 쓴다
        private const int    BEAM_POOL_CAPACITY = 4; // 빔 풀은 한 시전의 평행 빔 둘과 옅어지는 이전 빔이 함께 쓴다
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
        private BeamGroundVFXPool         _beamPool;
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
            _beamPool   = _root.GetComponentInChildren<BeamGroundVFXPool>();

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
            Assert.AreEqual(0, _presenter.BeamCount);
            Assert.AreEqual(0, _presenter.StarCount);
            Assert.AreEqual(0, _beamPool.LeasedCount);
            Assert.AreEqual(BEAM_POOL_CAPACITY, _beamPool.AvailableCount);
            Assert.AreEqual(0, _areaPool.LeasedCount);
            Assert.AreEqual(AREA_POOL_CAPACITY, _areaPool.AvailableCount);
            Assert.AreEqual(0, _sectorPool.LeasedCount);
            Assert.AreEqual(SECTOR_POOL_CAPACITY, _sectorPool.AvailableCount);
            Assert.AreEqual(0, _rectPool.LeasedCount);
            Assert.AreEqual(RECT_POOL_CAPACITY, _rectPool.AvailableCount);
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
            for (int i = 1; i <= SECTOR_POOL_CAPACITY + 1; ++i)
            {
                Assert.IsTrue(_presenter.BeginTelegraph(Fan(i, 1f, bossId: i)));
            }

            Assert.AreEqual(SECTOR_POOL_CAPACITY + 1, _presenter.ActiveCount);
            Visuals(SECTOR_POOL_CAPACITY + 1, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect);
            Assert.IsNull(sector);
            Assert.IsTrue(_presenter.Strike(SECTOR_POOL_CAPACITY + 1, Now));

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
            for (int i = 1; i <= RECT_POOL_CAPACITY + 1; ++i)
            {
                BossProjectileStart start = Shot(i, bossId: i);
                Assert.IsTrue(_presenter.LaunchProjectile(in start));
            }

            Assert.AreEqual(RECT_POOL_CAPACITY + 1, _presenter.ProjectileCount);
            Assert.IsTrue(_presenter.TryGetProjectile(RECT_POOL_CAPACITY + 1, out BossProjectileTrack track, out RectTelegraphVFXController rect));
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
            for (int i = 1; i <= AREA_POOL_CAPACITY + 1; ++i)
            {
                BossAreaStart start = AreaShot(i);
                Assert.IsTrue(_presenter.ActivateArea(in start));
            }

            Assert.AreEqual(AREA_POOL_CAPACITY + 1, _presenter.AreaCount);
            Assert.IsTrue(_presenter.TryGetArea(AREA_POOL_CAPACITY + 1, out BossAreaTrack track, out AreaGroundVFXController visual));
            Assert.IsNull(visual);

            _presenter.ClearAll();
            AssertAllReturned();
        }

        /****************************************
        *      Phase Two P06 Circle (Pressure)
        ****************************************/

        // 2페이즈 P06은 1페이즈 P03과 같은 원형 예고를 쓰고 패턴 ID만 다르다. 표현은 ID로 모양을 가르지 않으므로 같은 규칙을 따라야 한다
        private BossTelegraphStart PhaseTwoCircle(int castId, float seconds = 1.2f, int bossId = BOSS_ID, double? start = null)
        {
            return BossTelegraphStart.Circle(bossId, castId, Shinshi.Data.BossPatternCatalogSO.WOLBAEK_P2_P06_ID, Vector3.zero, 5f,
                start ?? Now, seconds);
        }

        [UnityTest]
        public IEnumerator PhaseTwoCircle_HoldsUntilServerStrike_ThenPlaysAndReturnsToPool()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(PhaseTwoCircle(1, 0.2f)));
            yield return new WaitForSeconds(0.45f);

            Visuals(1, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect);
            Assert.AreEqual(Shinshi.Data.BossPatternCatalogSO.WOLBAEK_P2_P06_ID, cast.PatternId);
            Assert.AreEqual(EBossTelegraphShape.Circle, cast.Shape);
            Assert.IsTrue(sector.IsHolding, "예고가 끝나도 서버 확정 전에는 폭발하지 않는다");
            CollectionAssert.DoesNotContain(_events, "1:Execute");

            Assert.IsTrue(_presenter.Strike(1, Now));
            yield return new WaitForSeconds(0.1f);
            Assert.IsFalse(sector.IsHolding);

            yield return WaitUntil(() => _presenter.ActiveCount == 0);
            yield return null;
            AssertAllReturned();
            CollectionAssert.Contains(_events, "1:Execute");
        }

        [UnityTest]
        public IEnumerator PhaseTwoCircle_NeverConfirmed_ClearsWithoutExploding()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(PhaseTwoCircle(1, 0.1f)));

            yield return WaitUntil(() => _presenter.ActiveCount == 0);
            yield return null;

            AssertAllReturned();
            CollectionAssert.DoesNotContain(_events, "1:Execute");
            CollectionAssert.Contains(_events, "1:Cancelled");
            Assert.IsFalse(_presenter.Strike(1, Now), "끝난 시전은 늦은 확정으로 되살아나지 않는다");
        }

        [Test]
        public void PhaseTwoCircle_Cancel_BlocksLateNotificationsAndDuplicateStart()
        {
            _useRealClock = true;
            BossTelegraphStart start = PhaseTwoCircle(1);
            Assert.IsTrue(_presenter.BeginTelegraph(start));
            Assert.IsFalse(_presenter.BeginTelegraph(start), "중복 시작은 무시한다");
            Assert.AreEqual(1, _sectorPool.LeasedCount);

            Assert.IsTrue(_presenter.Cancel(1));

            AssertAllReturned();
            Assert.IsFalse(_presenter.Strike(1, Now));
            Assert.IsFalse(_presenter.Cancel(1));
            Assert.IsFalse(_presenter.BeginTelegraph(start), "취소된 번호는 되살리지 못한다");
        }

        [UnityTest]
        public IEnumerator PhaseTwoCircle_ReceivedLate_ContinuesFromElapsedServerTime()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(PhaseTwoCircle(1, 1.2f, start: Now - 0.5)));
            yield return null;

            Visuals(1, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect);
            Assert.GreaterOrEqual(sector.Elapsed, 0.5f, "이미 흐른 시간만큼 건너뛰어 시작한다");
        }

        [UnityTest]
        public IEnumerator PhaseTwoCircle_RestoreExecuted_SkipsChargeAndReturnsToPool()
        {
            _useRealClock = true;
            double executeTime = Now - 0.1;
            BossTelegraphSnapshot snapshot = new BossTelegraphSnapshot
            {
                Start             = PhaseTwoCircle(6, 0.2f, start: executeTime - 0.2),
                Direction         = Vector3.forward,
                Length            = 5f,
                IsDirectionLocked = true,
                Phase             = EBossTelegraphPhase.Execute,
                ExecuteServerTime = executeTime
            };

            Assert.IsTrue(_presenter.Restore(in snapshot));
            yield return null;

            Visuals(6, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect);
            Assert.IsFalse(sector.IsHolding);

            yield return WaitUntil(() => _presenter.ActiveCount == 0);
            yield return null;
            AssertAllReturned();
        }

        [UnityTest]
        public IEnumerator PhaseTwoCircle_PoolReuse_AlternatingWithPhaseOneCircleLeaksNothing()
        {
            _useRealClock = true;
            for (int i = 1; i <= 10; ++i)
            {
                BossTelegraphStart start = ((i % 2) == 0) ? PhaseTwoCircle(i, 0.5f) : Circle(i, 0.5f);
                Assert.IsTrue(_presenter.BeginTelegraph(start));
                yield return null;

                Visuals(i, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect);
                Assert.Less(sector.Elapsed, 0.2f, "재사용한 연출은 처음부터 시작한다");
                Assert.IsFalse(sector.IsHolding);
                Assert.IsTrue(_presenter.Cancel(i));
                AssertAllReturned();
            }
        }

        // 샘플 입력의 2페이즈 P06 재생, 시작과 서버 확정 발동과 회복 뒤 정리까지 시각표대로 흐르는지 본다
        [UnityTest]
        public IEnumerator SampleInput_PhaseTwoCircle_PlaysStrikesAndReturnsToPool()
        {
            BossTelegraphSampleInput sample = PrepareSampleWithoutLoop();
            _useRealClock = true;
            sample.enabled = true;
            yield return null;

            sample.PlayPhaseTwoCircle();
            int castId = sample.LastCastId;
            Assert.IsTrue(_presenter.TryGetVisuals(castId, out BossTelegraphCast cast, out SectorVFXController sector, out _));
            Assert.AreEqual(Shinshi.Data.BossPatternCatalogSO.WOLBAEK_P2_P06_ID, cast.PatternId);

            yield return WaitUntilLong(() => _presenter.ActiveCount == 0);
            yield return null;
            AssertAllReturned();
            CollectionAssert.Contains(_events, castId + ":Execute");
        }

        [UnityTest]
        public IEnumerator SampleInput_PhaseTwoCircleCancelled_NeverExplodes()
        {
            BossTelegraphSampleInput sample = PrepareSampleWithoutLoop();
            _useRealClock = true;
            sample.enabled = true;
            yield return null;

            sample.PlayPhaseTwoCircleCancelled();
            int castId = sample.LastCastId;

            yield return WaitUntilLong(() => _presenter.ActiveCount == 0);
            yield return null;
            AssertAllReturned();
            CollectionAssert.DoesNotContain(_events, castId + ":Execute");
            CollectionAssert.Contains(_events, castId + ":Cancelled");
        }

        // 자동 반복을 끄지 않으면 켜는 순간 첫 단계가 시작돼 시전 번호가 섞인다
        private BossTelegraphSampleInput PrepareSampleWithoutLoop()
        {
            BossTelegraphSampleInput sample = _root.GetComponent<BossTelegraphSampleInput>();
            System.Reflection.FieldInfo loop = typeof(BossTelegraphSampleInput).GetField("_autoLoop",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            loop.SetValue(sample, false);
            return sample;
        }

        private IEnumerator WaitUntilLong(System.Func<bool> condition)
        {
            float until = Time.realtimeSinceStartup + (WAIT_LIMIT * 2f);
            while ((!condition()) && (Time.realtimeSinceStartup < until))
            {
                yield return null;
            }

            Assert.IsTrue(condition(), "시간 안에 조건이 만족되지 않았다");
        }

        /****************************************
        *   Phase Two P05 Volley (Basic, 3 shots)
        ****************************************/

        private const int VOLLEY_CAST = 50;

        // 한 시전이 쏘는 독립된 세 발, 시전 번호는 같고 투사체 번호(동작 번호)가 서로 다르다
        private BossProjectileStart VolleyShot(int projectileId, double? launch = null, int castId = VOLLEY_CAST, int bossId = BOSS_ID)
        {
            return new BossProjectileStart(bossId, projectileId, castId, Vector3.zero, Vector3.forward, 10f, 1f, 35f, launch ?? Now);
        }

        private BossTelegraphStart VolleyTelegraph(int castId = VOLLEY_CAST, int bossId = BOSS_ID)
        {
            return BossTelegraphStart.Projectile(bossId, castId, Shinshi.Data.BossPatternCatalogSO.WOLBAEK_P2_P05_ID, Vector3.zero,
                Vector3.forward, 35f, 1f, Now, 0.9f);
        }

        private void LaunchThree(double baseLaunch)
        {
            for (int i = 0; i < 3; ++i)
            {
                BossProjectileStart shot = VolleyShot(101 + i, baseLaunch + (i * 0.3));
                Assert.IsTrue(_presenter.LaunchProjectile(in shot), "발사 알림 " + (101 + i));
            }
        }

        [Test]
        public void Volley_ThreeShotsOfOneCast_FlyTogetherAndFirstLaunchFinishesTheTelegraph()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(VolleyTelegraph()));
            Assert.AreEqual(1, _presenter.ActiveCount);

            LaunchThree(Now);

            Assert.AreEqual(3, _presenter.ProjectileCount, "알림마다 한 발이고 클라이언트가 스스로 늘리지 않는다");
            for (int id = 101; id <= 103; ++id)
            {
                Assert.IsTrue(_presenter.TryGetProjectile(id, out BossProjectileTrack track, out RectTelegraphVFXController rect));
                Assert.AreEqual(VOLLEY_CAST, track.Start.CastId);
                Assert.IsNotNull(rect);
            }

            CollectionAssert.Contains(_events, VOLLEY_CAST + ":Execute", "첫 발사가 예고를 마무리한다");
        }

        [Test]
        public void Volley_OnlyAnnouncedShotsExist_NothingIsInventedWithoutServerLaunch()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(VolleyTelegraph()));
            BossProjectileStart first = VolleyShot(101);
            Assert.IsTrue(_presenter.LaunchProjectile(in first));

            Assert.AreEqual(1, _presenter.ProjectileCount);
            Assert.IsFalse(_presenter.TryGetProjectile(102, out BossProjectileTrack ignored, out _));
        }

        [Test]
        public void Volley_OneShotEnds_OthersKeepFlyingAndEndedOneStaysEnded()
        {
            _useRealClock = true;
            LaunchThree(Now);

            Assert.IsTrue(_presenter.EndProjectile(101, EBossProjectileFate.Hit, new Vector3(0f, 0f, 4f)));

            Assert.IsTrue(_presenter.TryGetProjectile(101, out BossProjectileTrack hit, out _));
            Assert.AreEqual(EBossProjectileFate.Hit, hit.Fate);
            Assert.IsTrue(_presenter.TryGetProjectile(102, out BossProjectileTrack second, out _));
            Assert.AreEqual(EBossProjectileFate.Flying, second.Fate);
            Assert.IsTrue(_presenter.TryGetProjectile(103, out BossProjectileTrack third, out _));
            Assert.AreEqual(EBossProjectileFate.Flying, third.Fate);
            Assert.IsFalse(_presenter.EndProjectile(101, EBossProjectileFate.Wall, Vector3.zero), "끝난 발은 다시 끝나지 않는다");
        }

        [Test]
        public void Volley_CancelOneShot_RemovesOnlyThatShot()
        {
            _useRealClock = true;
            LaunchThree(Now);

            Assert.IsTrue(_presenter.CancelProjectile(102));

            Assert.AreEqual(2, _presenter.ProjectileCount);
            Assert.IsFalse(_presenter.TryGetProjectile(102, out BossProjectileTrack gone, out _));
            Assert.IsTrue(_presenter.TryGetProjectile(101, out BossProjectileTrack kept, out _));
            Assert.IsTrue(_presenter.TryGetProjectile(103, out BossProjectileTrack kept2, out _));
        }

        [Test]
        public void Volley_CancelAllAndClear_ReturnEveryRectToThePool()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(VolleyTelegraph()));
            LaunchThree(Now);

            Assert.IsTrue(_presenter.CancelProjectile(101));
            Assert.IsTrue(_presenter.CancelProjectile(102));
            Assert.IsTrue(_presenter.CancelProjectile(103));
            _presenter.ClearAll();

            AssertAllReturned();
        }

        [Test]
        public void Volley_FinishAndNextCast_DoNotRemoveFlyingProjectiles()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(VolleyTelegraph()));
            LaunchThree(Now);

            Assert.IsTrue(_presenter.Finish(VOLLEY_CAST));
            Assert.IsTrue(_presenter.BeginTelegraph(Line(70)));

            Assert.AreEqual(3, _presenter.ProjectileCount);
            Assert.IsTrue(_presenter.TryGetProjectile(101, out BossProjectileTrack track, out RectTelegraphVFXController rect));
            Assert.IsNotNull(rect);
        }

        [Test]
        public void Volley_DuplicateLaunchOfTheSameShot_IsIgnoredWhileTheOthersStart()
        {
            _useRealClock = true;
            BossProjectileStart first = VolleyShot(101);
            BossProjectileStart second = VolleyShot(102);
            Assert.IsTrue(_presenter.LaunchProjectile(in first));
            Assert.IsFalse(_presenter.LaunchProjectile(in first));
            Assert.IsTrue(_presenter.LaunchProjectile(in second));

            Assert.AreEqual(2, _presenter.ProjectileCount);
        }

        [Test]
        public void Volley_EndArrivingBeforeLaunch_BlocksOnlyThatShot()
        {
            _useRealClock = true;
            Assert.IsFalse(_presenter.EndProjectile(102, EBossProjectileFate.Hit, Vector3.zero));

            BossProjectileStart blocked = VolleyShot(102);
            BossProjectileStart other = VolleyShot(103);
            Assert.IsFalse(_presenter.LaunchProjectile(in blocked), "먼저 온 소멸이 늦은 발사를 막는다");
            Assert.IsTrue(_presenter.LaunchProjectile(in other));
            Assert.AreEqual(1, _presenter.ProjectileCount);
        }

        [Test]
        public void Volley_LateLaunch_ContinuesFromTheServerLaunchTime()
        {
            _useRealClock = true;
            BossProjectileStart late = VolleyShot(102, Now - 0.5);
            Assert.IsTrue(_presenter.LaunchProjectile(in late));

            Assert.IsTrue(_presenter.TryGetProjectile(102, out BossProjectileTrack track, out _));
            Assert.AreEqual(5f, track.GetDistance(Now), 0.2f, "0.5초 늦게 받아도 속도 10m/초로 이미 5m 갔다");
        }

        [UnityTest]
        public IEnumerator Volley_RestoreThreeShots_EachContinuesFromItsOwnLaunchTime()
        {
            _useRealClock = true;
            for (int i = 0; i < 3; ++i)
            {
                BossProjectileSnapshot snapshot = new BossProjectileSnapshot
                {
                    Start = VolleyShot(101 + i, Now - 0.6 - ((2 - i) * 0.3)),
                    Fate = EBossProjectileFate.Flying
                };
                Assert.IsTrue(_presenter.RestoreProjectile(in snapshot));
            }

            yield return null;

            Assert.AreEqual(3, _presenter.ProjectileCount);
            double now = Now;
            float d1 = ProjectileDistance(101, now);
            float d2 = ProjectileDistance(102, now);
            float d3 = ProjectileDistance(103, now);
            Assert.Greater(d3, 5.5f);
            Assert.AreEqual(3f, d2 - d3, 0.3f, "먼저 쏜 발일수록 멀리 갔고 발 사이 0.3초는 3m 차이다");
            Assert.AreEqual(3f, d1 - d2, 0.3f);

            Assert.IsTrue(_presenter.TryGetProjectileSnapshot(102, out BossProjectileSnapshot back));
            Assert.AreEqual(VOLLEY_CAST, back.Start.CastId);
        }

        private float ProjectileDistance(int id, double now)
        {
            Assert.IsTrue(_presenter.TryGetProjectile(id, out BossProjectileTrack track, out _));
            return track.GetDistance(now);
        }

        [UnityTest]
        public IEnumerator Volley_PoolReuse_ManyVolleysLeakNothingAndRestartFromOrigin()
        {
            _useRealClock = true;
            for (int round = 0; round < 6; ++round)
            {
                int cast = 200 + (round * 10);
                Assert.IsTrue(_presenter.BeginTelegraph(VolleyTelegraph(cast)));
                for (int i = 0; i < 3; ++i)
                {
                    BossProjectileStart shot = VolleyShot(cast + 1 + i, Now, cast);
                    Assert.IsTrue(_presenter.LaunchProjectile(in shot));
                }

                yield return null;
                Assert.AreEqual(3, _presenter.ProjectileCount);
                for (int i = 0; i < 3; ++i)
                {
                    Assert.IsTrue(_presenter.TryGetProjectile(cast + 1 + i, out BossProjectileTrack track, out RectTelegraphVFXController rect));
                    Assert.Less(track.GetDistance(Now), 2f, "재사용한 표현은 발사 위치에서 다시 시작한다");
                    Assert.IsNotNull(rect);
                    Assert.IsTrue(_presenter.CancelProjectile(cast + 1 + i));
                }

                _presenter.ClearAll();
                AssertAllReturned();
            }
        }

        // 샘플 입력의 2페이즈 P05 재생, 예고 하나와 세 발의 시작, 개별 소멸, 중간 취소, 회복 뒤 정리까지 시각표대로 흐른다
        [UnityTest]
        public IEnumerator SampleInput_Volley_ThreeShotsFlyIndependentlyAndReturnToPool()
        {
            BossTelegraphSampleInput sample = PrepareSampleWithoutLoop();
            _useRealClock = true;
            sample.enabled = true;
            yield return null;

            sample.PlayPhaseTwoVolley();
            int castId = sample.LastCastId - 3;
            yield return WaitUntilLong(() => _presenter.ProjectileCount == 3);

            yield return WaitUntilLong(() => (_presenter.ProjectileCount == 0) && (_presenter.ActiveCount == 0));
            yield return null;
            AssertAllReturned();
            CollectionAssert.Contains(_events, castId + ":Execute");
        }

        [UnityTest]
        public IEnumerator SampleInput_VolleyOneHit_FirstEndsOthersContinue()
        {
            BossTelegraphSampleInput sample = PrepareSampleWithoutLoop();
            _useRealClock = true;
            sample.enabled = true;
            yield return null;

            sample.PlayPhaseTwoVolleyOneHit();
            yield return WaitUntilLong(() => _presenter.ProjectileCount == 3);
            int last = sample.LastCastId;
            int firstShot = last - 2;

            // 셋째 발이 나갈 때 첫 발은 이미 소멸했고(맞은 뒤) 나머지 둘은 계속 난다
            Assert.IsTrue(_presenter.TryGetProjectile(last, out BossProjectileTrack third, out _));
            Assert.AreEqual(EBossProjectileFate.Flying, third.Fate, "한 발의 소멸이 나머지를 지우지 않는다");
            Assert.IsTrue(_presenter.TryGetProjectile(last - 1, out BossProjectileTrack second, out _));
            Assert.AreEqual(EBossProjectileFate.Flying, second.Fate);
            if (_presenter.TryGetProjectile(firstShot, out BossProjectileTrack first, out _))
            {
                Assert.AreNotEqual(EBossProjectileFate.Flying, first.Fate);
            }

            yield return WaitUntilLong(() => (_presenter.ProjectileCount == 0) && (_presenter.ActiveCount == 0));
            yield return null;
            AssertAllReturned();
        }

        [UnityTest]
        public IEnumerator SampleInput_VolleyCancelled_RemovesFlyingShotsAndNeverLaunchesTheThird()
        {
            BossTelegraphSampleInput sample = PrepareSampleWithoutLoop();
            _useRealClock = true;
            sample.enabled = true;
            yield return null;

            sample.PlayPhaseTwoVolleyCancelled();
            int last = sample.LastCastId;

            yield return WaitUntilLong(() => _presenter.TryGetProjectile(last, out BossProjectileTrack second, out _));
            yield return WaitUntilLong(() => (_presenter.ProjectileCount == 0) && (_presenter.ActiveCount == 0));
            yield return null;

            AssertAllReturned();
            Assert.IsFalse(_presenter.TryGetProjectile(last + 1, out BossProjectileTrack none, out _), "셋째 발은 나가지 않았다");
        }

        [UnityTest]
        public IEnumerator SampleInput_VolleyLateLaunchAndRestore_PlayWithoutRestartingFromTheOrigin()
        {
            BossTelegraphSampleInput sample = PrepareSampleWithoutLoop();
            _useRealClock = true;
            sample.enabled = true;
            yield return null;

            sample.PlayPhaseTwoVolleyRestore();
            yield return null;
            Assert.AreEqual(3, _presenter.ProjectileCount);
            int last = sample.LastCastId;
            Assert.Greater(ProjectileDistance(last, Now), 5f, "복원은 발사 시각 기준 위치에서 이어진다");

            yield return WaitUntilLong(() => _presenter.ProjectileCount == 0);
            yield return null;
            AssertAllReturned();

            sample.PlayPhaseTwoVolleyLateLaunch();
            yield return WaitUntilLong(() => _presenter.ProjectileCount == 3);
            yield return WaitUntilLong(() => (_presenter.ProjectileCount == 0) && (_presenter.ActiveCount == 0));
            yield return null;
            AssertAllReturned();
        }

        /****************************************
        *   Phase Two P04 Areas (Pressure, 2 areas)
        ****************************************/

        private const int AREA_CAST = 300;

        // 한 시전의 두 장판, 시전 번호는 같고 장판 번호가 서로 다르다. 예고는 장판 번호를 키로 시전 번호 묶음에 속한다
        private BossTelegraphStart AreaTelegraph(int areaId, Vector3 center, int castId = AREA_CAST, int bossId = BOSS_ID,
            double? start = null)
        {
            return BossTelegraphStart.Circle(bossId, areaId, Shinshi.Data.BossPatternCatalogSO.WOLBAEK_P2_P04_ID, center, 3f,
                start ?? Now, 1f, castId);
        }

        private BossAreaStart PairArea(int areaId, Vector3 center, double? activate = null, int castId = AREA_CAST, int bossId = BOSS_ID)
        {
            return new BossAreaStart(bossId, areaId, castId, center, 3f, 6f, 1f, activate ?? Now);
        }

        private static readonly Vector3 LeftCenter  = new Vector3(-4f, 0f, 5f);
        private static readonly Vector3 RightCenter = new Vector3(4f, 0f, 5f);

        [Test]
        public void PairAreas_TwoTelegraphsOfOneCast_AreShownTogether()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(AreaTelegraph(301, LeftCenter)));
            Assert.IsTrue(_presenter.BeginTelegraph(AreaTelegraph(302, RightCenter)), "같은 시전의 둘째 예고가 첫째를 지우지 않는다");

            Assert.AreEqual(2, _presenter.ActiveCount);
            Assert.AreEqual(2, _sectorPool.LeasedCount);
            Assert.IsTrue(_presenter.TryGetVisuals(301, out BossTelegraphCast first, out SectorVFXController sector1, out _));
            Assert.IsTrue(_presenter.TryGetVisuals(302, out BossTelegraphCast second, out SectorVFXController sector2, out _));
            Assert.AreEqual(first.GroupId, second.GroupId);
            Assert.IsNotNull(sector1);
            Assert.IsNotNull(sector2);
        }

        [Test]
        public void PairAreas_DifferentCastOfTheSameBoss_StillRetiresThePreviousTelegraphs()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(AreaTelegraph(301, LeftCenter)));
            Assert.IsTrue(_presenter.BeginTelegraph(AreaTelegraph(302, RightCenter)));

            Assert.IsTrue(_presenter.BeginTelegraph(Line(70)));

            Assert.AreEqual(1, _presenter.ActiveCount, "다른 시전의 새 예고는 이전 예고를 끝낸다(기존 규칙)");
        }

        [Test]
        public void PairAreas_FirstActivationKeepsTheSecondTelegraphAndCreatesOnlyItsOwnArea()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(AreaTelegraph(301, LeftCenter)));
            Assert.IsTrue(_presenter.BeginTelegraph(AreaTelegraph(302, RightCenter)));

            BossAreaStart first = PairArea(301, LeftCenter);
            Assert.IsTrue(_presenter.ActivateArea(in first));

            Assert.AreEqual(1, _presenter.AreaCount, "활성 알림마다 장판 한 개");
            Assert.IsTrue(_presenter.TryGetVisuals(302, out BossTelegraphCast second, out _, out _), "둘째 예고는 그대로 남는다");
            Assert.IsFalse(second.HasExecuted);
            CollectionAssert.Contains(_events, "301:Execute");
            CollectionAssert.DoesNotContain(_events, "302:Execute");
        }

        [UnityTest]
        public IEnumerator PairAreas_TelegraphEndAlone_NeverCreatesAnAreaWithoutServerActivation()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(BossTelegraphStart.Circle(BOSS_ID, 301, "wolbaek_ph2_p04", LeftCenter, 3f, Now, 0.15f, AREA_CAST)));

            yield return WaitUntil(() => _presenter.ActiveCount == 0);
            yield return null;

            Assert.AreEqual(0, _presenter.AreaCount);
            AssertAllReturned();
        }

        [Test]
        public void PairAreas_TickEndAndCancel_AffectOnlyThatAreaId()
        {
            _useRealClock = true;
            BossAreaStart first = PairArea(301, LeftCenter);
            BossAreaStart second = PairArea(302, RightCenter);
            Assert.IsTrue(_presenter.ActivateArea(in first));
            Assert.IsTrue(_presenter.ActivateArea(in second));

            Assert.IsTrue(_presenter.TickArea(301, 0, Now));
            Assert.IsTrue(_presenter.TryGetArea(301, out BossAreaTrack t1, out _));
            Assert.IsTrue(_presenter.TryGetArea(302, out BossAreaTrack t2, out _));
            Assert.AreEqual(0, t1.LastTickIndex);
            Assert.AreEqual(-1, t2.LastTickIndex, "한 장판의 타격 표시가 다른 장판에 번지지 않는다");

            Assert.IsTrue(_presenter.CancelArea(301));
            Assert.AreEqual(1, _presenter.AreaCount);
            Assert.IsTrue(_presenter.TryGetArea(302, out BossAreaTrack kept, out _));
            Assert.AreEqual(EBossAreaFate.Active, kept.Fate);
            Assert.IsFalse(_presenter.EndArea(301, EBossAreaFate.Expired), "끝난 장판은 다시 끝나지 않는다");
        }

        [Test]
        public void PairAreas_FinishAndNextCast_DoNotRemoveActiveAreas()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(AreaTelegraph(301, LeftCenter)));
            Assert.IsTrue(_presenter.BeginTelegraph(AreaTelegraph(302, RightCenter)));
            BossAreaStart first = PairArea(301, LeftCenter);
            BossAreaStart second = PairArea(302, RightCenter);
            Assert.IsTrue(_presenter.ActivateArea(in first));
            Assert.IsTrue(_presenter.ActivateArea(in second));

            Assert.IsTrue(_presenter.Finish(301));
            Assert.IsTrue(_presenter.Finish(302));
            Assert.IsTrue(_presenter.BeginTelegraph(Line(70)));

            Assert.AreEqual(2, _presenter.AreaCount, "다음 시전이 시작돼도 활성 장판은 남는다");
        }

        [Test]
        public void PairAreas_DuplicateActivationIgnored_AndCancelBeforeActivationBlocksOnlyThatArea()
        {
            _useRealClock = true;
            BossAreaStart first = PairArea(301, LeftCenter);
            BossAreaStart second = PairArea(302, RightCenter);
            Assert.IsFalse(_presenter.CancelArea(302), "활성 전 취소는 장판이 없어 아무것도 지우지 않는다");

            Assert.IsTrue(_presenter.ActivateArea(in first));
            Assert.IsFalse(_presenter.ActivateArea(in first), "중복 활성 알림은 무시한다");
            Assert.IsFalse(_presenter.ActivateArea(in second), "먼저 온 취소가 그 장판의 늦은 활성을 막는다");
            Assert.AreEqual(1, _presenter.AreaCount);

            BossAreaStart third = PairArea(303, RightCenter);
            Assert.IsTrue(_presenter.ActivateArea(in third), "다른 장판은 영향이 없다");
            Assert.AreEqual(2, _presenter.AreaCount);
        }

        [Test]
        public void PairAreas_LateActivation_ContinuesFromTheServerActivationTime()
        {
            _useRealClock = true;
            BossAreaStart late = PairArea(302, RightCenter, Now - 2.5);
            Assert.IsTrue(_presenter.ActivateArea(in late));

            Assert.IsTrue(_presenter.TryGetArea(302, out BossAreaTrack track, out AreaGroundVFXController visual));
            Assert.AreEqual(2.5f, track.GetAge(Now), 0.2f);
            Assert.Less(track.GetLifeRatio(Now), 0.7f, "이미 흐른 시간만큼 수명이 줄어 있다");
            Assert.IsNotNull(visual);
        }

        [Test]
        public void PairAreas_RestoreTwoSnapshots_KeepsEachAreasShownRoundsAndCastId()
        {
            _useRealClock = true;
            for (int i = 0; i < 2; ++i)
            {
                double activate = Now - 3.2 - (i * 0.4);
                BossAreaSnapshot snapshot = new BossAreaSnapshot
                {
                    Start = PairArea(301 + i, (i == 0) ? LeftCenter : RightCenter, activate),
                    Fate = EBossAreaFate.Active,
                    LastTickIndex = 3,
                    LastTickServerTime = activate + 3.0
                };
                Assert.IsTrue(_presenter.RestoreArea(in snapshot));
            }

            Assert.AreEqual(2, _presenter.AreaCount);
            Assert.IsFalse(_presenter.TickArea(301, 3, Now), "복원 전에 표시한 회차는 다시 표시하지 않는다");
            Assert.IsTrue(_presenter.TickArea(301, 4, Now));
            Assert.IsTrue(_presenter.TryGetAreaSnapshot(302, out BossAreaSnapshot back));
            Assert.AreEqual(AREA_CAST, back.Start.CastId);
            Assert.AreEqual(3, back.LastTickIndex);
        }

        [Test]
        public void PairAreas_ThreeAreasAndTwoTelegraphs_AllGetVisualsWithoutExhaustingPools()
        {
            _useRealClock = true;
            for (int i = 0; i < 3; ++i)
            {
                BossAreaStart area = PairArea(401 + i, LeftCenter + new Vector3(i * 7f, 0f, 0f), castId: 400 + i);
                Assert.IsTrue(_presenter.ActivateArea(in area));
            }

            Assert.IsTrue(_presenter.BeginTelegraph(AreaTelegraph(501, LeftCenter, castId: 500)));
            Assert.IsTrue(_presenter.BeginTelegraph(AreaTelegraph(502, RightCenter, castId: 500)));

            Assert.AreEqual(3, _presenter.AreaCount);
            for (int i = 0; i < 3; ++i)
            {
                Assert.IsTrue(_presenter.TryGetArea(401 + i, out BossAreaTrack track, out AreaGroundVFXController visual));
                Assert.IsNotNull(visual, "보스 전체 상한 3개 모두 표현이 있다");
            }

            Assert.AreEqual(2, _sectorPool.LeasedCount);
            _presenter.ClearAll();
            AssertAllReturned();
        }

        [UnityTest]
        public IEnumerator PairAreas_PoolReuse_ManyPairsLeakNothing()
        {
            _useRealClock = true;
            for (int round = 0; round < 6; ++round)
            {
                int cast = 600 + (round * 10);
                Assert.IsTrue(_presenter.BeginTelegraph(AreaTelegraph(cast + 1, LeftCenter, cast)));
                Assert.IsTrue(_presenter.BeginTelegraph(AreaTelegraph(cast + 2, RightCenter, cast)));
                BossAreaStart a = PairArea(cast + 1, LeftCenter, Now, cast);
                BossAreaStart b = PairArea(cast + 2, RightCenter, Now, cast);
                Assert.IsTrue(_presenter.ActivateArea(in a));
                Assert.IsTrue(_presenter.ActivateArea(in b));
                yield return null;

                Assert.AreEqual(2, _presenter.AreaCount);
                Assert.IsTrue(_presenter.CancelArea(cast + 1));
                Assert.IsTrue(_presenter.CancelArea(cast + 2));
                _presenter.ClearAll();
                AssertAllReturned();
            }
        }

        // 샘플 입력의 2페이즈 P04 재생, 예고 둘과 서버가 확정한 장판 둘의 시작, 개별 종료, 전체 취소, 늦은 활성, 복원을 시각표대로 흘린다
        [UnityTest]
        public IEnumerator SampleInput_PhaseTwoAreas_TwoTelegraphsThenTwoIndependentAreasAndReturnToPool()
        {
            BossTelegraphSampleInput sample = PrepareSampleWithoutLoop();
            _useRealClock = true;
            sample.enabled = true;
            yield return null;

            sample.PlayPhaseTwoAreas();
            Assert.AreEqual(2, _presenter.ActiveCount, "예고 둘이 동시에 있다");
            Assert.AreEqual(0, _presenter.AreaCount);

            yield return WaitUntilLong(() => _presenter.AreaCount == 2);
            yield return WaitUntilVeryLong(() => (_presenter.AreaCount == 0) && (_presenter.ActiveCount == 0));
            yield return null;
            AssertAllReturned();
        }

        [UnityTest]
        public IEnumerator SampleInput_PhaseTwoOneAreaEnds_OtherAreaKeepsGoing()
        {
            BossTelegraphSampleInput sample = PrepareSampleWithoutLoop();
            _useRealClock = true;
            sample.enabled = true;
            yield return null;

            sample.PlayPhaseTwoAreaOneEnds();
            int second = sample.LastCastId;
            int first = second - 1;

            yield return WaitUntilLong(() => _presenter.AreaCount == 2);
            yield return WaitUntilLong(() => !_presenter.TryGetArea(first, out BossAreaTrack t, out _));
            Assert.IsTrue(_presenter.TryGetArea(second, out BossAreaTrack kept, out _), "한 장판이 끝나도 다른 장판은 남는다");
            Assert.AreEqual(EBossAreaFate.Active, kept.Fate);

            yield return WaitUntilVeryLong(() => (_presenter.AreaCount == 0) && (_presenter.ActiveCount == 0));
            yield return null;
            AssertAllReturned();
        }

        [UnityTest]
        public IEnumerator SampleInput_PhaseTwoAreasCancelled_BothRemovedAtOnce()
        {
            BossTelegraphSampleInput sample = PrepareSampleWithoutLoop();
            _useRealClock = true;
            sample.enabled = true;
            yield return null;

            sample.PlayPhaseTwoAreasCancelled();

            yield return WaitUntilLong(() => _presenter.AreaCount == 2);
            yield return WaitUntilLong(() => _presenter.AreaCount == 0);
            yield return WaitUntilLong(() => _presenter.ActiveCount == 0);
            yield return null;
            AssertAllReturned();
        }

        [UnityTest]
        public IEnumerator SampleInput_PhaseTwoSingleAndOverlapAndLateAndRestore_PlayAndReturnToPool()
        {
            BossTelegraphSampleInput sample = PrepareSampleWithoutLoop();
            _useRealClock = true;
            sample.enabled = true;
            yield return null;

            sample.PlayPhaseTwoAreaSingle();
            yield return WaitUntilLong(() => _presenter.AreaCount == 1);
            _presenter.ClearAll();
            AssertAllReturned();

            sample.PlayPhaseTwoAreasOverlap();
            yield return WaitUntilLong(() => _presenter.AreaCount == 2);
            _presenter.ClearAll();
            AssertAllReturned();

            sample.PlayPhaseTwoAreasRestore();
            yield return null;
            Assert.AreEqual(2, _presenter.AreaCount, "복원은 이미 지난 시간만큼 이어서 그린다");
            _presenter.ClearAll();
            AssertAllReturned();

            sample.PlayPhaseTwoAreasLateActivation();
            yield return WaitUntilLong(() => _presenter.AreaCount == 1);
            yield return WaitUntilLong(() => _presenter.AreaCount == 2);
            _presenter.ClearAll();
            AssertAllReturned();
        }

        private IEnumerator WaitUntilVeryLong(System.Func<bool> condition)
        {
            float until = Time.realtimeSinceStartup + (WAIT_LIMIT * 4f);
            while ((!condition()) && (Time.realtimeSinceStartup < until))
            {
                yield return null;
            }

            Assert.IsTrue(condition(), "시간 안에 조건이 만족되지 않았다");
        }

        /****************************************
        *   Phase Two P03 Sweep (Large, 4 zones)
        ****************************************/

        private const int SWEEP_CAST = 800;

        // 한 시전의 구역 부채꼴 예고, 구역 번호가 표현의 키이고 시전 번호가 묶음이다. 구역 i의 예고는 그 구역 폭발 시각까지다
        private BossTelegraphStart SweepZone(int zoneIndex, bool isRight = true, double? start = null, int castId = SWEEP_CAST,
            int bossId = BOSS_ID, float baseSeconds = 1.2f)
        {
            float side = isRight ? 1f : -1f;
            Vector3 direction = Quaternion.AngleAxis(side * 45f * (zoneIndex + 0.5f), Vector3.up) * Vector3.forward;
            return BossTelegraphStart.Fan(bossId, castId + 1 + zoneIndex, Shinshi.Data.BossPatternCatalogSO.WOLBAEK_P2_P03_ID,
                Vector3.zero, direction, 6f, 45f, start ?? Now, baseSeconds + (zoneIndex * 0.25f), castId);
        }

        private void BeginSweepZones(bool isRight = true, double? start = null, int castId = SWEEP_CAST, float baseSeconds = 1.2f)
        {
            for (int i = 0; i < 4; ++i)
            {
                Assert.IsTrue(_presenter.BeginTelegraph(SweepZone(i, isRight, start, castId, baseSeconds: baseSeconds)), "구역 " + i);
            }
        }

        private BossSweepCueView Cue()
        {
            BossSweepCueView cue = _root.GetComponentInChildren<BossSweepCueView>(true);
            Assert.IsNotNull(cue);
            return cue;
        }

        [Test]
        public void Sweep_FourZoneTelegraphsOfOneCast_AreShownTogetherAsFortyFiveDegreeFans()
        {
            _useRealClock = true;
            BeginSweepZones();

            Assert.AreEqual(4, _presenter.ActiveCount, "한 시전의 구역 예고가 서로 지우지 않는다");
            Assert.AreEqual(4, _sectorPool.LeasedCount);
            for (int i = 0; i < 4; ++i)
            {
                Visuals(SWEEP_CAST + 1 + i, out BossTelegraphCast cast, out SectorVFXController sector, out _);
                Assert.AreEqual(EBossTelegraphShape.Fan, cast.Shape);
                Assert.AreEqual(45f, cast.Start.ArcAngle, 0.001f);
                Assert.AreEqual(6f, cast.Length, 0.001f);
                Assert.AreEqual(SWEEP_CAST, cast.GroupId);
                Assert.IsNotNull(sector);
            }
        }

        [Test]
        public void Sweep_ZoneDirectionsFollowTheServerChosenSideInOrder()
        {
            foreach (bool isRight in new[] { true, false })
            {
                _presenter.ClearAll();
                _useRealClock = true;
                int cast = isRight ? 810 : 820;
                BeginSweepZones(isRight, null, cast);

                float sign = isRight ? 1f : -1f;
                for (int i = 0; i < 4; ++i)
                {
                    Visuals(cast + 1 + i, out BossTelegraphCast zone, out _, out _);
                    Assert.AreEqual(sign * 45f * (i + 0.5f), Vector3.SignedAngle(Vector3.forward, zone.Direction, Vector3.up), 0.1f,
                        (isRight ? "오른쪽 " : "왼쪽 ") + i);
                }
            }
        }

        [UnityTest]
        public IEnumerator Sweep_TelegraphEndAlone_NeverExplodesWithoutTheServerZoneConfirmation()
        {
            _useRealClock = true;
            Assert.IsTrue(_presenter.BeginTelegraph(SweepZone(0, baseSeconds: 0.15f)));
            yield return new WaitForSeconds(0.35f);

            Visuals(SWEEP_CAST + 1, out BossTelegraphCast cast, out SectorVFXController sector, out _);
            Assert.IsTrue(sector.IsHolding, "예고가 끝나도 서버 확정 전에는 터지지 않는다");
            CollectionAssert.DoesNotContain(_events, (SWEEP_CAST + 1) + ":Execute");
        }

        [UnityTest]
        public IEnumerator Sweep_ServerZoneBlast_ExplodesOnlyThatZoneAndTheRestKeepWaiting()
        {
            _useRealClock = true;
            BeginSweepZones(baseSeconds: 0.2f);
            yield return new WaitForSeconds(0.3f);

            Assert.IsTrue(_presenter.Strike(SWEEP_CAST + 1, Now));
            yield return new WaitForSeconds(0.1f);

            Visuals(SWEEP_CAST + 1, out BossTelegraphCast first, out SectorVFXController s1, out _);
            Assert.IsFalse(s1.IsHolding, "확정된 구역은 폭발로 이어진다");
            CollectionAssert.Contains(_events, (SWEEP_CAST + 1) + ":Execute");
            for (int i = 1; i < 4; ++i)
            {
                CollectionAssert.DoesNotContain(_events, (SWEEP_CAST + 1 + i) + ":Execute", "다른 구역은 확정 전이다");
            }

            Assert.IsFalse(_presenter.Strike(SWEEP_CAST + 1, Now), "중복 폭발 알림은 다시 터지지 않는다");
        }

        [Test]
        public void Sweep_DuplicateStartOfAZone_IsIgnored()
        {
            _useRealClock = true;
            BeginSweepZones();

            Assert.IsFalse(_presenter.BeginTelegraph(SweepZone(2)));
            Assert.AreEqual(4, _presenter.ActiveCount);
        }

        [UnityTest]
        public IEnumerator Sweep_CancelGroupMidSequence_StopsRemainingZonesAndKeepsBlastedOnesPlaying()
        {
            _useRealClock = true;
            BeginSweepZones(baseSeconds: 0.2f);
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(_presenter.Strike(SWEEP_CAST + 1, Now));
            Assert.IsTrue(_presenter.Strike(SWEEP_CAST + 2, Now));

            Assert.IsTrue(_presenter.CancelGroup(SWEEP_CAST));

            Assert.IsTrue(_presenter.TryGetVisuals(SWEEP_CAST + 1, out BossTelegraphCast blasted, out _, out _), "이미 폭발한 구역은 끝까지 간다");
            Assert.IsTrue(blasted.HasExecuted);
            Assert.IsFalse(_presenter.TryGetVisuals(SWEEP_CAST + 3, out BossTelegraphCast gone3, out _, out _));
            Assert.IsFalse(_presenter.TryGetVisuals(SWEEP_CAST + 4, out BossTelegraphCast gone4, out _, out _));
            Assert.IsFalse(_presenter.Strike(SWEEP_CAST + 3, Now), "취소된 구역은 늦은 확정으로 터지지 않는다");
            Assert.IsFalse(_presenter.BeginTelegraph(SweepZone(3)), "취소가 먼저 온 묶음의 늦은 예고는 시작하지 못한다");
        }

        [Test]
        public void Sweep_CancelBeforeAnyZoneArrives_BlocksTheWholeLateGroup()
        {
            _useRealClock = true;

            Assert.IsFalse(_presenter.CancelGroup(SWEEP_CAST));

            for (int i = 0; i < 4; ++i)
            {
                Assert.IsFalse(_presenter.BeginTelegraph(SweepZone(i)), "구역 " + i);
            }

            AssertAllReturned();
            Assert.IsTrue(_presenter.BeginTelegraph(SweepZone(0, castId: SWEEP_CAST + 50)), "다른 시전은 영향이 없다");
        }

        [UnityTest]
        public IEnumerator Sweep_LateStart_ContinuesFromTheServerStartTime()
        {
            _useRealClock = true;
            BeginSweepZones(true, Now - 0.6);
            yield return null;

            Visuals(SWEEP_CAST + 1, out BossTelegraphCast cast, out SectorVFXController sector, out _);
            Assert.GreaterOrEqual(sector.Elapsed, 0.55f, "이미 흐른 시간만큼 건너뛰어 시작한다");
        }

        [UnityTest]
        public IEnumerator Sweep_RestoreMidSequence_BlastedZonesContinueAndOthersWaitForTheServer()
        {
            _useRealClock = true;
            double start = Now - 1.2 - 0.3;
            for (int i = 0; i < 4; ++i)
            {
                BossTelegraphStart zone = SweepZone(i, true, start);
                double blastAt = start + 1.2 + (i * 0.25);
                bool blasted = blastAt <= Now;
                BossTelegraphSnapshot snapshot = new BossTelegraphSnapshot
                {
                    Start = zone,
                    Direction = zone.Direction,
                    Length = 6f,
                    IsDirectionLocked = true,
                    Phase = blasted ? EBossTelegraphPhase.Execute : EBossTelegraphPhase.Telegraph,
                    ExecuteServerTime = blasted ? blastAt : 0.0,
                    DashStoppedDistance = -1f
                };
                Assert.IsTrue(_presenter.Restore(in snapshot), "구역 " + i);
            }

            yield return null;

            Assert.AreEqual(4, _presenter.ActiveCount);
            Visuals(SWEEP_CAST + 1, out BossTelegraphCast zone0, out SectorVFXController sector0, out _);
            Assert.IsTrue(zone0.HasExecuted);
            Assert.IsFalse(sector0.IsHolding);
            Visuals(SWEEP_CAST + 3, out BossTelegraphCast zone2, out SectorVFXController sector2, out _);
            Assert.IsFalse(zone2.HasExecuted, "아직 서버 확정이 오지 않은 구역은 기다린다");

            Assert.IsTrue(_presenter.Strike(SWEEP_CAST + 3, Now));
            Assert.IsFalse(_presenter.Strike(SWEEP_CAST + 1, Now), "복원 전에 폭발한 구역은 다시 터지지 않는다");
            _presenter.ClearAll();
            AssertAllReturned();
        }

        [UnityTest]
        public IEnumerator Sweep_PoolReuse_ManySweepsLeakNothing()
        {
            _useRealClock = true;
            for (int round = 0; round < 6; ++round)
            {
                int cast = 900 + (round * 10);
                BeginSweepZones((round % 2) == 0, null, cast);
                yield return null;
                Assert.AreEqual(4, _presenter.ActiveCount);

                Assert.IsTrue(_presenter.CancelGroup(cast));
                AssertAllReturned();
            }
        }

        // 여우불 이동 표현, 서버가 정한 쪽의 반원을 서버 시각으로 따라간다
        [Test]
        public void SweepCue_MovesTowardTheServerChosenSideAndClearsWithTheCast()
        {
            _useRealClock = true;
            BossSweepCueView cue = Cue();

            foreach (bool isRight in new[] { true, false })
            {
                BossSweepStart start = new BossSweepStart(BOSS_ID, 850, Vector3.zero, Vector3.forward, isRight, 6f, 180f, 1.2f, 0.75f,
                    Now - 1.2);
                Assert.IsTrue(_presenter.BeginSweepCue(in start));
                Assert.IsTrue(cue.IsShown);

                // 예고가 끝난 시각의 위치는 반원 한가운데(90도)다
                cue.Clear();
                Assert.IsTrue(cue.Begin(in start, () => Now));
                Vector3 flat = cue.CurrentPosition;
                flat.y = 0f;
                float expected = isRight ? 90f : -90f;
                Assert.AreEqual(expected, Vector3.SignedAngle(Vector3.forward, flat, Vector3.up), 2f);

                Assert.IsTrue(_presenter.ClearSweepCue(850));
                Assert.IsFalse(cue.IsShown);
            }
        }

        [Test]
        public void SweepCue_ClearOfAnotherCastDoesNothing_AndCancelGroupAndClearAllRemoveIt()
        {
            _useRealClock = true;
            BossSweepCueView cue = Cue();
            BossSweepStart start = new BossSweepStart(BOSS_ID, SWEEP_CAST, Vector3.zero, Vector3.forward, true, 6f, 180f, 1.2f, 0.75f, Now);

            Assert.IsTrue(_presenter.BeginSweepCue(in start));
            Assert.IsFalse(_presenter.ClearSweepCue(SWEEP_CAST + 7));
            Assert.IsTrue(cue.IsShown);

            _presenter.CancelGroup(SWEEP_CAST);
            Assert.IsFalse(cue.IsShown, "취소하면 여우불도 사라진다");

            Assert.IsTrue(_presenter.BeginSweepCue(in start));
            _presenter.ClearAll();
            Assert.IsFalse(cue.IsShown);
        }

        [Test]
        public void SweepCue_StartThatEndedLongAgo_IsNotShown()
        {
            _useRealClock = true;
            BossSweepStart old = new BossSweepStart(BOSS_ID, SWEEP_CAST, Vector3.zero, Vector3.forward, true, 6f, 180f, 1.2f, 0.75f, Now - 10.0);

            Assert.IsFalse(_presenter.BeginSweepCue(in old));
            Assert.IsFalse(Cue().IsShown);
        }

        // 샘플 입력의 2페이즈 P03 재생
        [UnityTest]
        public IEnumerator SampleInput_SweepRightAndLeft_ZonesExplodeInOrderAndReturnToPool()
        {
            BossTelegraphSampleInput sample = PrepareSampleWithoutLoop();
            _useRealClock = true;
            sample.enabled = true;
            yield return null;

            foreach (bool isRight in new[] { true, false })
            {
                if (isRight)
                {
                    sample.PlayPhaseTwoSweepRight();
                }
                else
                {
                    sample.PlayPhaseTwoSweepLeft();
                }

                int last = sample.LastCastId;
                Assert.AreEqual(4, _presenter.ActiveCount, "구역 4개의 예고가 동시에 있다");
                Assert.IsTrue(Cue().IsShown);

                yield return WaitUntilVeryLong(() => (_presenter.ActiveCount == 0) && (!Cue().IsShown));
                yield return null;
                AssertAllReturned();
                for (int i = 0; i < 4; ++i)
                {
                    CollectionAssert.Contains(_events, (last - 3 + i) + ":Execute", "구역 " + i);
                }
            }
        }

        [UnityTest]
        public IEnumerator SampleInput_SweepCancelled_OnlyTheFirstTwoZonesExplode()
        {
            BossTelegraphSampleInput sample = PrepareSampleWithoutLoop();
            _useRealClock = true;
            sample.enabled = true;
            yield return null;

            sample.PlayPhaseTwoSweepCancelled();
            int last = sample.LastCastId;

            yield return WaitUntilVeryLong(() => (_presenter.ActiveCount == 0) && (!Cue().IsShown));
            yield return null;

            AssertAllReturned();
            CollectionAssert.Contains(_events, (last - 3) + ":Execute");
            CollectionAssert.Contains(_events, (last - 2) + ":Execute");
            CollectionAssert.DoesNotContain(_events, (last - 1) + ":Execute");
            CollectionAssert.DoesNotContain(_events, last + ":Execute");
        }

        [UnityTest]
        public IEnumerator SampleInput_SweepDuplicateLateAndRestore_PlayWithoutRestartingOrRepeating()
        {
            BossTelegraphSampleInput sample = PrepareSampleWithoutLoop();
            _useRealClock = true;
            sample.enabled = true;
            yield return null;

            sample.PlayPhaseTwoSweepDuplicate();
            Assert.AreEqual(4, _presenter.ActiveCount, "중복 시작 알림은 구역을 늘리지 않는다");
            yield return WaitUntilVeryLong(() => (_presenter.ActiveCount == 0) && (!Cue().IsShown));
            yield return null;
            AssertAllReturned();

            sample.PlayPhaseTwoSweepLateStart();
            Assert.AreEqual(4, _presenter.ActiveCount);
            yield return WaitUntilVeryLong(() => (_presenter.ActiveCount == 0) && (!Cue().IsShown));
            yield return null;
            AssertAllReturned();

            sample.PlayPhaseTwoSweepRestore();
            yield return null;
            Assert.AreEqual(4, _presenter.ActiveCount, "복원은 폭발한 구역과 기다리는 구역을 모두 되살린다");
            yield return WaitUntilVeryLong(() => (_presenter.ActiveCount == 0) && (!Cue().IsShown));
            yield return null;
            AssertAllReturned();
        }

        /****************************************
        *       Phase 2 P02: Fixed Parallel Beams
        ****************************************/

        private static readonly Vector3 BeamArenaCenter = new Vector3(10f, 0f, -5f);
        private const float BEAM_ARENA_RADIUS = 18f;
        private const float BEAM_TELEGRAPH    = 1.5f;
        private const float BEAM_DURATION     = 4f;
        private const float BEAM_TICK         = 0.5f;
        private const int   FIRST_BEAM        = 901;
        private const int   SECOND_BEAM       = 902;

        private BossBeamStart BeamStart(int beamId, int index, double? start = null, int castId = 900, Vector3? arenaCenter = null,
            int bossId = BOSS_ID)
        {
            BossBeamShape shape = BossBeamGeometry.Place(arenaCenter ?? BeamArenaCenter, BEAM_ARENA_RADIUS, Vector3.forward, index, 2,
                5f, 2f);
            return new BossBeamStart(bossId, beamId, castId, in shape, start ?? Now, BEAM_TELEGRAPH, BEAM_DURATION, BEAM_TICK);
        }

        private void BeamVisual(int beamId, out BossBeamTrack track, out BeamGroundVFXController visual)
        {
            Assert.IsTrue(_presenter.TryGetBeam(beamId, out track, out visual), "진행 중인 빔이 없다 " + beamId);
        }

        // 한 시전의 두 빔이 같은 묶음 번호로 동시에 예고되고, 서로 덮어쓰거나 지우지 않으며 각자 고정된 자리를 그린다
        [UnityTest]
        public IEnumerator Beam_TwoBeamsTelegraphTogether_EachHasItsOwnFixedVisual()
        {
            BossBeamStart a = BeamStart(FIRST_BEAM, 0);
            BossBeamStart b = BeamStart(SECOND_BEAM, 1);
            Assert.IsTrue(_presenter.BeginBeam(in a));
            Assert.IsTrue(_presenter.BeginBeam(in b), "같은 시전의 둘째 빔이 첫째를 지우지 않는다");
            yield return null;

            Assert.AreEqual(2, _presenter.BeamCount);
            Assert.AreEqual(2, _beamPool.LeasedCount);
            BeamVisual(FIRST_BEAM, out BossBeamTrack trackA, out BeamGroundVFXController visualA);
            BeamVisual(SECOND_BEAM, out BossBeamTrack trackB, out BeamGroundVFXController visualB);

            Assert.AreNotSame(visualA, visualB);
            Assert.IsTrue(visualA.IsShown);
            Assert.IsTrue(visualB.IsShown);
            Assert.AreEqual(-5f, visualA.LateralOffset, 0.0001f);
            Assert.AreEqual(5f, visualB.LateralOffset, 0.0001f);
            Assert.AreEqual(2f, visualA.Width, 0.0001f);
            Assert.AreEqual(BEAM_ARENA_RADIUS, visualA.ArenaRadius, 0.0001f);
            Assert.AreEqual(BeamArenaCenter, visualA.ArenaCenter);
            Assert.AreEqual(900, trackA.CastId);
            Assert.AreEqual(900, trackB.CastId, "시전 번호는 묶음이다");
            Assert.IsFalse(trackA.IsActivated);
            Assert.AreEqual(EBossBeamPhase.Telegraph, trackA.GetPhase(Now));

            // 예고 동안 시간이 흘러도 자리는 그대로이고 서버 활성 전에는 활성으로 그리지 않는다
            _clock += 1.0;
            yield return null;
            Assert.AreEqual(2, _presenter.BeamCount);
            Assert.IsFalse(trackA.IsActivated);
            Assert.AreEqual(-5f, visualA.LateralOffset, 0.0001f);
        }

        // 예고 시간이 끝나도 서버 활성 확정이 없으면 지속 빔이 되지 않고 대기 시간 뒤 풀로 돌아온다
        [UnityTest]
        public IEnumerator Beam_NotActiveWithoutServerActivation_ExpiresAfterTheConfirmGrace()
        {
            BossBeamStart a = BeamStart(FIRST_BEAM, 0);
            Assert.IsTrue(_presenter.BeginBeam(in a));
            yield return null;

            _clock += BEAM_TELEGRAPH + 0.2;
            yield return null;
            Assert.AreEqual(1, _presenter.BeamCount, "예고가 끝난 직후에는 서버 확정을 잠시 기다린다");
            BeamVisual(FIRST_BEAM, out BossBeamTrack track, out BeamGroundVFXController _);
            Assert.IsFalse(track.IsActivated, "예고 시간이 끝난 것만으로 활성이 되지 않는다");

            _clock += 1.0;
            yield return null;
            yield return null;
            AssertAllReturned();
        }

        // 활성에서 타격 회차, 지속 시간 끝의 옅어짐, 풀 반환까지
        [UnityTest]
        public IEnumerator Beam_FullLifecycle_ActivatesTicksThenFadesAtTheEndAndReturnsToPool()
        {
            BossBeamStart a = BeamStart(FIRST_BEAM, 0);
            Assert.IsTrue(_presenter.BeginBeam(in a));
            yield return null;
            BeamVisual(FIRST_BEAM, out BossBeamTrack track, out BeamGroundVFXController visual);

            double activate = a.StartServerTime + BEAM_TELEGRAPH;
            _clock = activate;
            Assert.IsTrue(_presenter.ActivateBeam(FIRST_BEAM, activate));
            yield return null;
            Assert.IsTrue(track.IsActivated);
            Assert.AreEqual(EBossBeamPhase.Active, track.GetPhase(_clock));

            for (int i = 0; i < 8; ++i)
            {
                _clock = activate + (i * (double)BEAM_TICK);
                Assert.IsTrue(_presenter.TickBeam(FIRST_BEAM, i, _clock));
                yield return null;
                Assert.AreEqual(i, track.LastTickIndex);
                Assert.IsTrue(visual.IsShown);
            }

            Assert.IsFalse(_presenter.TickBeam(FIRST_BEAM, 7, _clock), "같은 회차는 다시 표시하지 않는다");
            Assert.IsFalse(_presenter.ActivateBeam(FIRST_BEAM, activate), "중복 활성은 무시한다");
            Assert.AreEqual(0.5f, track.GetLifeRatio(activate + 2.0), 0.0001f);

            _clock = activate + BEAM_DURATION;
            yield return null;
            Assert.AreEqual(1, _presenter.BeamCount, "지속 시간이 끝나면 옅어지는 동안만 남는다");

            _clock += visual.FadeSeconds + 0.1;
            yield return null;
            yield return null;

            AssertAllReturned();
            Assert.IsFalse(visual.IsShown);
        }

        [UnityTest]
        public IEnumerator Beam_ServerExpiredMessage_FadesAndReturns_NoFurtherTicks()
        {
            BossBeamStart a = BeamStart(FIRST_BEAM, 0);
            Assert.IsTrue(_presenter.BeginBeam(in a));
            Assert.IsTrue(_presenter.ActivateBeam(FIRST_BEAM, Now));
            yield return null;

            Assert.IsTrue(_presenter.EndBeam(FIRST_BEAM, EBossAreaFate.Expired));
            Assert.IsFalse(_presenter.TickBeam(FIRST_BEAM, 2, _clock));
            yield return null;
            _clock += 1.0;
            yield return null;
            yield return null;

            AssertAllReturned();
        }

        // 예고가 대기 시간 때문에 지워진 뒤에 서버 활성이 늦게 오면 예고 때 받은 값으로 이어서 그린다. 이미 모두 지난 활성은 그리지 않는다
        [UnityTest]
        public IEnumerator Beam_LateActivationAfterTheTelegraphWasDropped_ContinuesFromTheServerActivationTime()
        {
            BossBeamStart a = BeamStart(FIRST_BEAM, 0);
            BossBeamStart b = BeamStart(SECOND_BEAM, 1);
            Assert.IsTrue(_presenter.BeginBeam(in a));
            Assert.IsTrue(_presenter.BeginBeam(in b));
            yield return null;

            _clock += BEAM_TELEGRAPH + 1.0;
            yield return null;
            yield return null;
            Assert.AreEqual(0, _presenter.BeamCount, "서버 확정 없이 대기 시간이 지나 지웠다");
            Assert.IsFalse(_presenter.BeginBeam(in a), "지운 번호의 중복 시작은 무시한다");

            double activate = a.StartServerTime + BEAM_TELEGRAPH;
            Assert.IsTrue(_presenter.ActivateBeam(FIRST_BEAM, activate), "늦게 온 서버 활성은 이어서 그린다");
            Assert.AreEqual(1, _presenter.BeamCount);
            BeamVisual(FIRST_BEAM, out BossBeamTrack track, out BeamGroundVFXController visual);
            Assert.IsTrue(track.IsActivated);
            Assert.AreEqual(activate, track.ActivateServerTime, 0.0001);
            Assert.IsTrue(visual.IsShown);
            Assert.AreEqual(-5f, visual.LateralOffset, 0.0001f, "예고 때 받은 자리를 쓴다");

            Assert.IsFalse(_presenter.ActivateBeam(SECOND_BEAM, _clock - 20.0), "지속 시간이 이미 모두 지난 활성은 그리지 않는다");
            Assert.AreEqual(1, _presenter.BeamCount);
        }

        // 서버 활성이 예고 시작보다 먼저 도착해도 기억해 두었다가 예고가 오면 활성으로 시작한다
        [Test]
        public void Beam_ActivationArrivingBeforeTheTelegraph_IsRememberedAndApplied()
        {
            double activate = Now + 0.1;
            Assert.IsFalse(_presenter.ActivateBeam(FIRST_BEAM, activate), "예고가 없어 아직 그릴 수 없다");
            Assert.AreEqual(0, _presenter.BeamCount);

            BossBeamStart a = BeamStart(FIRST_BEAM, 0, Now - BEAM_TELEGRAPH + 0.1);
            Assert.IsTrue(_presenter.BeginBeam(in a));
            BeamVisual(FIRST_BEAM, out BossBeamTrack track, out BeamGroundVFXController _);
            Assert.IsTrue(track.IsActivated, "먼저 온 활성을 이어서 쓴다");
            Assert.AreEqual(activate, track.ActivateServerTime, 0.0001);
        }

        // 활성 전에 한 빔만 취소돼도 같은 시전의 다른 빔은 남고, 취소가 시작보다 먼저 와도 늦은 시작이 되살아나지 않는다
        [UnityTest]
        public IEnumerator Beam_CancelOneBeam_KeepsTheOtherAndCancelBeforeStartBlocksTheLateStart()
        {
            Assert.IsFalse(_presenter.CancelBeam(910), "아직 모르는 번호의 취소");
            BossBeamStart early = BeamStart(910, 0);
            Assert.IsFalse(_presenter.BeginBeam(in early), "취소가 먼저 온 번호는 시작하지 못한다");

            BossBeamStart a = BeamStart(FIRST_BEAM, 0);
            BossBeamStart b = BeamStart(SECOND_BEAM, 1);
            Assert.IsTrue(_presenter.BeginBeam(in a));
            Assert.IsTrue(_presenter.BeginBeam(in b));
            yield return null;

            BeamVisual(FIRST_BEAM, out BossBeamTrack trackA, out BeamGroundVFXController visualA);
            Assert.IsTrue(_presenter.CancelBeam(FIRST_BEAM));
            Assert.IsFalse(visualA.IsShown);
            Assert.AreEqual(1, _presenter.BeamCount);
            Assert.AreEqual(1, _beamPool.LeasedCount);
            BeamVisual(SECOND_BEAM, out BossBeamTrack trackB, out BeamGroundVFXController visualB);
            Assert.IsTrue(visualB.IsShown, "다른 빔은 남는다");

            Assert.IsFalse(_presenter.ActivateBeam(FIRST_BEAM, Now), "취소된 번호의 활성은 되살리지 못한다");
            Assert.IsFalse(_presenter.TickBeam(FIRST_BEAM, 0, Now));
            Assert.IsFalse(_presenter.EndBeam(FIRST_BEAM, EBossAreaFate.Expired));
            Assert.IsFalse(_presenter.CancelBeam(FIRST_BEAM));
            Assert.IsFalse(_presenter.BeginBeam(in a), "취소된 번호의 중복 시작은 무시한다");

            Assert.IsTrue(_presenter.ActivateBeam(SECOND_BEAM, Now));
            Assert.IsTrue(trackB.IsActivated);
        }

        // 활성 중 사망이나 전투 종료로 두 빔이 모두 즉시 사라지고 이후 알림이 되살리지 못한다
        [UnityTest]
        public IEnumerator Beam_CancelWhileActive_HidesAtOnceAndLateNotificationsCannotRevive()
        {
            BossBeamStart a = BeamStart(FIRST_BEAM, 0);
            BossBeamStart b = BeamStart(SECOND_BEAM, 1);
            Assert.IsTrue(_presenter.BeginBeam(in a));
            Assert.IsTrue(_presenter.BeginBeam(in b));
            Assert.IsTrue(_presenter.ActivateBeam(FIRST_BEAM, Now));
            Assert.IsTrue(_presenter.ActivateBeam(SECOND_BEAM, Now));
            yield return null;
            BeamVisual(FIRST_BEAM, out BossBeamTrack _, out BeamGroundVFXController visualA);

            Assert.IsTrue(_presenter.EndBeam(FIRST_BEAM, EBossAreaFate.Cancelled));
            Assert.IsTrue(_presenter.CancelBeam(SECOND_BEAM));

            Assert.IsFalse(visualA.IsShown);
            AssertAllReturned();
            Assert.IsFalse(_presenter.TickBeam(FIRST_BEAM, 1, _clock));
            Assert.IsFalse(_presenter.ActivateBeam(SECOND_BEAM, _clock));
            BossBeamSnapshot snapshot = new BossBeamSnapshot { Start = a, IsActivated = true, ActivateServerTime = Now, Fate = EBossAreaFate.Active, LastTickIndex = -1 };
            Assert.IsFalse(_presenter.RestoreBeam(in snapshot));
        }

        [Test]
        public void Beam_TickBeforeActivation_IsIgnored_AndDuplicateStartIsIgnored()
        {
            BossBeamStart a = BeamStart(FIRST_BEAM, 0);
            Assert.IsTrue(_presenter.BeginBeam(in a));
            Assert.IsFalse(_presenter.BeginBeam(in a));
            Assert.AreEqual(1, _presenter.BeamCount);
            Assert.AreEqual(1, _beamPool.LeasedCount);
            Assert.IsFalse(_presenter.TickBeam(FIRST_BEAM, 0, Now), "활성 전의 타격 알림은 받아들이지 않는다");
            Assert.IsFalse(_presenter.TickBeam(999, 0, Now), "모르는 번호");
        }

        // 활성 중에 접속한 클라이언트가 스냅샷으로 복원, 이미 표시한 회차를 다시 표시하지 않고 남은 수명부터 이어서 그린다
        [Test]
        public void Beam_Restore_ContinuesAndDoesNotReplayShownTicks_EndedSnapshotsIgnored()
        {
            double activate = Now - 1.7;
            BossBeamStart a = BeamStart(FIRST_BEAM, 0, activate - BEAM_TELEGRAPH);
            BossBeamSnapshot snapshot = new BossBeamSnapshot
            {
                Start              = a,
                IsActivated        = true,
                ActivateServerTime = activate,
                Fate               = EBossAreaFate.Active,
                LastTickIndex      = 3,
                LastTickServerTime = activate + 1.5
            };

            Assert.IsTrue(_presenter.RestoreBeam(in snapshot));
            BeamVisual(FIRST_BEAM, out BossBeamTrack track, out BeamGroundVFXController visual);
            Assert.IsTrue(visual.IsShown);
            Assert.IsTrue(track.IsActivated);
            Assert.AreEqual(3, track.LastTickIndex);
            Assert.AreEqual(1f - (1.7f / BEAM_DURATION), track.GetLifeRatio(Now), 0.001f, "남은 수명부터 이어서 그린다");
            Assert.IsFalse(_presenter.TickBeam(FIRST_BEAM, 3, Now), "이미 표시한 회차");
            Assert.IsTrue(_presenter.TickBeam(FIRST_BEAM, 4, Now));
            Assert.IsFalse(_presenter.RestoreBeam(in snapshot), "같은 번호의 복원은 무시한다");

            Assert.IsTrue(_presenter.TryGetBeamSnapshot(FIRST_BEAM, out BossBeamSnapshot current));
            Assert.IsTrue(current.IsActivated);
            Assert.AreEqual(4, current.LastTickIndex);

            BossBeamSnapshot expired = new BossBeamSnapshot { Start = BeamStart(920, 0, Now - 20.0), IsActivated = true,
                ActivateServerTime = Now - 18.0, Fate = EBossAreaFate.Active, LastTickIndex = -1 };
            Assert.IsFalse(_presenter.RestoreBeam(in expired), "지속 시간이 모두 지난 스냅샷");
            BossBeamSnapshot cancelled = new BossBeamSnapshot { Start = BeamStart(921, 0), Fate = EBossAreaFate.Cancelled, LastTickIndex = -1 };
            Assert.IsFalse(_presenter.RestoreBeam(in cancelled));
            Assert.AreEqual(1, _presenter.BeamCount);
        }

        [Test]
        public void Beam_RestoreDuringTelegraph_KeepsWaitingForTheServerActivation()
        {
            BossBeamStart a = BeamStart(FIRST_BEAM, 0, Now - 0.5);
            BossBeamSnapshot snapshot = new BossBeamSnapshot { Start = a, IsActivated = false, Fate = EBossAreaFate.Active, LastTickIndex = -1 };

            Assert.IsTrue(_presenter.RestoreBeam(in snapshot));
            BeamVisual(FIRST_BEAM, out BossBeamTrack track, out BeamGroundVFXController _);
            Assert.IsFalse(track.IsActivated);
            Assert.AreEqual(EBossBeamPhase.Telegraph, track.GetPhase(Now));
            Assert.IsTrue(_presenter.ActivateBeam(FIRST_BEAM, Now + 1.0));
            Assert.IsTrue(track.IsActivated);
        }

        // 표현 메시가 서버 판정과 같은 영역(폭 띠와 전장 원의 교집합)이다
        [UnityTest]
        public IEnumerator Beam_VisualMeshStaysInsideTheServerAreaAndTouchesTheArenaBoundary()
        {
            BossBeamStart b = BeamStart(SECOND_BEAM, 1);
            Assert.IsTrue(_presenter.BeginBeam(in b));
            yield return null;

            BeamVisual(SECOND_BEAM, out BossBeamTrack track, out BeamGroundVFXController visual);
            BossBeamShape shape = track.Start.Shape;
            Vector3[] vertices = visual.GetComponent<MeshFilter>().sharedMesh.vertices;
            Assert.Greater(vertices.Length, 4);
            for (int i = 0; i < vertices.Length; ++i)
            {
                Vector3 world = visual.transform.TransformPoint(vertices[i]);
                Assert.IsTrue(BossBeamGeometry.Contains(in shape, world), "정점 " + i + " " + world);
                Vector3 offset = world - shape.ArenaCenter;
                offset.y = 0f;
                Assert.AreEqual(BEAM_ARENA_RADIUS, offset.magnitude, 0.001f, "앞뒤 끝 정점은 전장 경계 위다");
            }
        }

        // 풀 재사용, 빔마다 자기 전장 기준 자리에서 시작하고 아무것도 새지 않는다
        [UnityTest]
        public IEnumerator Beam_PoolReuse_ManyCastsLeakNothingAndStartFromTheirOwnPlacement()
        {
            for (int cast = 0; cast < 6; ++cast)
            {
                Vector3 center = new Vector3(cast * 3f, 0f, cast);
                int firstId = 1000 + (cast * 10);
                BossBeamStart a = BeamStart(firstId, 0, null, 900 + cast, center);
                BossBeamStart b = BeamStart(firstId + 1, 1, null, 900 + cast, center);
                Assert.IsTrue(_presenter.BeginBeam(in a));
                Assert.IsTrue(_presenter.BeginBeam(in b));
                Assert.IsTrue(_presenter.ActivateBeam(firstId, Now));
                yield return null;

                BeamVisual(firstId, out BossBeamTrack _, out BeamGroundVFXController visual);
                Assert.AreEqual(center, visual.ArenaCenter);
                Assert.AreEqual(2, _beamPool.LeasedCount);

                Assert.IsTrue(_presenter.CancelBeam(firstId));
                Assert.IsTrue(_presenter.CancelBeam(firstId + 1));
                AssertAllReturned();
            }
        }

        // 빔 풀이 모자라면 표현만 생략하고 모델은 그대로 따라간다
        [Test]
        public void Beam_PoolExhausted_SkipsVisualWithoutErrors()
        {
            for (int i = 0; i < BEAM_POOL_CAPACITY + 2; ++i)
            {
                BossBeamStart start = BeamStart(1100 + i, i % 2);
                Assert.IsTrue(_presenter.BeginBeam(in start));
            }

            Assert.AreEqual(BEAM_POOL_CAPACITY + 2, _presenter.BeamCount);
            Assert.AreEqual(BEAM_POOL_CAPACITY, _beamPool.LeasedCount);
            BeamVisual(1100 + BEAM_POOL_CAPACITY + 1, out BossBeamTrack track, out BeamGroundVFXController visual);
            Assert.IsNull(visual);
            Assert.IsTrue(_presenter.ActivateBeam(1100 + BEAM_POOL_CAPACITY + 1, Now));
            Assert.IsTrue(track.IsActivated);

            _presenter.ClearAll();
            AssertAllReturned();
        }

        // 빔과 장판과 투사체가 함께 있어도 서로 풀을 빼앗거나 지우지 않고 ClearAll이 모두 돌려준다
        [UnityTest]
        public IEnumerator Beam_ClearAllAndDisable_ReleaseEveryBeamVisual_CoexistingWithAreasAndProjectiles()
        {
            BossBeamStart a = BeamStart(FIRST_BEAM, 0);
            BossBeamStart b = BeamStart(SECOND_BEAM, 1);
            Assert.IsTrue(_presenter.BeginBeam(in a));
            Assert.IsTrue(_presenter.BeginBeam(in b));
            Assert.IsTrue(_presenter.ActivateBeam(FIRST_BEAM, Now));
            BossAreaStart area = AreaShot(1);
            Assert.IsTrue(_presenter.ActivateArea(in area));
            BossProjectileStart shot = Shot(2);
            Assert.IsTrue(_presenter.LaunchProjectile(in shot));
            yield return null;

            Assert.AreEqual(2, _presenter.BeamCount);
            Assert.AreEqual(1, _presenter.AreaCount);
            Assert.AreEqual(1, _presenter.ProjectileCount);

            _presenter.ClearAll();
            AssertAllReturned();

            BossBeamStart again = BeamStart(1200, 0);
            Assert.IsTrue(_presenter.BeginBeam(in again));
            _presenter.enabled = false;
            yield return null;
            AssertAllReturned();
        }

        /****************************************
        *       Beam Sample Input Scenarios
        ****************************************/

        [UnityTest]
        public IEnumerator SampleInput_BeamNormalAndTelegraphCancel_PlayAndReturnEverything()
        {
            BossTelegraphSampleInput sample = PrepareSampleWithoutLoop();
            _useRealClock = true;
            sample.enabled = true;
            yield return null;

            sample.PlayPhaseTwoBeams();
            yield return null;
            Assert.AreEqual(2, _presenter.BeamCount, "예고 동안 두 줄이 동시에 보인다");
            yield return WaitUntilVeryLong(() => (_presenter.BeamCount == 2) && IsAnyBeamActive());
            yield return WaitUntilVeryLong(() => _presenter.BeamCount == 0);
            yield return null;
            AssertAllReturned();

            sample.PlayPhaseTwoBeamsTelegraphCancelled();
            yield return WaitUntilVeryLong(() => _presenter.BeamCount == 0);
            yield return null;
            AssertAllReturned();
        }

        [UnityTest]
        public IEnumerator SampleInput_BeamActiveCancelledLateAndRestore_PlayWithoutRepeatingOrLeaking()
        {
            BossTelegraphSampleInput sample = PrepareSampleWithoutLoop();
            _useRealClock = true;
            sample.enabled = true;
            yield return null;

            sample.PlayPhaseTwoBeamsActiveCancelled();
            yield return WaitUntilVeryLong(() => IsAnyBeamActive());
            yield return WaitUntilVeryLong(() => _presenter.BeamCount == 0);
            yield return null;
            AssertAllReturned();

            sample.PlayPhaseTwoBeamsLateActivation();
            yield return WaitUntilVeryLong(() => _presenter.BeamCount == 0);
            yield return null;
            AssertAllReturned();

            sample.PlayPhaseTwoBeamsRestore();
            yield return null;
            Assert.AreEqual(2, _presenter.BeamCount, "복원은 활성 중인 두 빔을 모두 되살린다");
            yield return WaitUntilVeryLong(() => _presenter.BeamCount == 0);
            yield return null;
            AssertAllReturned();
        }

        private bool IsAnyBeamActive()
        {
            for (int id = 1000; id < 1100; ++id)
            {
                if (_presenter.TryGetBeam(id, out BossBeamTrack track, out BeamGroundVFXController _) && track.IsActivated)
                {
                    return true;
                }
            }

            return false;
        }

        /****************************************
        *       Phase 2 P01: Rotating Star
        ****************************************/

        private static readonly Vector3 StarCenter = new Vector3(10f, 0f, -5f);
        private const float STAR_ARENA_RADIUS = 18f;
        private const float STAR_TELEGRAPH    = 2f;
        private const float STAR_DURATION     = 6f;
        private const float STAR_TICK         = 0.25f;
        private const int   FIRST_STAR        = 1301;
        private const int   SECOND_STAR       = 1302;

        private BossStarStart StarStart(int starId, bool right = true, double? start = null, Vector3? center = null, int castId = 1300)
        {
            BossStarShape shape = BossStarGeometry.Place(center ?? StarCenter, STAR_ARENA_RADIUS, Vector3.forward, 14f, 5, 2, 1.5f,
                20f, right);
            return new BossStarStart(BOSS_ID, starId, castId, in shape, start ?? Now, STAR_TELEGRAPH, STAR_DURATION, STAR_TICK);
        }

        private void StarVisual(int starId, out BossStarTrack track, out StarGroundVFXController visual)
        {
            Assert.IsTrue(_presenter.Stars.TryGet(starId, out track, out visual), "진행 중인 별형 구조가 없다 " + starId);
        }

        // 예고 중에는 정지한 별이 보이고 여우불 5개가 모두 있으며 서버 활성 전에는 돌지 않는다
        [UnityTest]
        public IEnumerator Star_Telegraph_ShowsAFixedStarAndDoesNotRotateWithoutServerActivation()
        {
            BossStarStart a = StarStart(FIRST_STAR);
            Assert.IsTrue(_presenter.BeginStar(in a));
            yield return null;

            Assert.AreEqual(1, _presenter.StarCount);
            StarVisual(FIRST_STAR, out BossStarTrack track, out StarGroundVFXController visual);
            Assert.IsTrue(visual.IsShown);
            Assert.AreEqual(5, visual.ShownOrbCount);
            Assert.AreEqual(1.5f, visual.BeamWidth, 0.0001f);

            Vector3 vertex0 = visual.GetOrbPosition(0);
            _clock += 1.0;
            yield return null;
            Assert.AreEqual(0f, visual.RotationDegrees, 0.0001f, "활성 전에는 회전하지 않는다");
            Assert.AreEqual(0f, (vertex0 - visual.GetOrbPosition(0)).magnitude, 0.0001f, "예고 중 여우불 위치는 그대로");
            Assert.IsFalse(track.IsActivated);
            Assert.AreEqual(EBossBeamPhase.Telegraph, track.GetPhase(Now));
        }

        // 예고 종료만으로는 회전하지 않고, 대기 시간 뒤 풀로 돌아온다
        [UnityTest]
        public IEnumerator Star_NotActiveWithoutServerActivation_ExpiresAfterTheConfirmGrace()
        {
            BossStarStart a = StarStart(FIRST_STAR);
            Assert.IsTrue(_presenter.BeginStar(in a));
            yield return null;

            _clock += STAR_TELEGRAPH + 0.2;
            yield return null;
            Assert.AreEqual(1, _presenter.StarCount);
            StarVisual(FIRST_STAR, out BossStarTrack track, out StarGroundVFXController visual);
            Assert.IsFalse(track.IsActivated);
            Assert.AreEqual(0f, visual.RotationDegrees, 0.0001f);

            _clock += 1.0;
            yield return null;
            yield return null;
            Assert.AreEqual(0, _presenter.StarCount);
            Assert.AreEqual(2, _presenter.Stars.FreeCount);
        }

        // 활성 확정 뒤에는 서버 시각의 현재 각도로 돌고, 오른쪽과 왼쪽은 반대로 돈다
        [UnityTest]
        public IEnumerator Star_ActiveRotationFollowsServerTime_RightAndLeftAreOpposite()
        {
            BossStarStart right = StarStart(FIRST_STAR, true);
            BossStarStart left  = StarStart(SECOND_STAR, false, null, null, 1310);
            Assert.IsTrue(_presenter.BeginStar(in right));
            Assert.IsTrue(_presenter.BeginStar(in left));
            double activate = Now + STAR_TELEGRAPH;
            _clock = activate;
            Assert.IsTrue(_presenter.ActivateStar(FIRST_STAR, activate));
            Assert.IsTrue(_presenter.ActivateStar(SECOND_STAR, activate));

            _clock = activate + 1.5;
            yield return null;
            StarVisual(FIRST_STAR, out BossStarTrack trackRight, out StarGroundVFXController visualRight);
            StarVisual(SECOND_STAR, out BossStarTrack trackLeft, out StarGroundVFXController visualLeft);
            Assert.AreEqual(30f, visualRight.RotationDegrees, 0.01f, "1.5초 x 초당 20도");
            Assert.AreEqual(-30f, visualLeft.RotationDegrees, 0.01f);
            Assert.AreEqual(30f, trackRight.GetRotationDegrees(_clock), 0.01f);

            // 서버 시각으로 계산하므로 프레임을 건너뛰어도 같은 각도다
            _clock = activate + 5.0;
            yield return null;
            Assert.AreEqual(100f, visualRight.RotationDegrees, 0.01f);

            // 여우불 꼭짓점은 구조와 함께 돌아 별의 모양이 유지된다
            BossStarShape shape = trackRight.Start.Shape;
            for (int i = 0; i < 5; ++i)
            {
                Vector3 expected = BossStarGeometry.GetVertex(in shape, i, 100f);
                Vector3 actual = visualRight.GetOrbPosition(i);
                Assert.AreEqual(expected.x, actual.x, 0.01f, "여우불 " + i);
                Assert.AreEqual(expected.z, actual.z, 0.01f, "여우불 " + i);
            }
        }

        [UnityTest]
        public IEnumerator Star_FullLifecycle_TicksThenStopsAtTheEndFadesAndReturnsToPool()
        {
            BossStarStart a = StarStart(FIRST_STAR);
            Assert.IsTrue(_presenter.BeginStar(in a));
            double activate = a.StartServerTime + STAR_TELEGRAPH;
            _clock = activate;
            Assert.IsTrue(_presenter.ActivateStar(FIRST_STAR, activate));
            yield return null;
            StarVisual(FIRST_STAR, out BossStarTrack track, out StarGroundVFXController visual);

            for (int i = 0; i < 24; ++i)
            {
                _clock = activate + (i * (double)STAR_TICK);
                Assert.IsTrue(_presenter.TickStar(FIRST_STAR, i, _clock));
                yield return null;
                Assert.AreEqual(i, track.LastTickIndex);
            }

            Assert.IsFalse(_presenter.TickStar(FIRST_STAR, 23, _clock), "같은 회차는 다시 표시하지 않는다");
            Assert.IsFalse(_presenter.ActivateStar(FIRST_STAR, activate), "중복 활성은 무시한다");

            _clock = activate + STAR_DURATION;
            yield return null;
            Assert.AreEqual(120f, visual.RotationDegrees, 0.01f, "유지 시간이 끝나면 마지막 각도에서 멈춘다");
            Assert.AreEqual(1, _presenter.StarCount, "옅어지는 동안만 남는다");

            _clock += 0.5;
            yield return null;
            yield return null;
            Assert.AreEqual(0, _presenter.StarCount);
            Assert.AreEqual(2, _presenter.Stars.FreeCount);
            Assert.IsFalse(visual.IsShown);
        }

        // 늦은 활성(예고가 대기 시간으로 지워진 뒤)은 활성 서버 시각의 현재 각도부터 이어서 그린다
        [UnityTest]
        public IEnumerator Star_LateActivation_ContinuesFromTheCurrentServerAngle()
        {
            BossStarStart a = StarStart(FIRST_STAR);
            Assert.IsTrue(_presenter.BeginStar(in a));
            yield return null;
            _clock += STAR_TELEGRAPH + 1.0;
            yield return null;
            yield return null;
            Assert.AreEqual(0, _presenter.StarCount);
            Assert.IsFalse(_presenter.BeginStar(in a), "지운 번호의 중복 시작은 무시한다");

            double activate = a.StartServerTime + STAR_TELEGRAPH;
            Assert.IsTrue(_presenter.ActivateStar(FIRST_STAR, activate));
            yield return null;
            StarVisual(FIRST_STAR, out BossStarTrack track, out StarGroundVFXController visual);
            Assert.IsTrue(track.IsActivated);
            Assert.AreEqual(20f, visual.RotationDegrees, 0.05f, "활성 시각 기준 1초가 흐른 각도, 처음 각도로 돌아가지 않는다");
        }

        [Test]
        public void Star_ActivationBeforeTelegraph_IsRememberedAndAppliedWhenTheTelegraphArrives()
        {
            double activate = Now + 0.1;
            Assert.IsFalse(_presenter.ActivateStar(FIRST_STAR, activate));
            BossStarStart a = StarStart(FIRST_STAR, true, Now - STAR_TELEGRAPH + 0.1);
            Assert.IsTrue(_presenter.BeginStar(in a));
            StarVisual(FIRST_STAR, out BossStarTrack track, out StarGroundVFXController _);
            Assert.IsTrue(track.IsActivated);
            Assert.AreEqual(activate, track.ActivateServerTime, 0.0001);
        }

        [UnityTest]
        public IEnumerator Star_CancelBeforeStartBlocksTheLateStart_AndCancelWhileActiveHidesAtOnce()
        {
            Assert.IsFalse(_presenter.CancelStar(1310));
            BossStarStart early = StarStart(1310);
            Assert.IsFalse(_presenter.BeginStar(in early), "취소가 먼저 온 번호는 시작하지 못한다");

            BossStarStart a = StarStart(FIRST_STAR);
            Assert.IsTrue(_presenter.BeginStar(in a));
            Assert.IsTrue(_presenter.ActivateStar(FIRST_STAR, Now));
            yield return null;
            StarVisual(FIRST_STAR, out BossStarTrack _, out StarGroundVFXController visual);

            Assert.IsTrue(_presenter.CancelStar(FIRST_STAR));
            Assert.IsFalse(visual.IsShown);
            Assert.AreEqual(0, _presenter.StarCount);
            Assert.AreEqual(2, _presenter.Stars.FreeCount);

            Assert.IsFalse(_presenter.TickStar(FIRST_STAR, 1, Now));
            Assert.IsFalse(_presenter.ActivateStar(FIRST_STAR, Now));
            Assert.IsFalse(_presenter.EndStar(FIRST_STAR, EBossAreaFate.Expired));
            Assert.IsFalse(_presenter.CancelStar(FIRST_STAR));
            BossStarSnapshot snapshot = new BossStarSnapshot { Start = a, IsActivated = true, ActivateServerTime = Now,
                Fate = EBossAreaFate.Active, LastTickIndex = -1 };
            Assert.IsFalse(_presenter.RestoreStar(in snapshot));
        }

        // 중간 복원은 서버 시각의 현재 각도로 이어서 그리고 이미 표시한 회차를 다시 표시하지 않는다
        [UnityTest]
        public IEnumerator Star_Restore_DrawsTheCurrentServerAngle_NotTheStartAngle()
        {
            double activate = Now - 2.0;
            BossStarStart a = StarStart(FIRST_STAR, true, activate - STAR_TELEGRAPH);
            BossStarSnapshot snapshot = new BossStarSnapshot
            {
                Start              = a,
                IsActivated        = true,
                ActivateServerTime = activate,
                Fate               = EBossAreaFate.Active,
                LastTickIndex      = 8,
                LastTickServerTime = activate + 2.0
            };

            Assert.IsTrue(_presenter.RestoreStar(in snapshot));
            yield return null;
            StarVisual(FIRST_STAR, out BossStarTrack track, out StarGroundVFXController visual);
            Assert.AreEqual(40f, visual.RotationDegrees, 0.05f, "2초 x 초당 20도, 처음 각도가 아니다");
            Assert.IsFalse(_presenter.TickStar(FIRST_STAR, 8, Now), "이미 표시한 회차");
            Assert.IsTrue(_presenter.TickStar(FIRST_STAR, 9, Now));
            Assert.IsFalse(_presenter.RestoreStar(in snapshot), "같은 번호의 복원은 무시한다");

            BossStarSnapshot expired = new BossStarSnapshot { Start = StarStart(1320, true, Now - 30.0), IsActivated = true,
                ActivateServerTime = Now - 28.0, Fate = EBossAreaFate.Active, LastTickIndex = -1 };
            Assert.IsFalse(_presenter.RestoreStar(in expired), "지속 시간이 모두 지난 스냅샷");
            BossStarSnapshot cancelled = new BossStarSnapshot { Start = StarStart(1321), Fate = EBossAreaFate.Cancelled, LastTickIndex = -1 };
            Assert.IsFalse(_presenter.RestoreStar(in cancelled));
            Assert.AreEqual(1, _presenter.StarCount);
        }

        // 표현 메시의 모든 정점이 서버 판정 영역(빔 직사각형)의 모서리 안이고, 회전해도 같다
        [UnityTest]
        public IEnumerator Star_VisualMeshMatchesTheServerBeamRectangles_AtAnyRotation()
        {
            BossStarStart a = StarStart(FIRST_STAR, true);
            Assert.IsTrue(_presenter.BeginStar(in a));
            double activate = Now;
            Assert.IsTrue(_presenter.ActivateStar(FIRST_STAR, activate));

            foreach (double elapsed in new[] { 0.0, 1.1, 4.3 })
            {
                _clock = activate + elapsed;
                yield return null;
                StarVisual(FIRST_STAR, out BossStarTrack track, out StarGroundVFXController visual);
                BossStarShape shape = track.Start.Shape;
                float rotation = track.GetRotationDegrees(_clock);
                Vector3[] vertices = visual.GetComponent<MeshFilter>().sharedMesh.vertices;
                Assert.AreEqual(5 * 4, vertices.Length, "빔 5개의 직사각형");
                for (int i = 0; i < vertices.Length; ++i)
                {
                    Vector3 world = visual.transform.TransformPoint(vertices[i]);
                    Assert.IsTrue(BossStarGeometry.Contains(in shape, world, rotation), "정점 " + i + " 회전 " + rotation);
                }
            }
        }

        [UnityTest]
        public IEnumerator Star_PoolReuseAndClearAll_ReleaseEverythingAndCoexistWithAreasAndBeams()
        {
            for (int cast = 0; cast < 5; ++cast)
            {
                Vector3 center = new Vector3(cast * 2f, 0f, cast);
                int starId = 1400 + cast;
                BossStarStart a = StarStart(starId, (cast % 2) == 0, null, center, 1400 + cast);
                Assert.IsTrue(_presenter.BeginStar(in a));
                Assert.IsTrue(_presenter.ActivateStar(starId, Now));
                yield return null;

                StarVisual(starId, out BossStarTrack _, out StarGroundVFXController visual);
                Assert.AreEqual(center, visual.transform.position);
                Assert.AreEqual(1, 2 - _presenter.Stars.FreeCount);
                Assert.IsTrue(_presenter.CancelStar(starId));
                Assert.AreEqual(2, _presenter.Stars.FreeCount);
            }

            BossStarStart star = StarStart(FIRST_STAR);
            BossBeamStart beam = BeamStart(FIRST_BEAM, 0);
            BossAreaStart area = AreaShot(1);
            Assert.IsTrue(_presenter.BeginStar(in star));
            Assert.IsTrue(_presenter.ActivateStar(FIRST_STAR, Now));
            Assert.IsTrue(_presenter.BeginBeam(in beam));
            Assert.IsTrue(_presenter.ActivateArea(in area));
            yield return null;
            Assert.AreEqual(1, _presenter.StarCount);
            Assert.AreEqual(1, _presenter.BeamCount);
            Assert.AreEqual(1, _presenter.AreaCount);

            _presenter.ClearAll();
            Assert.AreEqual(0, _presenter.StarCount);
            Assert.AreEqual(2, _presenter.Stars.FreeCount);
            AssertAllReturned();
        }

        [UnityTest]
        public IEnumerator SampleInput_StarRightLeftAndCancelled_PlayAndReturnEverything()
        {
            BossTelegraphSampleInput sample = PrepareSampleWithoutLoop();
            _useRealClock = true;
            sample.enabled = true;
            yield return null;

            sample.PlayPhaseTwoStarRight();
            yield return null;
            Assert.AreEqual(1, _presenter.StarCount, "예고 동안 구조 하나가 보인다");
            yield return WaitUntilVeryLong(() => IsAnyStarActive());
            yield return WaitUntilVeryLong(() => _presenter.StarCount == 0);
            yield return null;
            AssertAllReturned();

            sample.PlayPhaseTwoStarTelegraphCancelled();
            yield return WaitUntilVeryLong(() => _presenter.StarCount == 0);
            yield return null;
            AssertAllReturned();

            sample.PlayPhaseTwoStarActiveCancelled();
            yield return WaitUntilVeryLong(() => IsAnyStarActive());
            yield return WaitUntilVeryLong(() => _presenter.StarCount == 0);
            yield return null;
            AssertAllReturned();
        }

        [UnityTest]
        public IEnumerator SampleInput_StarLateActivationRestoreAndWithAreas_PlayWithoutRepeatingOrLeaking()
        {
            BossTelegraphSampleInput sample = PrepareSampleWithoutLoop();
            _useRealClock = true;
            sample.enabled = true;
            yield return null;

            sample.PlayPhaseTwoStarLateActivation();
            yield return WaitUntilVeryLong(() => _presenter.StarCount == 0);
            yield return null;
            AssertAllReturned();

            sample.PlayPhaseTwoStarRestore();
            yield return null;
            Assert.AreEqual(1, _presenter.StarCount, "복원은 회전 중인 구조를 되살린다");
            yield return WaitUntilVeryLong(() => _presenter.StarCount == 0);
            yield return null;
            AssertAllReturned();

            sample.PlayPhaseTwoStarWithAreas();
            yield return null;
            Assert.AreEqual(1, _presenter.StarCount);
            yield return WaitUntilVeryLong(() => (_presenter.StarCount == 0) && (_presenter.AreaCount == 0) && (_presenter.ActiveCount == 0));
            yield return null;
            AssertAllReturned();
        }

        private bool IsAnyStarActive()
        {
            for (int id = 1000; id < 1100; ++id)
            {
                if (_presenter.Stars.TryGet(id, out BossStarTrack track, out StarGroundVFXController _) && track.IsActivated)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
