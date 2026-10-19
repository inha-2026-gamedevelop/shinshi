// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using EnemyDefinition = Shinshi.Data.EnemyCatalogSO.EnemyDefinition;

namespace Shinshi.Systems
{
    // 호스트가 전투 틱 안(플레이어 피해 뒤, 종료 판정 앞)에서 Tick을 부른다. 처치 통지는 그 Tick 안에서만 하고,
    // 한 틱의 순서는 처치 반영 -> 웨이브 완료 -> 다음 구역 도착 -> 출현 -> 제한 시간이다. 마지막 처치와 제한 시간이 같은 틱이면 처치가 먼저라 성공이 된다.
    // 현재 웨이브를 모두 처치하면 다음 구역 입구로 이동하고, 접속 중인 생존 참가자 한 명이 도착하면 다음 웨이브가 시작된다.
    // 고정 휴식과 시간 묶음 출현은 없다. 제한 시간은 이동 중에도 흐른다.
    // source ID와 적 종류는 WaveConfigSO 정본의 배치 순번에서 한 번만 계산해 둔다(호스트의 사전 등록과 같은 규칙)
    /// <summary> 일반전 6웨이브 진행기, 배치 계획대로 빈 칸을 채워 적을 내보내고 처치, 도착, 제한 시간을 판정한다 </summary>
    public sealed class WaveSession : IWaveSession
    {
        /****************************************
        *              Constants
        ****************************************/

        public const float ARRIVAL_RADIUS           = 3f;    // m, 다음 웨이브 시작 지점에서 수평 도착 반경
        public const float ARRIVAL_HEIGHT_TOLERANCE = 1.5f;  // m, 같은 보행 층으로 보는 높이 차
        public const float SPAWN_RETRY_SECONDS      = 0.25f; // 출현 지점이 막혔을 때 다시 보는 간격
        public const float SPAWN_BLOCKED_NOTICE     = 3f;    // 초, 이 시간 이상 막히면 안내와 경고를 낸다
        public const float NEAR_BAND_MIN            = 6f;    // m, 근접, 돌진, 엘리트의 가장 가까운 생존자 기준 거리 띠
        public const float NEAR_BAND_MAX            = 10f;
        public const float RANGED_BAND_MIN          = 10f;   // m, 원거리 거리 띠
        public const float RANGED_BAND_MAX          = 14f;
        public const int   MARKER_MAX_ENEMIES       = 3;     // 남은 출현 예약이 없을 때 위치 표시를 켜는 생존 적 수 상한

        private const float RESERVE_SPACING        = 1f;      // m, 같은 순간 예약 위치끼리 선호하는 최소 간격
        private const float OVERLAP_PENALTY        = 1000f;   // 후보가 이것뿐이면 겹쳐도 내보낸다
        private const float MOVE_EPSILON           = 0.0005f; // m
        private const int   PLAYER_BUFFER_CAPACITY = 16;

        /****************************************
        *                Types
        ****************************************/

        private sealed class WavePlan
        {
            public string[]          SourceIds;
            public EnemyDefinition[] Definitions;
            public int               Total;
        }

        private sealed class ActiveEnemy
        {
            public EnemyServer       Enemy;
            public NetworkIdentity   Identity;
            public MeleeEnemyBrain   Melee;
            public ChargerEnemyBrain Charger;
            public RangedEnemyBrain  Ranged;
            public string            SourceId;
            public EnemyDefinition   Definition;
            public EEnemyState       Behavior;
            public bool              TelegraphOn;
            public double            TelegraphStart;
        }

        private struct Reservation
        {
            public int     PlanIndex;
            public Vector3 Position;
            public double  SpawnAt;
            public int     PreviewId;
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly WaveConfigSO            _waves;
        private readonly WaveSessionDependencies _deps;
        private readonly WavePlan[]              _plans;
        private readonly List<ActiveEnemy>       _active;
        private readonly List<ActiveEnemy>       _defeated = new List<ActiveEnemy>(PLAYER_BUFFER_CAPACITY);
        private readonly List<Reservation>       _reserved;
        private readonly List<ICombatTarget>     _players = new List<ICombatTarget>(PLAYER_BUFFER_CAPACITY);
        private readonly List<ICombatTarget>     _participants = new List<ICombatTarget>(PLAYER_BUFFER_CAPACITY);
        private readonly EnemyCrowd              _crowd   = new EnemyCrowd();
        private readonly IEnemyPresentationHost  _presentation;
        private readonly BossProjectileManager   _projectiles;
        private readonly BossDamageGate          _gate;
        private readonly EnemyTelegraphLimiter   _limiter;
        private readonly float                   _enemyHealthScale;
        private readonly int                     _maxActive;

