// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    /// <summary> 서버 여우불 봉인 사건만 표현한다. 핵 파괴와 피해 판정은 만들거나 추측하지 않는다. </summary>
    public sealed class WolbaekFoxfireSealView : MonoBehaviour
    {
        private const float SHIELD_HEIGHT = 1.4f;
        private const float SHIELD_SCALE = 0.9f;
        private const float MOTION_BLEND_SECONDS = 0.05f;
        private const float SWEEP_HEIGHT = 2f;
        private const float SWEEP_LIFE_SECONDS = 4f;
        private const float SWEEP_RANGE_HEIGHT = 0.05f;
        private const int SWEEP_SEGMENTS = 24;
        private const string DEFENSE_STATE = "defense_inplace";
        private const string SUCCESS_STATE = "down_groggy02_loop_inplace";
        private const string IDLE_STATE = "Idle";

        private static readonly Color SweepRangeColor = new Color(1f, 0.1f, 0.1f, 0.3f);

        [SerializeField] private GameObject _shieldPrefab;
        [SerializeField] private WolbaekSealCorePlayer _corePrefab;
        [SerializeField] private WolbaekSealDropPlayer _dropPrefab;
        [SerializeField] private GameObject _sweepPrefab;

        private readonly Dictionary<int, Transform> _anchors = new Dictionary<int, Transform>();
        private readonly Dictionary<int, GameObject> _sweepRanges = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, WolbaekSealSweep> _pendingSweeps = new Dictionary<int, WolbaekSealSweep>();
        private readonly HashSet<int> _resolvedSweeps = new HashSet<int>();
        private readonly List<GameObject> _sweepEffects = new List<GameObject>();
        private Func<double> _clock;
        private BossView _bossView;
        private WolbaekSealStart _start;
        private WolbaekSealTurn _turn;
        private WolbaekSealCorePlayer[] _cores = Array.Empty<WolbaekSealCorePlayer>();
        private WolbaekSealDropPlayer[] _drops = Array.Empty<WolbaekSealDropPlayer>();
        private uint[] _corePlayback = Array.Empty<uint>();
        private uint[] _dropPlayback = Array.Empty<uint>();
        private Vector3[] _dropPositions = Array.Empty<Vector3>();
        private bool[] _hasServerDropPosition = Array.Empty<bool>();
        private bool[] _detached = Array.Empty<bool>();
        private GameObject _shield;
        private int _castId;
        private int _endedCastId;
        private int _turnIndex = -1;
        private int _successCount;
        private ulong _sequence;
        private double _counterEndServerTime;
        private bool _active;
        private bool _tracking;
        private bool _locked;
        private bool _turnResolved;
        private bool _counter;

        public bool IsActive => _active;
        public bool IsCounter => _counter;
        public int TurnIndex => _turnIndex;
        public int SuccessCount => _successCount;
        public int VisibleCoreCount => CountRunning(_cores);
        public int VisibleDropCount => CountRunning(_drops);
        public bool HasPendingSweep => _sweepRanges.Count > 0;
        public double CounterEndServerTime => _counterEndServerTime;

        private void Awake()
        {
            _bossView = GetComponent<BossView>();
        }

        private void Update()
        {
            double now = Now();
            if (_shield != null)
            {
                _shield.transform.position = transform.position + (Vector3.up * SHIELD_HEIGHT);
            }
            if (_active && (_turnIndex >= 0))
            {
                if (_tracking && (!_locked) && (!_turnResolved))
                {
                    TrackAnchors(now);
                }
                TickTurn(now);
            }
            for (int i = _sweepEffects.Count - 1; i >= 0; --i)
            {
                if (_sweepEffects[i] == null) _sweepEffects.RemoveAt(i);
            }
        }

        private void OnDisable() { Clear(); }
        private void OnDestroy() { Clear(); }

        public void SetServerClock(Func<double> clock) { _clock = clock; }

        public void SetMemberAnchor(int combatId, Transform anchor)
        {
            if (anchor == null) _anchors.Remove(combatId);
            else _anchors[combatId] = anchor;
        }

        public bool TryGetDropTarget(int index, out Vector3 position)
        {
            if ((index < 0) || (index >= _drops.Length) || (_drops[index] == null) || (!_drops[index].IsRunning))
            {
                position = default;
                return false;
            }
            position = _drops[index].CurrentSnapshot.WorldTarget;
            return true;
        }

        public bool StartSeal(in WolbaekSealStart start, double now)
        {
            if (!ValidStart(in start) || (start.CastInstanceId <= _endedCastId)
                || ((_active || _counter) && (start.CastInstanceId <= _castId))) return false;
            ClearVisuals();
            _start = start;
            _castId = start.CastInstanceId;
            _active = true;
            _successCount = 0;
            _sequence = 1;
            BeginShield();
            PlayMotion(DEFENSE_STATE);
            return true;
        }

        public void TurnStarted(int castId, in WolbaekSealTurn turn, double now)
        {
            if (!Matches(castId) || !ValidTurn(in turn)) return;
            StartTurn(in turn, null, null, now);
        }

        public void TrackingStarted(int castId, int turnIndex, double serverTime, double now)
        {
            if (!MatchesTurn(castId, turnIndex) || _turnResolved || (serverTime > now)) return;
            _tracking = true;
            OpenCores(serverTime, now);
            TrackAnchors(now);
        }

        public void FoxfireDetached(int castId, int turnIndex, int foxfireIndex, double serverTime,
            Vector3 position, double now)
        {
            if (!MatchesTurn(castId, turnIndex) || _turnResolved || !ValidIndex(foxfireIndex, _drops.Length)
                || (serverTime > now) || !Finite(position)) return;
            _detached[foxfireIndex] = true;
            _dropPositions[foxfireIndex] = position;
            _hasServerDropPosition[foxfireIndex] = true;
            EnsureDrop(foxfireIndex, WolbaekSealDropPlayer.EPhase.Tracking, _turn.StartServerTime, now);
        }

        public void FoxfiresLocked(int castId, int turnIndex, double serverTime,
            IReadOnlyList<Vector3> centers, double now)
        {
            if (!MatchesTurn(castId, turnIndex) || _turnResolved || (centers == null)
                || (centers.Count != _drops.Length) || (serverTime > now)) return;
            for (int i = 0; i < centers.Count; ++i)
            {
                if (!Finite(centers[i])) return;
            }
            _locked = true;
            _tracking = false;
            for (int i = 0; i < centers.Count; ++i)
            {
                _dropPositions[i] = centers[i];
                _hasServerDropPosition[i] = true;
                EnsureDrop(i, WolbaekSealDropPlayer.EPhase.Locked, serverTime, now);
            }
        }

        public void SweepTelegraphed(int castId, in WolbaekSealSweep sweep, double now)
        {
            if (!MatchesTurn(castId, sweep.TurnIndex) || !ValidSweep(in sweep)
                || (sweep.StrikeServerTime <= now) || _resolvedSweeps.Contains(SweepKey(sweep.TurnIndex, sweep.SweepIndex))) return;
            int key = SweepKey(sweep.TurnIndex, sweep.SweepIndex);
            RemoveSweepRange(key);
            _sweepRanges[key] = CreateSweepRange(in sweep);
            _pendingSweeps[key] = sweep;
        }

        public void SweepResolved(int castId, int turnIndex, int sweepIndex, double serverTime)
        {
            if (!MatchesTurn(castId, turnIndex) || (sweepIndex < 0)) return;
            int key = SweepKey(turnIndex, sweepIndex);
            if (!_resolvedSweeps.Add(key)) return;
            if (_pendingSweeps.TryGetValue(key, out WolbaekSealSweep sweep))
            {
                SpawnSweep(in sweep);
            }
            RemoveSweepRange(key);
        }

        public void TurnResolved(int castId, int turnIndex, double serverTime, int successCount, double now)
        {
            if (!MatchesTurn(castId, turnIndex) || _turnResolved || (serverTime > now)) return;
            _turnResolved = true;
            _tracking = false;
            _locked = true;
            _successCount = Math.Max(_successCount, successCount);
            ClearSweepRanges();
            for (int i = 0; i < _drops.Length; ++i)
            {
                if ((_drops[i] == null) || (_dropPlayback[i] == 0)) continue;
                ++_sequence;
                WolbaekSealDropPlayer.Snapshot drop = DropSnapshot(i, WolbaekSealDropPlayer.EPhase.Resolved,
                    serverTime, now);
                _drops[i].Reconcile(_dropPlayback[i], drop, now);
            }
            // 개별 파괴 핵 ID가 없어 Destroyed를 추측하지 않는다. 모든 핵은 일반 만료로 정리한다.
            for (int i = 0; i < _cores.Length; ++i)
            {
                if ((_cores[i] == null) || (_corePlayback[i] == 0)) continue;
                ++_sequence;
                WolbaekSealCorePlayer.Snapshot core = CoreSnapshot(i, WolbaekSealCorePlayer.EPhase.Expired,
                    serverTime, now);
                _cores[i].Reconcile(_corePlayback[i], core, now);
            }
        }

        public void Succeeded(int castId, double serverTime, double counterEndServerTime, double now)
        {
            if (!Matches(castId) || (serverTime > now) || (counterEndServerTime < serverTime)) return;
            _endedCastId = Math.Max(_endedCastId, castId);
            ClearTurn();
            StopShield();
            _active = false;
            _counter = true;
            _counterEndServerTime = counterEndServerTime;
            PlayMotion(SUCCESS_STATE);
        }

        public void CounterEnded(int castId)
        {
            if (!_counter || (castId != _castId)) return;
            _counter = false;
            _counterEndServerTime = 0d;
            PlayMotion(IDLE_STATE);
        }

        public bool Restore(in WolbaekSealSnapshot snapshot, double now)
        {
            WolbaekSealStart start = snapshot.Start;
            WolbaekSealTurn turn = snapshot.Turn;
            if (snapshot.State == EWolbaekSealState.Counter)
            {
                if (!ValidStart(in start) || (start.CastInstanceId <= _endedCastId)) return false;
                ClearVisuals();
                _start = start;
                _castId = start.CastInstanceId;
                _successCount = snapshot.SuccessCount;
                _counter = true;
                _counterEndServerTime = snapshot.CounterEndServerTime;
                PlayMotion(SUCCESS_STATE);
                return true;
            }
            if ((snapshot.State != EWolbaekSealState.Active) || !ValidStart(in start)
                || !ValidTurn(in turn))
            {
                _endedCastId = Math.Max(_endedCastId, start.CastInstanceId);
                Clear();
                return false;
            }
            ClearVisuals();
            _start = start;
            _castId = start.CastInstanceId;
            _active = true;
            _successCount = snapshot.SuccessCount;
            _sequence = 1;
            BeginShield();
            StartTurn(in turn, snapshot.FoxfirePositions, snapshot.FoxfireDetached, now);
            if (snapshot.Step == EWolbaekSealStep.Cleanup)
            {
                // 판정이 지난 일회성 낙하와 핵 파쇄는 다시 재생하지 않는다.
                ClearTurn();
                return true;
            }
            if (snapshot.Step != EWolbaekSealStep.Prepare)
            {
                OpenCores(turn.TrackStartServerTime, now);
            }
            _tracking = snapshot.Step == EWolbaekSealStep.Track;
            _locked = snapshot.FoxfiresLocked || (snapshot.Step == EWolbaekSealStep.Escape);
            if (_locked)
            {
                for (int i = 0; i < _drops.Length; ++i)
                    EnsureDrop(i, WolbaekSealDropPlayer.EPhase.Locked, turn.LockServerTime, now);
            }
            if (snapshot.HasPendingSweep && (snapshot.PendingSweep.StrikeServerTime > now))
            {
                WolbaekSealSweep sweep = snapshot.PendingSweep;
                SweepTelegraphed(_castId, in sweep, now);
            }
            return true;
        }

        public void ClearCast(int castId)
        {
            if (((!_active) && (!_counter)) || (castId != _castId)) return;
            _endedCastId = Math.Max(_endedCastId, castId);
            ClearVisuals();
            PlayMotion(IDLE_STATE);
        }

        public void Clear()
        {
            if (_active || _counter) _endedCastId = Math.Max(_endedCastId, _castId);
            bool hadMotion = _active || _counter;
            ClearVisuals();
            _anchors.Clear();
            if (hadMotion) PlayMotion(IDLE_STATE);
        }

        public void ResetNetworkSession()
        {
            bool hadMotion = _active || _counter;
            ClearVisuals();
            _endedCastId = 0;
            _castId = 0;
            _anchors.Clear();
            if (hadMotion) PlayMotion(IDLE_STATE);
        }

        private void StartTurn(in WolbaekSealTurn turn, IReadOnlyList<Vector3> positions,
            IReadOnlyList<bool> detached, double now)
        {
            ClearTurn();
            _turn = turn;
            _turnIndex = turn.Index;
            _tracking = false;
            _locked = false;
            _turnResolved = false;
            int coreCount = turn.Cores.Count;
            int dropCount = turn.Foxfires.Count;
            _cores = new WolbaekSealCorePlayer[coreCount];
            _drops = new WolbaekSealDropPlayer[dropCount];
            _corePlayback = new uint[coreCount];
            _dropPlayback = new uint[dropCount];
            _dropPositions = new Vector3[dropCount];
            _hasServerDropPosition = new bool[dropCount];
            _detached = new bool[dropCount];
            _resolvedSweeps.Clear();
            for (int i = 0; i < coreCount; ++i)
            {
                if (_corePrefab == null) continue;
                _cores[i] = Instantiate(_corePrefab);
                ++_sequence;
                WolbaekSealCorePlayer.Snapshot core = CoreSnapshot(i, WolbaekSealCorePlayer.EPhase.Dormant,
                    turn.StartServerTime, now);
                _corePlayback[i] = _cores[i].Begin(core, now);
            }
            for (int i = 0; i < dropCount; ++i)
            {
                if ((detached != null) && (i < detached.Count)) _detached[i] = detached[i];
                if ((positions != null) && (i < positions.Count) && Finite(positions[i]))
                {
                    _dropPositions[i] = positions[i];
                    _hasServerDropPosition[i] = true;
                    EnsureDrop(i, WolbaekSealDropPlayer.EPhase.Tracking, turn.StartServerTime, now);
                }
                else if (_anchors.TryGetValue(turn.Foxfires[i].TargetCombatId, out Transform anchor) && (anchor != null))
                {
                    _dropPositions[i] = anchor.position;
                    EnsureDrop(i, WolbaekSealDropPlayer.EPhase.Tracking, turn.StartServerTime, now);
                }
            }
        }

        private void TrackAnchors(double now)
        {
            for (int i = 0; i < _drops.Length; ++i)
            {
                if (_detached[i]) continue;
                int targetId = _turn.Foxfires[i].TargetCombatId;
                if (!_anchors.TryGetValue(targetId, out Transform anchor) || (anchor == null))
                {
                    if (!_hasServerDropPosition[i])
                    {
                        if (_drops[i] != null) _drops[i].Stop();
                        _dropPlayback[i] = 0;
                    }
                    continue;
                }
                _dropPositions[i] = anchor.position;
                EnsureDrop(i, WolbaekSealDropPlayer.EPhase.Tracking, _turn.StartServerTime, now);
            }
        }

        private void EnsureDrop(int index, WolbaekSealDropPlayer.EPhase phase, double phaseStartedAt, double now)
        {
            if ((_dropPrefab == null) || !ValidIndex(index, _drops.Length)) return;
            if (_drops[index] == null) _drops[index] = Instantiate(_dropPrefab);
            ++_sequence;
            WolbaekSealDropPlayer.Snapshot snapshot = DropSnapshot(index, phase, phaseStartedAt, now);
            if (_dropPlayback[index] == 0) _dropPlayback[index] = _drops[index].Begin(snapshot, now);
            else _drops[index].Reconcile(_dropPlayback[index], snapshot, now);
        }

        private void OpenCores(double serverTime, double now)
        {
            for (int i = 0; i < _cores.Length; ++i)
            {
                if ((_cores[i] == null) || (_corePlayback[i] == 0)) continue;
                ++_sequence;
                WolbaekSealCorePlayer.Snapshot core = CoreSnapshot(i, WolbaekSealCorePlayer.EPhase.Opened,
                    serverTime, now);
                _cores[i].Reconcile(_corePlayback[i], core, now);
            }
        }

        private void TickTurn(double now)
        {
            for (int i = 0; i < _cores.Length; ++i)
                if ((_cores[i] != null) && (_corePlayback[i] != 0)) _cores[i].Tick(_corePlayback[i], now);
            for (int i = 0; i < _drops.Length; ++i)
                if ((_drops[i] != null) && (_dropPlayback[i] != 0)) _drops[i].Tick(_dropPlayback[i], now);
        }

        private WolbaekSealCorePlayer.Snapshot CoreSnapshot(int index, WolbaekSealCorePlayer.EPhase phase,
            double phaseStartedAt, double now)
        {
            WolbaekSealCore core = _turn.Cores[index];
            return new WolbaekSealCorePlayer.Snapshot((ulong)_castId, _sequence, _turn.Index, core.Index,
                core.Center, _turn.StartServerTime, now, phase, phaseStartedAt);
        }

        private WolbaekSealDropPlayer.Snapshot DropSnapshot(int index, WolbaekSealDropPlayer.EPhase phase,
            double phaseStartedAt, double now)
        {
            return new WolbaekSealDropPlayer.Snapshot((ulong)_castId, _sequence, _turn.Index,
                _turn.Foxfires[index].Index, _dropPositions[index], _start.BlastRadius, _turn.StartServerTime,
                _turn.LockServerTime, _turn.ResolveServerTime, now, phase, phaseStartedAt);
        }

        private void BeginShield()
        {
            if (_shieldPrefab == null) return;
            _shield = Instantiate(_shieldPrefab, transform.position + (Vector3.up * SHIELD_HEIGHT), Quaternion.identity);
            _shield.transform.localScale = _shieldPrefab.transform.localScale * SHIELD_SCALE;
            foreach (ParticleSystem particle in _shield.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particle.main;
                main.loop = true;
            }
        }

        private void StopShield()
        {
            if (_shield != null) Destroy(_shield);
            _shield = null;
        }

        private GameObject CreateSweepRange(in WolbaekSealSweep sweep)
        {
            Vector3[] vertices = new Vector3[SWEEP_SEGMENTS + 2];
            int[] triangles = new int[SWEEP_SEGMENTS * 3];
            for (int i = 0; i <= SWEEP_SEGMENTS; ++i)
            {
                float angle = Mathf.Lerp(-sweep.HalfAngleDegrees, sweep.HalfAngleDegrees,
                    i / (float)SWEEP_SEGMENTS) * Mathf.Deg2Rad;
                vertices[i + 1] = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * sweep.Range;
            }
            for (int i = 0; i < SWEEP_SEGMENTS; ++i)
            {
                triangles[i * 3] = 0;
                triangles[(i * 3) + 1] = i + 1;
                triangles[(i * 3) + 2] = i + 2;
            }
            Mesh mesh = new Mesh { vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            GameObject range = new GameObject("WolbaekSealSweepRange");
            range.transform.SetPositionAndRotation(sweep.Origin + (Vector3.up * SWEEP_RANGE_HEIGHT),
                Quaternion.LookRotation(sweep.Direction, Vector3.up));
            range.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = range.AddComponent<MeshRenderer>();
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) renderer.sharedMaterial = new Material(shader) { color = SweepRangeColor };
            return range;
        }

        private void SpawnSweep(in WolbaekSealSweep sweep)
        {
            if (_sweepPrefab == null) return;
            float angleFromX = Mathf.Atan2(sweep.Direction.z, sweep.Direction.x) * Mathf.Rad2Deg;
            for (int side = -1; side <= 1; side += 2)
            {
                float yaw = -(angleFromX + (side * sweep.HalfAngleDegrees * 0.5f)) + 270f;
                GameObject effect = Instantiate(_sweepPrefab, sweep.Origin + (Vector3.up * SWEEP_HEIGHT),
                    Quaternion.Euler(0f, yaw, 0f));
                effect.transform.localScale = _sweepPrefab.transform.localScale * (sweep.Range * 0.5f);
                _sweepEffects.Add(effect);
                Destroy(effect, SWEEP_LIFE_SECONDS);
            }
        }

        private void RemoveSweepRange(int key)
        {
            if (!_sweepRanges.TryGetValue(key, out GameObject range)) return;
            if (range != null)
            {
                MeshFilter filter = range.GetComponent<MeshFilter>();
                MeshRenderer renderer = range.GetComponent<MeshRenderer>();
                if (filter != null) Destroy(filter.sharedMesh);
                if ((renderer != null) && (renderer.sharedMaterial != null)) Destroy(renderer.sharedMaterial);
                Destroy(range);
            }
            _sweepRanges.Remove(key);
            _pendingSweeps.Remove(key);
        }

        private void ClearSweepRanges()
        {
            int[] keys = new int[_sweepRanges.Count];
            _sweepRanges.Keys.CopyTo(keys, 0);
            for (int i = 0; i < keys.Length; ++i) RemoveSweepRange(keys[i]);
        }

        private void ClearTurn()
        {
            ClearSweepRanges();
            for (int i = 0; i < _cores.Length; ++i)
            {
                if (_cores[i] == null) continue;
                _cores[i].Stop();
                Destroy(_cores[i].gameObject);
            }
            for (int i = 0; i < _drops.Length; ++i)
            {
                if (_drops[i] == null) continue;
                _drops[i].Stop();
                Destroy(_drops[i].gameObject);
            }
            _cores = Array.Empty<WolbaekSealCorePlayer>();
            _drops = Array.Empty<WolbaekSealDropPlayer>();
            _corePlayback = Array.Empty<uint>();
            _dropPlayback = Array.Empty<uint>();
            _dropPositions = Array.Empty<Vector3>();
            _hasServerDropPosition = Array.Empty<bool>();
            _detached = Array.Empty<bool>();
            _turnIndex = -1;
            _tracking = false;
            _locked = false;
            _turnResolved = false;
            _resolvedSweeps.Clear();
            _pendingSweeps.Clear();
        }

        private void ClearVisuals()
        {
            _active = false;
            _counter = false;
            _counterEndServerTime = 0d;
            ClearTurn();
            StopShield();
            for (int i = 0; i < _sweepEffects.Count; ++i)
                if (_sweepEffects[i] != null) Destroy(_sweepEffects[i]);
            _sweepEffects.Clear();
        }

        private void PlayMotion(string state)
        {
            if (_bossView == null) _bossView = GetComponent<BossView>();
            if (_bossView != null) _bossView.PlayGimmickMotion(state, 1f, MOTION_BLEND_SECONDS);
        }

        private bool Matches(int castId) { return _active && (castId == _castId); }
        private bool MatchesTurn(int castId, int turnIndex)
        {
            return Matches(castId) && (turnIndex == _turnIndex);
        }
        private double Now() { return _clock != null ? _clock() : Time.timeAsDouble; }

        private static int SweepKey(int turnIndex, int sweepIndex)
        {
            return unchecked((turnIndex * 397) ^ sweepIndex);
        }

        private static int CountRunning<T>(T[] players) where T : Component
        {
            int count = 0;
            for (int i = 0; i < players.Length; ++i)
            {
                if (players[i] == null) continue;
                if ((players[i] is WolbaekSealCorePlayer core) && core.IsRunning) ++count;
                else if ((players[i] is WolbaekSealDropPlayer drop) && drop.IsRunning) ++count;
            }
            return count;
        }

        private static bool ValidStart(in WolbaekSealStart start)
        {
            return (start.CastInstanceId > 0) && (start.RequiredSuccesses > 0)
                && (start.LimitEndServerTime >= start.StartServerTime) && (start.PrepareSeconds > 0f)
                && (start.TrackSeconds > 0f) && (start.EscapeSeconds > 0f) && (start.CleanupSeconds >= 0f)
                && (start.BlastRadius > 0f) && (start.CoreHitRadius > 0f) && Finite(start.ArenaCenter);
        }

        private static bool ValidTurn(in WolbaekSealTurn turn)
        {
            if ((turn.Index < 0) || (turn.Cores == null) || (turn.Foxfires == null)
                || (turn.Cores.Count == 0) || (turn.Foxfires.Count == 0)
                || (turn.StartServerTime > turn.TrackStartServerTime)
                || (turn.TrackStartServerTime > turn.LockServerTime)
                || (turn.LockServerTime > turn.ResolveServerTime)
                || (turn.ResolveServerTime > turn.EndServerTime)) return false;
            for (int i = 0; i < turn.Cores.Count; ++i)
                if ((turn.Cores[i].Index < 0) || !Finite(turn.Cores[i].Center)) return false;
            for (int i = 0; i < turn.Foxfires.Count; ++i)
                if ((turn.Foxfires[i].Index < 0) || (turn.Foxfires[i].TargetCombatId <= 0)) return false;
            return true;
        }

        private static bool ValidSweep(in WolbaekSealSweep sweep)
        {
            Vector3 direction = sweep.Direction;
            direction.y = 0f;
            return (sweep.SweepIndex >= 0) && (sweep.TelegraphServerTime <= sweep.StrikeServerTime)
                && Finite(sweep.Origin) && Finite(direction) && (direction.sqrMagnitude > 0.0001f)
                && (sweep.Range > 0f) && (sweep.HalfAngleDegrees > 0f) && (sweep.HalfAngleDegrees < 180f);
        }

        private static bool ValidIndex(int index, int count) { return (index >= 0) && (index < count); }
        private static bool Finite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }
    }
}
