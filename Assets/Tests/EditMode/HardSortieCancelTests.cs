// System
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;
using Shinshi.Systems;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class HardSortieCancelTests
    {
        // 하드 보스 출정을 열 수 없을 때 서비스 취소로 끝내는 경로를 고정한다. 세션 호스트의 취소 계약과
        // CombatServerRuntime의 첫 틱 보스 생성 분기를 본다: 준비된 보스는 난이도와 상관없이 만들고, 준비되지 않은 하드만 취소한다.
        // 월백은 하드도 준비 검사를 통과하므로 준비되지 않은 보스는 패턴 정의가 없는 보스 ID로 만든다.
        // 체력 기믹 묶음은 실행 설정(EnableBossGimmicks)을 켠 출정에만 붙는다

        /****************************************
        *                Fields
        ****************************************/

        private const int    REGISTRY_CAPACITY = 16;
        private const string SORTIE_ID         = "6f1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const string API_ONE           = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string BOSS_WOLBAEK      = "boss_wolbaek";
        private const string BOSS_UNDEFINED    = "boss_without_patterns"; // 패턴 카탈로그에 없는 보스, 출정 준비 검사가 거짓이다
        private const string MAP_ID            = "map_forest";
        private const string BATTLE_ID         = "battle_forest";
        private const int    SESSION_EPOCH     = 7;

        private SessionConfigSO       _config;
        private ServerSessionRegistry _registry;
        private DungeonSessionHost    _host;
        private List<string>          _warnings;
        private GameObject            _runtimeObject;
        private PlayerConfigSO        _playerConfig;
        private bool                  _gameDbInitialized;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config   = ScriptableObject.CreateInstance<SessionConfigSO>();
            _registry = new ServerSessionRegistry(REGISTRY_CAPACITY);
            _warnings = new List<string>();
            _host     = new DungeonSessionHost(_config, _registry, null, true, _warnings.Add, new CommittingSink());
        }

        [TearDown]
        public void TearDown()
        {
            if (_runtimeObject != null)
            {
                Object.DestroyImmediate(_runtimeObject);
            }

            _host.Dispose();
            Object.DestroyImmediate(_config);
            if (_playerConfig != null)
            {
                Object.DestroyImmediate(_playerConfig);
            }

            if (_gameDbInitialized)
            {
                GameDB.ResetForTests();
            }
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void CancelSortieService_RunningSessionAtTickTime_ReturnsTrueAndEndsWithServiceCancelled()
        {
            Admit(API_ONE, Boss(GameSessionCodes.Difficulty.HARD));
            double time = StartSession();

            Assert.IsTrue(_host.BeginTick(time + 1.0));
            Assert.IsTrue(_host.CancelSortieService(time + 1.0), "틱 시각으로 부르면 받는다");
            _host.CompleteTick();

            Assert.IsFalse(_host.IsSessionRunning);
            Assert.IsTrue(_host.Session.IsEnded);
            Assert.AreEqual(ESessionEndReason.ServiceCancelled, _host.Session.EndReason);
        }

        [Test]
        public void CancelSortieService_NotRunning_ReturnsFalse()
        {
            Assert.IsFalse(_host.CancelSortieService(0.0), "출정이 시작되기 전에는 받지 않는다");

            Admit(API_ONE, Boss(GameSessionCodes.Difficulty.HARD));
            Assert.IsFalse(_host.CancelSortieService(0.0), "명단만 있고 세션이 열리기 전에도 받지 않는다");

            double time = StartSession();
            Assert.IsTrue(_host.BeginTick(time + 1.0));
            Assert.IsTrue(_host.CancelSortieService(time + 1.0));
            _host.CompleteTick();

            Assert.IsFalse(_host.CancelSortieService(time + 1.0), "이미 끝난 세션은 다시 받지 않는다");
            Assert.IsFalse(_host.CancelSortieService(time + 2.0));
            Assert.AreEqual(ESessionEndReason.ServiceCancelled, _host.Session.EndReason, "첫 종료 사유가 바뀌지 않는다");
        }

        [Test]
        public void CancelSortieService_WrongTime_ReturnsFalseAndSessionKeepsRunning()
        {
            Admit(API_ONE, Boss(GameSessionCodes.Difficulty.HARD));
            double time = StartSession();

            // 틱 밖: 직전 틱보다 이른 시각
            Assert.IsFalse(_host.CancelSortieService(time - 1.0));

            // 틱 안: 열린 틱 시각과 다른 시각, 음수, 무한대
            Assert.IsTrue(_host.BeginTick(time + 1.0));
            Assert.IsFalse(_host.CancelSortieService(time + 2.0), "세션은 같은 틱 시각만 허용한다");
            Assert.IsFalse(_host.CancelSortieService(time));
            Assert.IsFalse(_host.CancelSortieService(-1.0));
            Assert.IsFalse(_host.CancelSortieService(double.PositiveInfinity));
            Assert.IsTrue(_host.IsSessionRunning, "거절된 취소는 세션을 끝내지 않는다");
            _host.CompleteTick();

            Assert.IsTrue(_host.IsSessionRunning);
            Assert.IsFalse(_host.Session.IsEnded);
        }

        [Test]
        public void Step_HardBossSortie_FirstTickSpawnsBossAndDoesNotCancel()
        {
            CombatServerRuntime runtime = CreateRuntime(GameSessionCodes.Difficulty.HARD);

            // 하드 월백은 1, 2페이즈 정의가 모두 준비돼 출정 준비 검사를 통과한다
            StepToFirstRunningTick(runtime);

            Assert.IsNotNull(runtime.Boss, "준비된 하드는 보스를 만든다");
            Assert.IsTrue(runtime.Boss.IsActive);
            Assert.IsTrue(_host.IsSessionRunning, "취소하지 않고 진행한다");
            Assert.AreNotEqual(ESessionEndReason.ServiceCancelled, _host.Session.EndReason);
            Assert.IsFalse(runtime.Boss.HasGimmicks, "실행 설정을 켜지 않으면 기믹을 붙이지 않는다");
        }

        [Test]
        public void Step_HardBossSortieNotReady_FirstTickDoesNotSpawnBossAndCancelsSession()
        {
            CombatServerRuntime runtime = CreateRuntime(GameSessionCodes.Difficulty.HARD, BOSS_UNDEFINED);

            // 패턴 정의가 없는 보스는 출정 준비 검사가 거짓이다. 첫 Step 안에서 취소로 끝낸다
            LogAssert.Expect(LogType.Error, new Regex("하드 출정 준비가 안 돼 보스를 만들지 않는다"));
            LogAssert.Expect(LogType.Error, new Regex("하드 출정을 서비스 취소로 끝낸다"));
            StepToFirstRunningTick(runtime);

            Assert.IsNull(runtime.Boss, "하드는 보스 없이 진행하지 않는다");
            Assert.IsTrue(_host.HasStarted);
            Assert.IsFalse(_host.IsSessionRunning);
            Assert.AreEqual(ESessionEndReason.ServiceCancelled, _host.Session.EndReason);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Step_EasyBossSortie_FirstTickSpawnsBossAndDoesNotCancel()
        {
            CombatServerRuntime runtime = CreateRuntime(GameSessionCodes.Difficulty.EASY);

            // 보스 개체 스포너를 연결하지 않아 경고만 남는다(경고는 시험을 실패시키지 않는다)
            StepToFirstRunningTick(runtime);

            Assert.IsTrue(_host.IsSessionRunning, "이지는 취소하지 않고 진행한다");
            Assert.AreNotEqual(ESessionEndReason.ServiceCancelled, _host.Session.EndReason);
            Assert.IsNotNull(runtime.Boss, "이지는 기존대로 보스를 만든다");
            Assert.IsFalse(runtime.Boss.HasGimmicks, "실행 설정을 켜지 않으면 기믹을 붙이지 않는다");
        }

        [TestCase(GameSessionCodes.Difficulty.EASY)]
        [TestCase(GameSessionCodes.Difficulty.HARD)]
        public void Step_BossGimmicksEnabled_FirstTickSpawnsBossWithGimmicks(int difficulty)
        {
            CombatServerRuntime runtime = CreateRuntime(difficulty);
            runtime.EnableBossGimmicks = true;

            StepToFirstRunningTick(runtime);

            Assert.IsNotNull(runtime.Boss);
            Assert.IsTrue(runtime.Boss.HasGimmicks, "실행 설정을 켠 월백 출정은 기믹 묶음을 붙인다");
            Assert.IsFalse(runtime.Boss.HasBarrier, "색 결계가 결계 전개를 대신한다");
            Assert.IsTrue(runtime.Boss.IsActive);
            Assert.IsTrue(_host.IsSessionRunning);
            Assert.IsFalse(runtime.Boss.TryGetGimmickStop(out _, out _, out _), "막 열린 전투에는 멈춘 기믹이 없다");
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static string Key(string apiId)
        {
            return Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + apiId;
        }

        private static SortieDescriptor Boss(int difficulty, string bossId = BOSS_WOLBAEK)
        {
            return new SortieDescriptor(GameSessionCodes.Mode.BOSS, MAP_ID, BATTLE_ID, bossId, difficulty);
        }

        // 호스트의 입장 검사만 통과시킨다. 패턴 정의가 있는지는 전투 런타임의 출정 준비 검사가 본다
        private static bool IsKnownBoss(string bossId)
        {
            return (bossId == BOSS_WOLBAEK) || (bossId == BOSS_UNDEFINED);
        }

        private void Admit(string apiId, SortieDescriptor sortie)
        {
            JoinTokenClaims claims = new JoinTokenClaims(Key(apiId), null, null, SORTIE_ID, null, EPlayerClass.Warrior).WithSortie(in sortie);
            Assert.AreEqual(ERejectReason.None, _registry.TryAdmit(in claims, out SessionBinding binding));
            Assert.AreEqual(ERejectReason.None, _host.Admit(in claims, binding));
        }

        // 시작 조건을 채우는 틱까지 돌리고 그 시각을 돌려준다
        private double StartSession()
        {
            double time = _config.ReadyCountdownSeconds;
            Assert.IsFalse(_host.BeginTick(0.0));
            Assert.IsTrue(_host.BeginTick(time));
            _host.CompleteTick();
            Assert.IsTrue(_host.IsSessionRunning);
            return time;
        }

        // 프로젝트 GameDB로 전투 런타임을 세우고 보스전 출정 한 명을 입장시킨다. Step은 비공개라 반사로 부른다
        private CombatServerRuntime CreateRuntime(int difficulty, string bossId = BOSS_WOLBAEK)
        {
            GameDB.ResetForTests();
            GameDatabaseSO root = Resources.Load<GameDatabaseSO>(Constants.Resource.GAME_DB_PATH);
            Assume.That(root, Is.Not.Null, "Resources/GameDB.asset이 없다");
            GameDB.Initialize(root);
            _gameDbInitialized = true;

            _host.ConfigureContent(GameDB.Waves, GameDB.Rewards, GameDB.Talismans, BOSS_WOLBAEK, IsKnownBoss);
            Admit(API_ONE, Boss(difficulty, bossId));

            _playerConfig  = ScriptableObject.CreateInstance<PlayerConfigSO>();
            _runtimeObject = new GameObject("HardSortieCancelRuntimeTest");
            var runtime    = _runtimeObject.AddComponent<CombatServerRuntime>();
            runtime.Initialize(_playerConfig, _config.ReconnectGraceSeconds, SESSION_EPOCH, _host.Roster);
            runtime.BindSession(_host);
            return runtime;
        }

        // 시작 전 틱 한 번과 시작 조건을 채우는 첫 진행 틱을 돌린다. 보스 생성 시도는 이 첫 진행 틱에 한 번 일어난다
        private void StepToFirstRunningTick(CombatServerRuntime runtime)
        {
            MethodInfo step = typeof(CombatServerRuntime).GetMethod("Step", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(step, "CombatServerRuntime.Step이 바뀌었다");

            float deltaTime = (float)(1.0 / Constants.Network.SERVER_TICK_RATE);
            step.Invoke(runtime, new object[] { 0.0, deltaTime });
            Assert.IsFalse(_host.HasStarted, "시작 조건 전에는 세션이 열리지 않는다");

            step.Invoke(runtime, new object[] { (double)_config.ReadyCountdownSeconds, deltaTime });
            Assert.IsTrue(_host.HasStarted);
        }

        private sealed class CommittingSink : IBackendEventSink
        {
            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count,
                IBackendEventSinkListener listener)
            {
                listener.OnBatchCompleted(attemptId, EEventBatchResult.Committed);
                return true;
            }
        }
    }
}