        private IWaveSessionListener _listener;
        private bool                 _running;
        private bool                 _finished;
        private bool                 _waveActive;
        private bool                 _awaitingArrival;
        private bool                 _arrivalPointValid;
        private bool                 _warnedPassive;
        private bool                 _warnedBlocked;
        private int                  _waveIndex;
        private int                  _pointCount;
        private int                  _nextSpawn;
        private int                  _spawnAttempt;
        private int                  _defeatedInWave;
        private int                  _defeatedTotal;
        private int                  _clearedWaves;
        private double               _beginTime;
        private double               _nextReserveTime;
        private double               _blockedSince;
        private double               _elapsed;
        private Vector3              _arrivalPoint;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsRunning  => (_running) && (!_finished);
        public bool IsFinished => _finished;

        public WaveProgressSnapshot Progress
        {
            get
            {
                int waveIndex = Mathf.Min(_waveIndex, _plans.Length - 1);
                return new WaveProgressSnapshot(waveIndex, _plans.Length, _plans[waveIndex].Total - _defeatedInWave, _elapsed);
            }
        }

        /// <summary> 지금 내보내 둔 적 수 </summary>
        public int ActiveCount => _active.Count;

        /// <summary> 미리보기 중이라 칸만 잡아 둔 출현 수 </summary>
        public int ReservedCount => _reserved.Count;

        /// <summary> 웨이브를 모두 처치하고 다음 구역 도착을 기다리는 중인지 </summary>
        public bool IsAwaitingArrival => _awaitingArrival;

        /// <summary> 출현 지점이 막힌 채 안내 기준 시간을 넘겼는지 </summary>
        public bool IsSpawnBlockedNotice => (_waveActive) && (_warnedBlocked);

        /// <summary> 남은 출현 예약이 없고 생존 적이 소수라 위치, 방향 표시를 켜야 하는지 </summary>
        public bool ShowRemainingMarker => (_waveActive) && (_nextSpawn >= _plans[_waveIndex].Total) && (_reserved.Count == 0)
            && (_active.Count >= 1) && (_active.Count <= MARKER_MAX_ENEMIES);

        /****************************************
        *                Methods
        ****************************************/

        private WaveSession(in WaveSessionContext context, WaveSessionDependencies deps, WavePlan[] plans)
        {
            _waves            = context.Waves;
            _deps             = deps;
            _plans            = plans;
            _enemyHealthScale = context.EnemyHealthScale;
            _maxActive        = Mathf.Max(1, context.Waves.MaxActiveEnemies);
            _active           = new List<ActiveEnemy>(_maxActive);
            _reserved         = new List<Reservation>(_maxActive);
            _blockedSince     = -1.0;
            _presentation     = deps.Objects as IEnemyPresentationHost;
            _projectiles      = new BossProjectileManager(deps.Registry, deps.Walls, _presentation?.CreateProjectileListener());
            _gate             = new BossDamageGate(deps.Resolver);
            _limiter          = new EnemyTelegraphLimiter(deps.Enemies.MaxCloseTelegraphsPerTarget,
                deps.Enemies.MaxProjectileTelegraphsPerTarget);
        }

