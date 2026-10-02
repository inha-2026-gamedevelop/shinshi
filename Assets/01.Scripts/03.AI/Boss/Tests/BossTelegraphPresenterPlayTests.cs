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
    }
}
