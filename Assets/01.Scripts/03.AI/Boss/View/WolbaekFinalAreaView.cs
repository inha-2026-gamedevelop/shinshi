// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    /// <summary> 서버의 최후의 영역 사건과 스냅샷만 표현한다. 바닥 삭제와 피해 판정은 소유하지 않는다. </summary>
    public sealed class WolbaekFinalAreaView : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float SHIELD_HEIGHT          = 1.4f;
        private const float SHIELD_SCALE           = 0.9f;
        private const float FLIGHT_HEIGHT          = 1.2f;
        private const float FLOOR_EFFECT_HEIGHT    = 0.1f;
        private const float FLOOR_EFFECT_BASE_SIZE = 8f;
        private const float FLOOR_EFFECT_SCALE     = 3.1f;
        private const float LOCK_EFFECT_SCALE      = 0.77f;
        private const float ONE_SHOT_LIFE_SECONDS  = 4f;
        private const float RING_HEIGHT            = 0.06f;
        private const float RING_HALF_WIDTH        = 0.18f;
        private const float MOTION_BLEND_SECONDS   = 0.05f;
        private const float GROGGY_ENTER_BLEND     = 0.3f;  // 시험 씬 GimmickAttackCue 그로기 진입 섞기와 같은 정규화 값
        private const float SHIELD_FADE_SECONDS    = 2.5f;  // 정상 성공 때 보호막의 남은 파티클이 투명해지며 사라지는 시간, 시험 씬 GimmickShieldCue와 같다
        private const int   RING_SEGMENTS          = 64;
        private const string GROGGY_STATE          = "down_groggy02_loop_inplace";
        private const string IDLE_STATE            = "Idle";

        private static readonly Color G_TelegraphColor = new Color(0.1f, 0.45f, 1f, 0.72f);

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private GameObject _shieldPrefab;
        [SerializeField] private WolbaekFoxfirePlayer _foxfirePrefab;
        [SerializeField] private GameObject _floorTelegraphPrefab;
        [SerializeField] private GameObject _floorCollapsePrefab;
        [SerializeField] private GameObject _redLockPrefab;

        private readonly Dictionary<int, Transform> _anchors = new Dictionary<int, Transform>();
        private readonly List<GameObject> _oneShots = new List<GameObject>();
        private Func<double> _clock;
        private readonly WolbaekEffectTails _tails = new WolbaekEffectTails();
        private readonly WolbaekServerEventGate _gate = new WolbaekServerEventGate(); // 시계 오차로 서버 시각이 아직 오지 않은 사건을 그 시각까지 보류한다
        private BossView _bossView;
        private WolbaekFinalStart _start;
        private WolbaekFinalBundle _bundle;
        private WolbaekFoxfirePlayer[] _yellows = Array.Empty<WolbaekFoxfirePlayer>();
        private WolbaekFoxfirePlayer[] _reds = Array.Empty<WolbaekFoxfirePlayer>();
        private uint[] _yellowPlayback = Array.Empty<uint>();
        private uint[] _redPlayback = Array.Empty<uint>();
        private Vector3[] _redPositions = Array.Empty<Vector3>();
        private bool[] _redDetached = Array.Empty<bool>();
        private GameObject _shield;
        private GameObject _floorTelegraph;
        private GameObject _floorEffect;
        private Mesh _ringMesh;
        private Material _ringMaterial;
        private int _castId;
        private int _endedCastId;
        private int _bundleIndex = -1;
        private bool _active;
        private bool _awaitingDeath; // 성공 뒤 서버 사망 상태가 올 때까지 그로기 자세를 유지한다
        private bool _floorRemoved;
        private bool _redsLocked;
        private bool _bundleResolved;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsActive => _active;
        public bool IsAwaitingDeath => _awaitingDeath;
        public bool IsFloorRemoved => _floorRemoved;
        public int VisibleYellowCount => CountRunning(_yellows);
        public int VisibleRedCount => CountRunning(_reds);
        public int BundleIndex => _bundleIndex;
        public bool RedsLocked => _redsLocked;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _bossView = GetComponent<BossView>();
        }

        private void Update()
        {
            double now = Now();
            _gate.Flush(now);
            if (_shield != null)
            {
                _shield.transform.position = transform.position + (Vector3.up * SHIELD_HEIGHT);
            }

            if (_active && (_bundleIndex >= 0) && (!_redsLocked) && (!_bundleResolved))
            {
                TrackReds(now);
            }

            TickPlayers(_yellows, _yellowPlayback, now);
            TickPlayers(_reds, _redPlayback, now);
            for (int i = _oneShots.Count - 1; i >= 0; --i)
            {
                if (_oneShots[i] == null)
                {
                    _oneShots.RemoveAt(i);
                }
            }
        }

        private void OnDisable() { Clear(); }
        private void OnDestroy() { Clear(); }

        /****************************************
        *                Methods
        ****************************************/

        public void SetServerClock(Func<double> clock) { _clock = clock; }

        public void SetParticipantAnchor(int combatId, Transform anchor)
        {
            if (anchor == null)
            {
                _anchors.Remove(combatId);
            }
            else
            {
                _anchors[combatId] = anchor;
            }
        }

        public bool StartFinal(in WolbaekFinalStart start, double now)
        {
            if (!ValidStart(in start) || (start.CastInstanceId <= _endedCastId)
                || (_active && (start.CastInstanceId <= _castId)))
            {
                return false;
            }

            ClearVisuals();
            _start = start;
            _castId = start.CastInstanceId;
            _active = true;
            BeginShield();
            ShowFloorTelegraph();
            PlayMotion(GROGGY_STATE, GROGGY_ENTER_BLEND);
            return true;
        }

        public void OuterFloorRemoved(int castId, double serverTime, double now)
        {
            if (_gate.MustDefer(serverTime, now))
            {
                _gate.Defer(serverTime, now, () => OuterFloorRemoved(castId, serverTime, Now()));
                return;
            }
            if (!Matches(castId) || _floorRemoved || (serverTime > now))
            {
                return;
            }

            _floorRemoved = true;
            StopFloorTelegraph();
            SpawnOneShot(_floorCollapsePrefab, _start.ArenaCenter, FLOOR_EFFECT_SCALE * RadiusScale());
        }

        public void YellowsSpawned(int castId, IReadOnlyList<WolbaekFinalYellow> yellows, double now)
        {
            if (!Matches(castId) || (!_floorRemoved) || !ValidYellows(yellows))
            {
                return;
            }

            ShowYellows(yellows, null, now);
        }

        public void YellowDestroyed(int castId, int yellowIndex, double serverTime, double now)
        {
            if (_gate.MustDefer(serverTime, now))
            {
                _gate.Defer(serverTime, now, () => YellowDestroyed(castId, yellowIndex, serverTime, Now()));
                return;
            }
            if (!Matches(castId) || (serverTime > now) || !ValidIndex(yellowIndex, _yellows.Length))
            {
                return;
            }

            WolbaekFoxfirePlayer yellow = _yellows[yellowIndex];
            if ((yellow == null) || (_yellowPlayback[yellowIndex] == 0))
            {
                return;
            }

            Vector3 position = yellow.transform.position;
            yellow.Stop(_yellowPlayback[yellowIndex]);
            _yellowPlayback[yellowIndex] = yellow.PlayImpact(position, EWolbaekFoxfireVariant.GoldTarget,
                serverTime, now);
        }

        public void BundleStarted(int castId, in WolbaekFinalBundle bundle, double now)
        {
            if (!Matches(castId) || !ValidBundle(in bundle) || (bundle.Index <= _bundleIndex))
            {
                return;
            }

            StartBundle(in bundle, null, null, false, now);
        }

        public void RedDetached(int castId, int bundleIndex, int redIndex, double serverTime,
            Vector3 position, double now)
        {
            if (_gate.MustDefer(serverTime, now))
            {
                _gate.Defer(serverTime, now, () => RedDetached(castId, bundleIndex, redIndex, serverTime, position, Now()));
                return;
            }
            if (!MatchesBundle(castId, bundleIndex) || _bundleResolved || _redsLocked
                || !ValidIndex(redIndex, _reds.Length) || (serverTime > now) || !Finite(position))
            {
                return;
            }

            _redDetached[redIndex] = true;
            _redPositions[redIndex] = position;
            EnsureTrackingRed(redIndex, position, serverTime, now);
        }

        public void LockReds(int castId, int bundleIndex, double serverTime,
            IReadOnlyList<Vector3> centers, double now)
        {
            if (_gate.MustDefer(serverTime, now))
            {
                _gate.Defer(serverTime, now, () => LockReds(castId, bundleIndex, serverTime, centers, Now()));
                return;
            }
            if (!MatchesBundle(castId, bundleIndex) || _bundleResolved || _redsLocked
                || (serverTime > now) || !ValidPositions(centers, _reds.Length))
            {
                return;
            }

            LockReds(centers, serverTime, true, now);
        }

        public void BundleResolved(int castId, int bundleIndex, double serverTime, double now)
        {
            if (_gate.MustDefer(serverTime, now))
            {
                _gate.Defer(serverTime, now, () => BundleResolved(castId, bundleIndex, serverTime, Now()));
                return;
            }
            if (!MatchesBundle(castId, bundleIndex) || _bundleResolved || (serverTime > now))
            {
                return;
            }

            _bundleResolved = true;
            for (int i = 0; i < _reds.Length; ++i)
            {
                if ((_reds[i] == null) || (_redPlayback[i] == 0))
                {
                    continue;
                }

                if (_reds[i].State == EWolbaekFoxfireState.Locked)
                {
                    _reds[i].ConfirmImpact(_redPlayback[i], serverTime, now);
                }
                else
                {
                    _reds[i].Stop(_redPlayback[i]);
                }
            }
        }

        public void Succeeded(int castId, double serverTime, double now)
        {
            if (_gate.MustDefer(serverTime, now))
            {
                _gate.Defer(serverTime, now, () => Succeeded(castId, serverTime, Now()));
                return;
            }
            if (!Matches(castId) || (serverTime > now))
            {
                return;
            }

            // 처치는 서버의 사망 상태(LifeState)가 시작한다. 성공 사건은 보호막을 자연 소멸시키고 기믹 표현만 정리하며,
            // 사망 상태가 올 때까지 그로기 자세를 유지한다. 클라이언트가 처치를 먼저 확정하지 않는다
            _endedCastId = Math.Max(_endedCastId, castId);
            FadeShield();
            ClearVisuals(true);
            _awaitingDeath = true;
        }

        public bool Restore(in WolbaekFinalSnapshot snapshot, double now)
        {
            WolbaekFinalStart start = snapshot.Start;
            if (!ValidStart(in start) || (start.CastInstanceId <= _endedCastId)
                || ((snapshot.Step != EWolbaekFinalStep.Collapse) && (snapshot.Step != EWolbaekFinalStep.Hunt)))
            {
                _endedCastId = Math.Max(_endedCastId, start.CastInstanceId);
                Clear();
                return false;
            }

            ClearVisuals();
            _start = start;
            _castId = start.CastInstanceId;
            _active = true;
            _floorRemoved = snapshot.OuterFloorRemoved;
            BeginShield();
            PlayMotion(GROGGY_STATE, GROGGY_ENTER_BLEND);

            if (!_floorRemoved && (start.CollapseServerTime > now))
            {
                ShowFloorTelegraph();
            }

            if (_floorRemoved && ValidYellows(snapshot.Yellows))
            {
                ShowYellows(snapshot.Yellows, snapshot.YellowHealth, now);
            }

            WolbaekFinalBundle restoredBundle = snapshot.Bundle;
            if (snapshot.HasBundle && ValidBundle(in restoredBundle)
                && ValidPositions(snapshot.RedPositions, restoredBundle.Reds.Count)
                && ValidFlags(snapshot.RedDetached, restoredBundle.Reds.Count))
            {
                StartBundle(in restoredBundle, snapshot.RedPositions, snapshot.RedDetached,
                    snapshot.RedsLocked, now);
            }

            return true;
        }

        public void ClearCast(int castId)
        {
            if ((!_active) || (castId != _castId))
            {
                return;
            }

            _endedCastId = Math.Max(_endedCastId, castId);
            ClearVisuals();
            PlayMotion(IDLE_STATE);
        }

        public void Clear()
        {
            _gate.Clear();
            bool hadMotion = (_active) || (_awaitingDeath);
            _awaitingDeath = false;
            if (_active)
            {
                _endedCastId = Math.Max(_endedCastId, _castId);
            }
            ClearVisuals();
            _anchors.Clear();
            if (hadMotion)
            {
                PlayMotion(IDLE_STATE);
            }
        }

        public void ResetNetworkSession()
        {
            _gate.Clear();
            bool hadMotion = (_active) || (_awaitingDeath);
            _awaitingDeath = false;
            ClearVisuals();
            _endedCastId = 0;
            _castId = 0;
            _anchors.Clear();
            if (hadMotion)
            {
                PlayMotion(IDLE_STATE);
            }
        }

        private void StartBundle(in WolbaekFinalBundle bundle, IReadOnlyList<Vector3> positions,
            IReadOnlyList<bool> detached, bool locked, double now)
        {
            ClearReds();
            _bundle = bundle;
            _bundleIndex = bundle.Index;
            _reds = new WolbaekFoxfirePlayer[bundle.Reds.Count];
            _redPlayback = new uint[bundle.Reds.Count];
            _redPositions = new Vector3[bundle.Reds.Count];
            _redDetached = new bool[bundle.Reds.Count];
            _redsLocked = false;
            _bundleResolved = false;

            for (int i = 0; i < bundle.Reds.Count; ++i)
            {
                bool hasPosition = (positions != null) && (i < positions.Count) && Finite(positions[i]);
                bool isDetached = (detached != null) && (i < detached.Count) && detached[i];
                _redDetached[i] = isDetached;
                if (hasPosition)
                {
                    _redPositions[i] = positions[i];
                }

                if (locked)
                {
                    continue;
                }

                if (isDetached && hasPosition)
                {
                    EnsureTrackingRed(i, positions[i], bundle.StartServerTime, now);
                }
                else if (_anchors.TryGetValue(bundle.Reds[i].TargetCombatId, out Transform anchor)
                    && (anchor != null))
                {
                    _redPositions[i] = anchor.position;
                    EnsureTrackingRed(i, anchor.position, bundle.StartServerTime, now);
                }
            }

            if (locked)
            {
                LockReds(positions, bundle.LockServerTime, false, now);
            }
        }

        private void TrackReds(double now)
        {
            for (int i = 0; i < _reds.Length; ++i)
            {
                if (_redDetached[i])
                {
                    EnsureTrackingRed(i, _redPositions[i], _bundle.StartServerTime, now);
                    continue;
                }

                int combatId = _bundle.Reds[i].TargetCombatId;
                if (!_anchors.TryGetValue(combatId, out Transform anchor) || (anchor == null))
                {
                    StopRed(i);
                    continue;
                }

                _redPositions[i] = anchor.position;
                EnsureTrackingRed(i, anchor.position, _bundle.StartServerTime, now);
            }
        }

        private void EnsureTrackingRed(int index, Vector3 position, double startTime, double now)
        {
            if (_foxfirePrefab == null)
            {
                return;
            }
            if (_reds[index] == null)
            {
                _reds[index] = Instantiate(_foxfirePrefab);
            }

            Vector3 flightPosition = position + (Vector3.up * FLIGHT_HEIGHT);
            if ((_redPlayback[index] == 0) || (_reds[index].State != EWolbaekFoxfireState.Flight))
            {
                _redPlayback[index] = _reds[index].PlayFlight(flightPosition, Quaternion.identity, startTime, now);
            }
            else
            {
                _reds[index].SetFlightPose(_redPlayback[index], flightPosition, Quaternion.identity);
            }
        }

        private void LockReds(IReadOnlyList<Vector3> centers, double serverTime, bool playOneShot, double now)
        {
            _redsLocked = true;
            for (int i = 0; i < centers.Count; ++i)
            {
                _redPositions[i] = centers[i];
                if (_foxfirePrefab == null)
                {
                    continue;
                }
                if (_reds[i] == null)
                {
                    _reds[i] = Instantiate(_foxfirePrefab);
                }
                if (_redPlayback[i] != 0)
                {
                    _reds[i].Stop(_redPlayback[i]);
                }
                _redPlayback[i] = _reds[i].BeginTelegraph(centers[i], EWolbaekFoxfireVariant.RedHazard,
                    serverTime, _bundle.BlastServerTime, now);
                _reds[i].SetGroundRadius(_redPlayback[i], _start.RedBlastRadius);
                if (playOneShot)
                {
                    SpawnOneShot(_redLockPrefab, centers[i], LOCK_EFFECT_SCALE);
                }
            }
        }

        private void ShowYellows(IReadOnlyList<WolbaekFinalYellow> yellows, IReadOnlyList<float> health, double now)
        {
            ClearYellows();
            _yellows = new WolbaekFoxfirePlayer[yellows.Count];
            _yellowPlayback = new uint[yellows.Count];
            for (int i = 0; i < yellows.Count; ++i)
            {
                if ((health != null) && (i < health.Count) && (health[i] <= 0f))
                {
                    continue;
                }
                if (_foxfirePrefab == null)
                {
                    continue;
                }
                _yellows[i] = Instantiate(_foxfirePrefab);
                _yellowPlayback[i] = _yellows[i].BeginTelegraph(yellows[i].Position,
                    EWolbaekFoxfireVariant.GoldTarget, _start.CollapseServerTime, double.MaxValue, now);
            }
        }

        private void ShowFloorTelegraph()
        {
            StopFloorTelegraph();
            _floorTelegraph = CreateRing(_start.ArenaCenter, _start.CentralRadius);
            if (_floorTelegraphPrefab != null)
            {
                _floorEffect = Instantiate(_floorTelegraphPrefab,
                    _start.ArenaCenter + (Vector3.up * FLOOR_EFFECT_HEIGHT), Quaternion.identity);
                _floorEffect.transform.localScale = _floorTelegraphPrefab.transform.localScale
                    * (FLOOR_EFFECT_SCALE * RadiusScale());
            }
        }

        private void StopFloorTelegraph()
        {
            if (_floorTelegraph != null)
            {
                Destroy(_floorTelegraph);
            }
            if (_floorEffect != null)
            {
                Destroy(_floorEffect);
            }
            if (_ringMesh != null)
            {
                Destroy(_ringMesh);
            }
            if (_ringMaterial != null)
            {
                Destroy(_ringMaterial);
            }
            _floorTelegraph = null;
            _floorEffect = null;
            _ringMesh = null;
            _ringMaterial = null;
        }

        private GameObject CreateRing(Vector3 center, float radius)
        {
            float innerRadius = Mathf.Max(0f, radius - RING_HALF_WIDTH);
            float outerRadius = radius + RING_HALF_WIDTH;
            var vertices = new Vector3[(RING_SEGMENTS + 1) * 2];
            var triangles = new int[RING_SEGMENTS * 6];
            for (int i = 0; i <= RING_SEGMENTS; ++i)
            {
                float angle = (i / (float)RING_SEGMENTS) * Mathf.PI * 2f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction * innerRadius;
                vertices[(i * 2) + 1] = direction * outerRadius;
            }
            for (int i = 0; i < RING_SEGMENTS; ++i)
            {
                int vertex = i * 2;
                int triangle = i * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 1;
                triangles[triangle + 2] = vertex + 2;
                triangles[triangle + 3] = vertex + 2;
                triangles[triangle + 4] = vertex + 1;
                triangles[triangle + 5] = vertex + 3;
            }

            _ringMesh = new Mesh { name = "WolbaekFinalSafeBoundary", vertices = vertices, triangles = triangles };
            _ringMesh.RecalculateNormals();
            GameObject ring = new GameObject("WolbaekFinalSafeBoundary");
            ring.transform.position = center + (Vector3.up * RING_HEIGHT);
            ring.AddComponent<MeshFilter>().sharedMesh = _ringMesh;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _ringMaterial = new Material(shader) { color = G_TelegraphColor };
                ring.AddComponent<MeshRenderer>().sharedMaterial = _ringMaterial;
            }
            return ring;
        }

        private void BeginShield()
        {
            if (_shieldPrefab == null)
            {
                return;
            }
            _shield = Instantiate(_shieldPrefab, transform.position + (Vector3.up * SHIELD_HEIGHT), Quaternion.identity);
            _shield.transform.localScale = _shieldPrefab.transform.localScale * SHIELD_SCALE;
            foreach (ParticleSystem particle in _shield.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particle.main;
                main.loop = true;
            }
        }

        private void SpawnOneShot(GameObject prefab, Vector3 position, float scale)
        {
            if (prefab == null)
            {
                return;
            }
            GameObject instance = Instantiate(prefab, position + (Vector3.up * FLOOR_EFFECT_HEIGHT), Quaternion.identity);
            instance.transform.localScale = prefab.transform.localScale * scale;
            _oneShots.Add(instance);
            Destroy(instance, ONE_SHOT_LIFE_SECONDS);
        }

        private void ClearVisuals(bool preserveTails = false)
        {
            if (!preserveTails)
            {
                _tails.Clear();
            }
            _active = false;
            _floorRemoved = false;
            StopFloorTelegraph();
            if (_shield != null)
            {
                Destroy(_shield);
            }
            _shield = null;
            ClearYellows();
            ClearReds();
            for (int i = 0; i < _oneShots.Count; ++i)
            {
                if (_oneShots[i] != null)
                {
                    Destroy(_oneShots[i]);
                }
            }
            _oneShots.Clear();
        }

        private void ClearYellows()
        {
            for (int i = 0; i < _yellows.Length; ++i)
            {
                if (_yellows[i] == null)
                {
                    continue;
                }
                _yellows[i].Stop();
                Destroy(_yellows[i].gameObject);
            }
            _yellows = Array.Empty<WolbaekFoxfirePlayer>();
            _yellowPlayback = Array.Empty<uint>();
        }

        private void ClearReds()
        {
            for (int i = 0; i < _reds.Length; ++i)
            {
                if (_reds[i] == null)
                {
                    continue;
                }
                _reds[i].Stop();
                Destroy(_reds[i].gameObject);
            }
            _reds = Array.Empty<WolbaekFoxfirePlayer>();
            _redPlayback = Array.Empty<uint>();
            _redPositions = Array.Empty<Vector3>();
            _redDetached = Array.Empty<bool>();
            _bundleIndex = -1;
            _redsLocked = false;
            _bundleResolved = false;
        }

        private void StopRed(int index)
        {
            if ((_reds[index] != null) && (_redPlayback[index] != 0))
            {
                _reds[index].Stop(_redPlayback[index]);
                _redPlayback[index] = 0;
            }
        }

        private void FadeShield()
        {
            if (_shield == null)
            {
                return;
            }

            // 새 파티클을 멈추고 남은 파티클을 제거 시각에 투명해지도록 낮춘다. 사망, 취소, Reset은 _tails가 즉시 지운다
            WolbaekShieldFade.Begin(_shield, SHIELD_FADE_SECONDS);
            _tails.Keep(_shield);
            Destroy(_shield, SHIELD_FADE_SECONDS);
            _shield = null;
        }

        private void PlayMotion(string state, float blend = MOTION_BLEND_SECONDS)
        {
            if (_bossView == null)
            {
                _bossView = GetComponent<BossView>();
            }
            if (_bossView != null)
            {
                _bossView.PlayGimmickMotion(state, 1f, blend);
            }
        }

        private double Now() { return _clock != null ? _clock() : Time.timeAsDouble; }
        private float RadiusScale() { return _start.CentralRadius / FLOOR_EFFECT_BASE_SIZE; }
        private bool Matches(int castId) { return _active && (castId == _castId); }
        private bool MatchesBundle(int castId, int bundleIndex)
        {
            return Matches(castId) && (bundleIndex == _bundleIndex);
        }

        private static void TickPlayers(WolbaekFoxfirePlayer[] players, uint[] playback, double now)
        {
            for (int i = 0; i < players.Length; ++i)
            {
                if ((players[i] != null) && (playback[i] != 0))
                {
                    players[i].Tick(playback[i], now);
                }
            }
        }

        private static int CountRunning(WolbaekFoxfirePlayer[] players)
        {
            int count = 0;
            for (int i = 0; i < players.Length; ++i)
            {
                if ((players[i] != null) && players[i].IsRunning)
                {
                    ++count;
                }
            }
            return count;
        }

        private static bool ValidStart(in WolbaekFinalStart start)
        {
            return (start.CastInstanceId > 0) && (start.CollapseServerTime >= start.StartServerTime)
                && (start.CentralRadius > 0f) && (start.YellowCount > 0) && (start.YellowMaxHealth > 0f)
                && (start.RedTrackSeconds > 0f) && (start.RedFuseSeconds > 0f) && (start.RedBlastRadius > 0f)
                && Finite(start.ArenaCenter);
        }

        private static bool ValidBundle(in WolbaekFinalBundle bundle)
        {
            if ((bundle.Index < 0) || (bundle.Reds == null) || (bundle.Reds.Count == 0)
                || (bundle.StartServerTime > bundle.LockServerTime)
                || (bundle.LockServerTime > bundle.BlastServerTime))
            {
                return false;
            }
            for (int i = 0; i < bundle.Reds.Count; ++i)
            {
                if ((bundle.Reds[i].Index < 0) || (bundle.Reds[i].TargetCombatId <= 0))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ValidYellows(IReadOnlyList<WolbaekFinalYellow> yellows)
        {
            if ((yellows == null) || (yellows.Count == 0))
            {
                return false;
            }
            for (int i = 0; i < yellows.Count; ++i)
            {
                if ((yellows[i].Index != i) || (yellows[i].CombatId <= 0)
                    || (yellows[i].MaxHealth <= 0f) || !Finite(yellows[i].Position))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ValidPositions(IReadOnlyList<Vector3> positions, int count)
        {
            if ((positions == null) || (positions.Count != count))
            {
                return false;
            }
            for (int i = 0; i < positions.Count; ++i)
            {
                if (!Finite(positions[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ValidFlags(IReadOnlyList<bool> values, int count)
        {
            return (values != null) && (values.Count == count);
        }

        private static bool ValidIndex(int index, int count) { return (index >= 0) && (index < count); }
        private static bool Finite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }
    }
}