        /// <summary> 배치 계획을 만들 수 없으면(적 정의 누락, 묶음 합계 불일치) 거짓과 사유 </summary>
        public static bool TryCreate(in WaveSessionContext context, WaveSessionDependencies deps, out WaveSession session,
            out string error)
        {
            session = null;
            if ((context.Waves == null) || (context.Waves.Waves == null) || (context.Waves.Waves.Count == 0))
            {
                error = "웨이브 정의가 없다";
                return false;
            }

            if ((deps == null) || (deps.Registry == null) || (deps.Resolver == null) || (deps.Objects == null)
                || (deps.Spawns == null) || (deps.Enemies == null) || (deps.Movement == null))
            {
                error = "웨이브 협력 객체가 비어 있다";
                return false;
            }

            if ((!(context.EnemyHealthScale > 0f)) || (float.IsInfinity(context.EnemyHealthScale)))
            {
                error = "적 체력 배율이 0 이하다";
                return false;
            }

            if ((!(deps.ReferenceAttack > 0f)) || (!(deps.ReferenceHealth > 0f)))
            {
                error = "지역 기준 공격력이나 체력이 0 이하다";
                return false;
            }

            var plans = new WavePlan[context.Waves.Waves.Count];
            for (int w = 0; w < plans.Length; ++w)
            {
                plans[w] = BuildPlan(context.Waves, w, deps.Enemies, out error);
                if (plans[w] == null)
                {
                    return false;
                }
            }

            session = new WaveSession(in context, deps, plans);
            error   = string.Empty;
            return true;
        }

        private static WavePlan BuildPlan(WaveConfigSO waves, int waveIndex, EnemyCatalogSO enemies, out string error)
        {
            WaveConfigSO.WaveDefinition wave  = waves.Waves[waveIndex];
            int                         total = wave.CountEnemies();

            var plan = new WavePlan
            {
                SourceIds   = new string[total],
                Definitions = new EnemyDefinition[total],
                Total       = total
            };

            int index = 0;
            for (int e = 0; e < wave.Enemies.Count; ++e)
            {
                WaveConfigSO.WaveEnemyEntry entry = wave.Enemies[e];
                if (entry == null)
                {
                    continue;
                }

                if (!enemies.TryGetEnemy(entry.EnemyId, out EnemyDefinition definition))
                {
                    error = $"{wave.DefinitionId}: 적 정의가 없다, enemyId={entry.EnemyId}";
                    return null;
                }

                for (int c = 0; c < entry.Count; ++c)
                {
                    plan.Definitions[index] = definition;
                    plan.SourceIds[index]   = waves.GetSourceId(waveIndex, index);
                    ++index;
                }
            }

            // 묶음 시각은 더 이상 출현 시각에 쓰지 않는다. 데이터 합계가 예정 수와 맞는지만 확인한다
            int grouped = 0;
            for (int g = 0; g < wave.Groups.Count; ++g)
            {
                if (wave.Groups[g] != null)
                {
                    grouped += wave.Groups[g].Count;
                }
            }

            if (grouped != total)
            {
                error = $"{wave.DefinitionId}: 묶음 합계가 예정 수와 다르다";
                return null;
            }

            error = string.Empty;
            return plan;
        }

        public void SetListener(IWaveSessionListener listener)
        {
            _listener = listener;
        }

        /// <summary> 도착을 기다리는 다음 웨이브 시작 지점, 기다리는 중이 아니거나 지점이 없으면 거짓 </summary>
        public bool TryGetArrivalPoint(out Vector3 point)
        {
            point = _arrivalPoint;
            return (_awaitingArrival) && (_arrivalPointValid);
        }

        public void Begin(double serverTime)
        {
            if ((_running) || (_finished))
            {
                return;
            }

            _running   = true;
            _beginTime = serverTime;
            _elapsed   = 0.0;
            _gate.Open();
            StartWave(serverTime);
        }

        public void Tick(double serverTime)
        {
            if (!IsRunning)
            {
                return;
            }

            _elapsed = serverTime - _beginTime;

            CollectDefeats();
            TickBodies(serverTime);
            _projectiles.Tick(serverTime);

            if ((_waveActive) && (_defeatedInWave >= _plans[_waveIndex].Total))
            {
                ClearWave();
                if (!IsRunning)
                {
                    return;
                }
            }

            if ((_awaitingArrival) && (HasArrivedParticipant()))
            {
                _awaitingArrival = false;
                StartWave(serverTime);
            }

            if (_waveActive)
            {
                ReleaseSpawns(serverTime);
            }

            if (_elapsed >= _waves.TimeLimitSeconds)
            {
                Finish(ESessionEndReason.Timeout);
            }
        }

        public void Abort()
        {
            if (_finished)
            {
                return;
            }

            _running  = false;
            _finished = true;
            Cleanup();
        }

        private void StartWave(double serverTime)
        {
            _waveActive      = true;
            _nextSpawn       = 0;
            _spawnAttempt    = 0;
            _defeatedInWave  = 0;
            _nextReserveTime = serverTime;
            _blockedSince    = -1.0;
            _warnedBlocked   = false;
            DropReservations();
            _pointCount = _deps.Spawns.GetPointCount(_waveIndex);
            if (_pointCount <= 0)
            {
                Debug.LogError($"[Wave] 출현 지점이 없어 적을 내보내지 못한다, wave={_waveIndex}");
            }
        }

        // 죽은 몸체는 레지스트리와 무리, 네트워크 개체에서 모두 내린 뒤 배치 순서대로 통지한다. 통지 중 듣는 쪽이 다시 들어와도 목록이 일관된다
        private void CollectDefeats()
        {
            int write = 0;
            _defeated.Clear();
            for (int read = 0; read < _active.Count; ++read)
            {
                ActiveEnemy active = _active[read];
                if (active.Enemy.LifeState == EParticipantLifeState.Alive)
                {
                    _active[write++] = active;
                }
                else
                {
                    _defeated.Add(active);
                }
            }

            if (_defeated.Count == 0)
            {
                return;
            }

            _active.RemoveRange(write, _active.Count - write);

            int   count     = _defeated.Count;
            int   waveIndex = _waveIndex;
            var   ids       = new int[count];
            var   sources   = new string[count];
            for (int i = 0; i < count; ++i)
            {
                ids[i]     = _defeated[i].Enemy.CombatId;
                sources[i] = _defeated[i].SourceId;
                Release(_defeated[i]);
                ++_defeatedInWave;
                ++_defeatedTotal;
            }

            _defeated.Clear();

            for (int i = 0; i < count; ++i)
            {
                string killer = string.Empty;
                if ((_deps.Killers != null) && (!_deps.Killers.TryGetKiller(ids[i], out killer)))
                {
                    killer = string.Empty;
                }

                _listener?.OnEnemyDefeated(waveIndex, sources[i], killer ?? string.Empty);
            }
        }

        private void TickBodies(double serverTime)
        {
            for (int i = 0; i < _active.Count; ++i)
            {
                ActiveEnemy active = _active[i];
                if ((active.Melee == null) && (active.Charger == null) && (active.Ranged == null))
                {
                    _deps.Objects.Publish(active.Enemy, active.Identity);
                    continue;
                }

                EnemyServer enemy  = active.Enemy;
                Vector3     before = enemy.Position;

                if (active.Melee != null)
                {
                    active.Melee.Tick(serverTime);
                }
                else if (active.Charger != null)
                {
                    active.Charger.Tick(serverTime);
                }
                else
                {
                    active.Ranged.Tick(serverTime);
                }

                ConfineAndSnap(enemy, before);
                _deps.Objects.Publish(enemy, active.Identity);
                SyncPresentation(active, enemy);
            }
        }

        // 상태가 바뀔 때만 행동을 알리고, 예고는 시작 시각이 새로 잡힐 때 한 번만 시작하며 상태를 벗어나면 끝낸다
        private void SyncPresentation(ActiveEnemy active, EnemyServer enemy)
        {
            if ((_presentation == null) || (active.Identity == null))
            {
                return;
            }

            EEnemyState state;
            if (active.Melee != null)
            {
                state = active.Melee.State;
            }
            else if (active.Charger != null)
            {
                state = active.Charger.State;
            }
            else
            {
                state = active.Ranged.State;
            }

            if (state != active.Behavior)
            {
                active.Behavior = state;
                _presentation.SetBehavior(active.Identity, state);
            }

            if (state != EEnemyState.Telegraph)
            {
                EndTelegraph(active);
                return;
            }

            double start = (active.Melee != null) ? active.Melee.TelegraphStartTime
                : (active.Charger != null) ? active.Charger.TelegraphStartTime : active.Ranged.TelegraphStartTime;
            if ((active.TelegraphOn) && (active.TelegraphStart == start))
            {
                return;
            }

            EndTelegraph(active);
            BeginTelegraph(active, enemy, start);
        }

        private void BeginTelegraph(ActiveEnemy active, EnemyServer enemy, double start)
        {
            EnemyDefinition   definition = active.Definition;
            EEnemyAttackShape shape      = definition.AttackShape.Shape;

            if (active.Melee != null)
            {
                MeleeEnemyBrain brain = active.Melee;
                float width = (shape == EEnemyAttackShape.Fan) ? brain.AttackArcAngle : 0f;
                _presentation.BeginTelegraph(active.Identity, shape, enemy.Position, brain.AimDirection, brain.AttackRange, width,
                    start, brain.TelegraphEndTime);
            }
            else if (active.Charger != null)
            {
                ChargerEnemyBrain brain = active.Charger;
                _presentation.BeginTelegraph(active.Identity, shape, brain.DashOrigin, brain.AimDirection, brain.DashLength,
                    brain.DashWidth, start, brain.TelegraphEndTime);
            }
            else
            {
                RangedEnemyBrain brain = active.Ranged;
                _presentation.BeginTelegraph(active.Identity, shape, enemy.Position, brain.AimDirection, definition.AttackShape.Range,
                    definition.AttackShape.Width, start, brain.TelegraphEndTime);
            }

            active.TelegraphOn    = true;
            active.TelegraphStart = start;
        }

        private void EndTelegraph(ActiveEnemy active)
        {
            if (!active.TelegraphOn)
            {
                return;
            }

            active.TelegraphOn = false;
            _presentation?.EndTelegraph(active.Identity);
        }

        // 취소된 예약의 표시를 하나씩 내리고, 남은 표시도 모두 정리한다
        private void DropReservations()
        {
            if (_presentation != null)
            {
                for (int i = 0; i < _reserved.Count; ++i)
                {
                    if (_reserved[i].PreviewId > 0)
                    {
                        _presentation.CancelSpawnPreview(_reserved[i].PreviewId);
                    }
                }

                _presentation.CancelAllSpawnPreviews();
            }

            _reserved.Clear();
        }

        // 근접 두뇌는 벽을 모르고 이동 결과를 그대로 넣는다. 정적 벽 검사가 있으면 수평 이동을 벽 앞에서 끊고 높이를 지면에 맞춘다
        private void ConfineAndSnap(EnemyServer enemy, Vector3 before)
        {
            Vector3 after = enemy.Position;

            if (_deps.Walls != null)
            {
                var flatAfter = new Vector3(after.x, before.y, after.z);
                if ((flatAfter - before).sqrMagnitude > (MOVE_EPSILON * MOVE_EPSILON))
                {
                    Vector3 reached = _deps.Movement.Move(before, flatAfter, enemy.BodyRadius);
                    if ((reached - flatAfter).sqrMagnitude > (MOVE_EPSILON * MOVE_EPSILON))
                    {
                        after = new Vector3(reached.x, after.y, reached.z);
                        enemy.SetPosition(after);
                    }
                }
            }

            if ((_deps.Spawns.TrySnapToGround(_waveIndex, after, out Vector3 grounded))
                && (Mathf.Abs(grounded.y - after.y) > MOVE_EPSILON))
            {
                enemy.SetPosition(new Vector3(after.x, grounded.y, after.z));
            }
        }

        // 마지막 웨이브의 마지막 처치는 이동 없이 곧바로 성공이다. 그 밖에는 다음 구역 지점으로 향하는 대기 상태가 된다
        private void ClearWave()
        {
            ++_clearedWaves;
            _listener?.OnWaveCleared(_waveIndex);

            if (_waveIndex >= (_plans.Length - 1))
            {
                Finish(ESessionEndReason.Success);
                return;
            }

            int cleared = _waveIndex;
            ++_waveIndex;
            _defeatedInWave  = 0;
            _waveActive      = false;
            _awaitingArrival = true;
            DropReservations();

            _arrivalPointValid = _deps.Spawns.TryGetNextWavePoint(cleared, out _arrivalPoint);
            if (!_arrivalPointValid)
            {
                Debug.LogError($"[Wave] 다음 웨이브 시작 지점이 없어 진행이 멈춘다, 제한 시간까지 대기한다, clearedWave={cleared}");
            }
        }

        // 접속 중인 살아 있는 참가자가 지점 수평 반경 안, 같은 보행 층에 있으면 도착이다. 이미 안에 있던 사람도 센다
        private bool HasArrivedParticipant()
        {
            if (!_arrivalPointValid)
            {
                return false;
            }

            _deps.Registry.GetLivingPlayers(_players);
            for (int i = 0; i < _players.Count; ++i)
            {
                ICombatTarget player = _players[i];
                if ((player.LifeState != EParticipantLifeState.Alive) || (player.IsSummon))
                {
                    continue;
                }

                if ((_deps.Connections != null) && (!_deps.Connections.IsConnected(player.CombatId)))
                {
                    continue;
                }

                Vector3 offset = player.Position - _arrivalPoint;
                if ((Mathf.Abs(offset.y) <= ARRIVAL_HEIGHT_TOLERANCE)
                    && (((offset.x * offset.x) + (offset.z * offset.z)) <= (ARRIVAL_RADIUS * ARRIVAL_RADIUS)))
                {
                    return true;
                }
            }

            return false;
        }

        // 만기가 된 예약을 먼저 내보낸 뒤 활성과 예약 합이 상한 미만인 동안 빈 칸을 예약한다
        private void ReleaseSpawns(double serverTime)
        {
            WavePlan plan = _plans[_waveIndex];

            int write = 0;
            for (int read = 0; read < _reserved.Count; ++read)
            {
                Reservation reservation = _reserved[read];
                if ((serverTime >= reservation.SpawnAt) && (TryMaterialize(plan, in reservation)))
                {
                    continue;
                }

                _reserved[write++] = reservation;
            }

            if (write < _reserved.Count)
            {
                _reserved.RemoveRange(write, _reserved.Count - write);
            }

            if ((_pointCount <= 0) || (serverTime < _nextReserveTime))
            {
                return;
            }

            while ((_nextSpawn < plan.Total) && ((_active.Count + _reserved.Count) < _maxActive))
            {
                if (!TryChoosePoint(plan, _nextSpawn, out Vector3 point))
                {
                    NoteBlocked(serverTime);
                    _nextReserveTime = serverTime + SPAWN_RETRY_SECONDS;
                    return;
                }

                _blockedSince  = -1.0;
                _warnedBlocked = false;
                double spawnAt   = serverTime + _waves.SpawnTelegraphSeconds;
                int    previewId = (_presentation != null)
                    ? _presentation.ReserveSpawnPreview(point, plan.Definitions[_nextSpawn].Role, _deps.BodyRadius, serverTime, spawnAt)
                    : 0;
                _reserved.Add(new Reservation
                {
                    PlanIndex = _nextSpawn,
                    Position  = point,
                    SpawnAt   = spawnAt,
                    PreviewId = previewId
                });
                ++_nextSpawn;
            }
        }

        // 예약 뒤 플레이어가 금지 반경 안으로 들어왔으면 지연일 뿐 버리지 않는다
        private bool TryMaterialize(WavePlan plan, in Reservation reservation)
        {
            float exclusion = _waves.SpawnExclusionRadius;
            if (exclusion > 0f)
            {
                _deps.Registry.GetParticipantPlayers(_participants);
                if (IsInsideForbidden(reservation.Position, exclusion))
                {
                    return false;
                }
            }

            if (CreateEnemy(plan, reservation.PlanIndex, reservation.Position))
            {
                if ((_presentation != null) && (reservation.PreviewId > 0))
                {
                    _presentation.MaterializeSpawnPreview(reservation.PreviewId);
                }

                return true;
            }

            ++_spawnAttempt;
            return false;
        }

        private void NoteBlocked(double serverTime)
        {
            if (_blockedSince < 0.0)
            {
                _blockedSince = serverTime;
                return;
            }

            if ((!_warnedBlocked) && ((serverTime - _blockedSince) >= SPAWN_BLOCKED_NOTICE))
            {
                _warnedBlocked = true;
                Debug.LogWarning($"[Wave] 출현 지점이 {SPAWN_BLOCKED_NOTICE}초 이상 막혀 출현을 미룬다, wave={_waveIndex}");
            }
        }

        // 금지 반경(엄격히 초과)을 지키는 후보 중 가장 가까운 생존자 거리가 역할별 띠에 드는 첫 지점을 고른다.
        // 띠에 드는 후보가 없으면 띠에 가장 가까운 후보로 물러난다. 예약 위치와 겹치는 후보는 뺀다
        private bool TryChoosePoint(WavePlan plan, int spawnIndex, out Vector3 point)
        {
            point = default;

            EnemyDefinition definition = plan.Definitions[spawnIndex];
            bool            ranged     = (definition.AttackShape != null) && (definition.AttackShape.Shape == EEnemyAttackShape.Projectile);
            float           bandMin    = ranged ? RANGED_BAND_MIN : NEAR_BAND_MIN;
            float           bandMax    = ranged ? RANGED_BAND_MAX : NEAR_BAND_MAX;
            float           exclusion  = _waves.SpawnExclusionRadius;

            _deps.Registry.GetLivingPlayers(_players);
            _deps.Registry.GetParticipantPlayers(_participants);

            float bestScore = float.MaxValue;
            bool  found     = false;
            for (int probe = 0; probe < _pointCount; ++probe)
            {
                int index = (spawnIndex + _spawnAttempt + probe) % _pointCount;
                if (!_deps.Spawns.TryGetPoint(_waveIndex, index, out Vector3 candidate))
                {
                    continue;
                }

                if ((exclusion > 0f) && (IsInsideForbidden(candidate, exclusion)))
                {
                    continue;
                }

                float nearest = NearestPlayerDistance(candidate);

                float score = 0f;
                if (nearest < bandMin)
                {
                    score = bandMin - nearest;
                }
                else if ((nearest != float.MaxValue) && (nearest > bandMax))
                {
                    score = nearest - bandMax;
                }

                if (OverlapsReservation(candidate))
                {
                    score += OVERLAP_PENALTY;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    point     = candidate;
                    found     = true;
                    if (score <= 0f)
                    {
                        break;
                    }
                }
            }

            return found;
        }

        // 금지는 생존·사망·부활 대기 참가자 전원의 현재 위치(사망자는 사망 위치)가 기준이고, 선호 띠는 생존자만 본다
        private bool IsInsideForbidden(Vector3 point, float exclusion)
        {
            float sqr = exclusion * exclusion;
            for (int i = 0; i < _participants.Count; ++i)
            {
                Vector3 offset = _participants[i].Position - point;
                if (((offset.x * offset.x) + (offset.z * offset.z)) <= sqr)
                {
                    return true;
                }
            }

            return false;
        }

        private float NearestPlayerDistance(Vector3 from)
        {
            float best = float.MaxValue;
            for (int i = 0; i < _players.Count; ++i)
            {
                Vector3 offset = _players[i].Position - from;
                float   dist   = Mathf.Sqrt((offset.x * offset.x) + (offset.z * offset.z));
                if (dist < best)
                {
                    best = dist;
                }
            }

            return best;
        }

        private bool OverlapsReservation(Vector3 candidate)
        {
            for (int i = 0; i < _reserved.Count; ++i)
            {
                Vector3 offset = _reserved[i].Position - candidate;
                if (((offset.x * offset.x) + (offset.z * offset.z)) < (RESERVE_SPACING * RESERVE_SPACING))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CreateEnemy(WavePlan plan, int spawnIndex, Vector3 point)
        {
            EnemyDefinition definition = plan.Definitions[spawnIndex];
            int             combatId   = _deps.Registry.IssueCombatId();
            Vector3         forward    = FaceNearestPlayer(point);

            var enemy = new EnemyServer(definition, combatId, point, forward, _deps.ReferenceAttack, _deps.BodyRadius,
                _enemyHealthScale, _deps.SeparationRadius);

            if (!_deps.Registry.Register(enemy))
            {
                return false;
            }

            _presentation?.DescribeEnemy(combatId, definition.Role);
            if (!_deps.Objects.TrySpawn(enemy, out NetworkIdentity identity))
            {
                _deps.Registry.Unregister(combatId);
                return false;
            }

            _crowd.Add(enemy);

            var active = new ActiveEnemy
            {
                Enemy = enemy, Identity = identity, SourceId = plan.SourceIds[spawnIndex], Definition = definition, Behavior = EEnemyState.Idle
            };
            AttachBrain(active, definition);
            _active.Add(active);
            return true;
        }

        // 부채꼴과 원형(엘리트)은 근접 두뇌, 투사체는 원거리 두뇌, 활성 구간이 있는 돌진은 돌진 두뇌다. 그 밖의 모양만 제자리 몸체로 둔다
        private void AttachBrain(ActiveEnemy active, EnemyDefinition definition)
        {
            EEnemyAttackShape shape = (definition.AttackShape != null) ? definition.AttackShape.Shape : EEnemyAttackShape.Circle;

            if ((shape == EEnemyAttackShape.Fan) || (shape == EEnemyAttackShape.Circle))
            {
                active.Melee = new MeleeEnemyBrain(active.Enemy, definition, _deps.ReferenceHealth, _deps.Registry, _deps.Resolver,
                    MeleeEnemyBrain.PROTOTYPE_RECOVER_SECONDS, _crowd, _deps.Navigation, _limiter, _deps.Walls);
                return;
            }

            if (shape == EEnemyAttackShape.Projectile)
            {
                active.Ranged = new RangedEnemyBrain(active.Enemy, definition, _deps.ReferenceHealth, _deps.Registry, _projectiles,
                    _gate, _deps.Movement, _crowd, _deps.Navigation, _limiter, walls: _deps.Walls);
                return;
            }

            if ((shape == EEnemyAttackShape.Dash) && (definition.AttackActiveSeconds > 0f))
            {
                active.Charger = new ChargerEnemyBrain(active.Enemy, definition, _deps.ReferenceHealth, _deps.Registry, _deps.Resolver,
                    _deps.Movement, ChargerEnemyBrain.PROTOTYPE_RECOVER_SECONDS, _crowd, _deps.Navigation, _limiter, _deps.Walls);
                return;
            }

            if (!_warnedPassive)
            {
                _warnedPassive = true;
                Debug.LogWarning($"[Wave] 공격 모양 {shape}에는 서버 두뇌가 없어 제자리 몸체로 세운다, enemy={definition.DefinitionId}");
            }
        }

        private Vector3 FaceNearestPlayer(Vector3 from)
        {
            _deps.Registry.GetLivingPlayers(_players);

            float   best    = float.MaxValue;
            Vector3 forward = Vector3.forward;
            for (int i = 0; i < _players.Count; ++i)
            {
                Vector3 offset = _players[i].Position - from;
                offset.y = 0f;
                float sqr = offset.sqrMagnitude;
                if ((sqr < best) && (sqr > 0f))
                {
                    best    = sqr;
                    forward = offset;
                }
            }

            return forward;
        }

        /// <summary> 남은 적의 위치를 results에 채운다, ShowRemainingMarker가 거짓이면 비운다 </summary>
        public int CopyRemainingPositions(List<Vector3> results)
        {
            results.Clear();
            if (!ShowRemainingMarker)
            {
                return 0;
            }

            for (int i = 0; i < _active.Count; ++i)
            {
                results.Add(_active[i].Enemy.Position);
            }

            return results.Count;
        }

        private void Finish(ESessionEndReason reason)
        {
            if (_finished)
            {
                return;
            }

            _running  = false;
            _finished = true;
            Cleanup();

            _listener?.OnWaveSessionEnded(new WaveSessionOutcome(reason, _clearedWaves, _defeatedTotal, _elapsed));
        }

        private void Cleanup()
        {
            _gate.CloseForGood();
            _projectiles.Cancel(EBossInterruptReason.CombatEnd, CombatIds.NONE);
            DropReservations();
            _waveActive      = false;
            _awaitingArrival = false;

            for (int i = 0; i < _active.Count; ++i)
            {
                Release(_active[i]);
            }

            _active.Clear();
        }

        private void Release(ActiveEnemy active)
        {
            if ((_presentation != null) && (active.Identity != null))
            {
                EndTelegraph(active);
                if ((active.Enemy.LifeState != EParticipantLifeState.Alive) && (active.Behavior != EEnemyState.Dead))
                {
                    active.Behavior = EEnemyState.Dead;
                    _presentation.SetBehavior(active.Identity, EEnemyState.Dead);
                }
            }

            _limiter.Release(active.Enemy.CombatId);
            _deps.Registry.Unregister(active.Enemy.CombatId);
            _crowd.Remove(active.Enemy);
            if (active.Identity != null)
            {
                _deps.Objects.Despawn(active.Identity);
            }
        }
    }
}
