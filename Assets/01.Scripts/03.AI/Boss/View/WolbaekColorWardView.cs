// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    /// <summary> 서버 색 결계 사건만 재생한다. 판정, 단계 진행, 참가자 위치는 만들지 않는다. </summary>
    public sealed class WolbaekColorWardView : MonoBehaviour
    {
        private const float SHIELD_HEIGHT = 1.4f;
        private const float SHIELD_SCALE = 0.9f;
        private const float GATHER_SOURCE_RADIUS = 6.25f;
        private const float STRIKE_HEIGHT = 0.1f;
        private const float STRIKE_SCALE = 6.9f;
        private const float STRIKE_LIFE = 4f;
        private const float MOTION_BLEND_SECONDS = 0.05f;
        private const string GROGGY_STATE = "down_groggy02_loop_inplace";
        private const string IDLE_STATE = "Idle";

        [SerializeField] private GameObject _shieldPrefab;
        [SerializeField] private GameObject _gatherPrefab;
        [SerializeField] private WolbaekWardPlayer _redWardPrefab;
        [SerializeField] private WolbaekWardPlayer _blueWardPrefab;
        [SerializeField] private WolbaekLinkPlayer _redLinkPrefab;
        [SerializeField] private WolbaekLinkPlayer _blueLinkPrefab;
        [SerializeField] private GameObject _strikeTelegraphPrefab;
        [SerializeField] private GameObject _strikePrefab;

        private readonly Dictionary<int, Transform> _anchors = new Dictionary<int, Transform>();
        private readonly List<GameObject> _strikes = new List<GameObject>();
        private readonly double[] _graceEnds = new double[4];
        private Func<double> _clock;
        private WolbaekWardStart _start;
        private WolbaekWardStage _stage;
        private WolbaekWardStrike _telegraph;
        private GameObject _shield;
        private GameObject _gather;
        private GameObject _telegraphEffect;
        private LineRenderer _holdLine;
        private Material _holdMaterial;
        private WolbaekWardPlayer _redWard;
        private WolbaekWardPlayer _blueWard;
        private WolbaekLinkPlayer _redLink;
        private WolbaekLinkPlayer _blueLink;
        private int _castId;
        private int _endedCastId;
        private int _nextStrikeIndex;
        private int _resolvedStrikeIndex = -1;
        private ulong _sequence;
        private double _holdStart;
        private bool _active;
        private bool _gathering;
        private bool _holding;
        private bool _hasStage;
        private bool _hasTelegraph;
        private bool _reviveWindowOpened;
        private double _lastLinkDamageServerTime;
        private BossView _bossView;
        private bool _wardMotionActive;
        private bool _awaitingPhaseTransition;

        public bool IsActive => _active;
        public bool IsGathering => _active && _gathering;
        public bool IsHolding => _active && _holding;
        public float HoldProgress => _holding && (_start.GatherHoldSeconds > 0f)
            ? Mathf.Clamp01((float)((Now() - _holdStart) / _start.GatherHoldSeconds)) : 0f;
        public bool HasTelegraph => _hasTelegraph;
        public int NextStrikeIndex => _nextStrikeIndex;
        public WolbaekWardStage CurrentStage => _stage;
        public bool ReviveWindowOpened => _reviveWindowOpened;
        public double LastLinkDamageServerTime => _lastLinkDamageServerTime;
        public bool IsAwaitingPhaseTransition => _awaitingPhaseTransition;

        private void Awake()
        {
            _bossView = GetComponent<BossView>();
        }

        private void Update()
        {
            if (!_active) return;
            double now = Now();
            if (_shield != null) _shield.transform.position = transform.position + Vector3.up * SHIELD_HEIGHT;
            UpdateHoldLine(now);
            if (_redWard != null && _redWard.IsRunning) _redWard.Tick(_redWard.PlaybackId, now);
            if (_blueWard != null && _blueWard.IsRunning) _blueWard.Tick(_blueWard.PlaybackId, now);
            UpdateLink(_redLink, EWolbaekWardColor.Red, now);
            UpdateLink(_blueLink, EWolbaekWardColor.Blue, now);
            if (_hasTelegraph && now >= _telegraph.StrikeServerTime) StopTelegraph();
            for (int i = _strikes.Count - 1; i >= 0; --i)
            {
                if (_strikes[i] == null) _strikes.RemoveAt(i);
            }
        }

        private void OnDisable() { Clear(); }
        private void OnDestroy() { Clear(); }

        public void SetServerClock(Func<double> clock) { _clock = clock; }

        // 복제 계층이 combatId와 살아 있는 NetworkIdentity의 발밑 Transform을 대응시켜 공급한다.
        public void SetMemberAnchor(int combatId, Transform anchor)
        {
            if (anchor == null) _anchors.Remove(combatId);
            else _anchors[combatId] = anchor;
        }

        public bool StartWard(in WolbaekWardStart start, double now)
        {
            if (start.CastInstanceId <= 0 || start.Members == null || start.GatherRadius <= 0f
                || start.GatherHoldSeconds <= 0f || start.LinkDamageDistance <= start.LinkWarnDistance
                || start.CastInstanceId <= _endedCastId || (_active && start.CastInstanceId <= _castId)) return false;
            if (start.CastInstanceId != _castId) _anchors.Clear();
            ClearVisuals();
            _start = start;
            _castId = start.CastInstanceId;
            _active = true;
            _gathering = true;
            _nextStrikeIndex = 0;
            _resolvedStrikeIndex = -1;
            _sequence = 1;
            Array.Clear(_graceEnds, 0, _graceEnds.Length);
            if (_shieldPrefab != null)
            {
                _shield = Instantiate(_shieldPrefab, transform.position + Vector3.up * SHIELD_HEIGHT, Quaternion.identity);
                _shield.transform.localScale = _shieldPrefab.transform.localScale * SHIELD_SCALE;
                LoopParticles(_shield);
            }
            if (_gatherPrefab != null)
            {
                _gather = Instantiate(_gatherPrefab, start.GatherCenter, Quaternion.identity);
                _gather.transform.localScale = _gatherPrefab.transform.localScale * (start.GatherRadius / GATHER_SOURCE_RADIUS);
                LoopParticles(_gather);
            }
            BeginWardMotion();
            return true;
        }

        public void HoldStarted(int castId, double serverTime)
        {
            if (!Matches(castId) || !_gathering) return;
            _holding = true;
            _holdStart = serverTime;
            if (_holdLine == null)
            {
                var progress = new GameObject("WolbaekWardGatherProgress");
                _holdLine = progress.AddComponent<LineRenderer>();
                _holdLine.useWorldSpace = true;
                _holdLine.widthMultiplier = 0.12f;
                _holdLine.positionCount = 0;
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    _holdMaterial = new Material(shader);
                    _holdLine.sharedMaterial = _holdMaterial;
                }
                _holdLine.startColor = Color.white;
                _holdLine.endColor = Color.white;
            }
        }

        public void HoldReset(int castId)
        {
            if (!Matches(castId) || !_gathering) return;
            _holding = false;
            _holdStart = 0d;
            ClearHoldLine();
        }

        public void GatherCompleted(int castId)
        {
            if (!Matches(castId)) return;
            _gathering = false;
            _holding = false;
            ClearHoldLine();
            DestroyOwned(ref _gather);
        }

        public void StageStarted(int castId, in WolbaekWardStage stage, double now)
        {
            if (!Matches(castId) || stage.Radius <= 0f || stage.StrikeCount <= 0
                || stage.FirstStrikeServerTime < stage.StartServerTime) return;
            GatherCompleted(castId);
            StopStage();
            _stage = stage;
            _hasStage = true;
            _nextStrikeIndex = 0;
            _resolvedStrikeIndex = -1;
            ++_sequence;
            _redWard = SpawnWard(_redWardPrefab, stage.RedCenter, stage.Radius, EWolbaekWardSign.Diamond, Color.red, now);
            _blueWard = SpawnWard(_blueWardPrefab, stage.BlueCenter, stage.Radius, EWolbaekWardSign.Moon, Color.blue, now);
            _redLink = SpawnLink(_redLinkPrefab);
            _blueLink = SpawnLink(_blueLinkPrefab);
        }

        public void StrikeTelegraphed(int castId, in WolbaekWardStrike strike, double now)
        {
            if (!Matches(castId) || !_hasStage || strike.StageIndex != _stage.Index
                || strike.StrikeIndex < _nextStrikeIndex || strike.StrikeIndex >= _stage.StrikeCount
                || strike.StrikeServerTime <= now) return;
            if (_hasTelegraph && _telegraph.StageIndex == strike.StageIndex
                && _telegraph.StrikeIndex == strike.StrikeIndex
                && _telegraph.StrikeServerTime == strike.StrikeServerTime) return;
            StopTelegraph();
            _telegraph = strike;
            _hasTelegraph = true;
            if (_strikeTelegraphPrefab != null)
            {
                _telegraphEffect = Instantiate(_strikeTelegraphPrefab, strike.Origin + Vector3.up * STRIKE_HEIGHT, Quaternion.identity);
                LoopParticles(_telegraphEffect);
            }
        }

        public void StrikeResolved(int castId, int stageIndex, int strikeIndex, double strikeServerTime, double now)
        {
            if (!Matches(castId) || !_hasStage || stageIndex != _stage.Index
                || strikeIndex <= _resolvedStrikeIndex || strikeIndex < _nextStrikeIndex
                || strikeIndex >= _stage.StrikeCount) return;
            Vector3 origin = (_hasTelegraph && _telegraph.StrikeIndex == strikeIndex)
                ? _telegraph.Origin : transform.position;
            StopTelegraph();
            _resolvedStrikeIndex = strikeIndex;
            _nextStrikeIndex = strikeIndex + 1;
            if (strikeServerTime < now || _strikePrefab == null) return;
            GameObject effect = Instantiate(_strikePrefab, origin + Vector3.up * STRIKE_HEIGHT, Quaternion.identity);
            effect.transform.localScale = _strikePrefab.transform.localScale * STRIKE_SCALE;
            _strikes.Add(effect);
            Destroy(effect, STRIKE_LIFE);
        }

        public void Restore(in WolbaekWardSnapshot snapshot, double now)
        {
            if (snapshot.State != EWolbaekWardState.Gather && snapshot.State != EWolbaekWardState.Search
                && snapshot.State != EWolbaekWardState.Strike)
            {
                _endedCastId = Math.Max(_endedCastId, snapshot.Start.CastInstanceId);
                Clear();
                return;
            }
            if (_active && snapshot.Start.CastInstanceId == _castId) ClearVisuals();
            WolbaekWardStart start = snapshot.Start;
            if (!StartWard(in start, now)) return;
            _gathering = snapshot.State == EWolbaekWardState.Gather;
            _holding = _gathering && snapshot.HasHold;
            _holdStart = _holding ? snapshot.HoldStartServerTime : 0d;
            if (_holding) HoldStarted(_castId, _holdStart);
            if (!_gathering)
            {
                WolbaekWardStage stage = snapshot.Stage;
                StageStarted(_castId, in stage, now);
                _nextStrikeIndex = Mathf.Clamp(snapshot.NextStrikeIndex, 0, snapshot.Stage.StrikeCount);
                _resolvedStrikeIndex = _nextStrikeIndex - 1;
                // 현재 예고의 시작 시각과 Origin은 스냅샷에 없어 추측해서 재생하지 않는다.
                // 복제된 진행 중 예고 사건이 오면 StrikeTelegraphed가 복원한다.
            }
            if (snapshot.GraceEndServerTimes != null)
            {
                int count = Math.Min(_graceEnds.Length, snapshot.GraceEndServerTimes.Count);
                for (int i = 0; i < count; ++i)
                    _graceEnds[i] = snapshot.GraceEndServerTimes[i] > now ? snapshot.GraceEndServerTimes[i] : 0d;
            }
        }

        public void GraceStarted(int castId, int combatId, double end)
        {
            int index = MemberIndex(combatId);
            if (Matches(castId) && index >= 0 && index < _graceEnds.Length) _graceEnds[index] = end;
        }

        public void GraceEnded(int castId, int combatId)
        {
            int index = MemberIndex(combatId);
            if (Matches(castId) && index >= 0 && index < _graceEnds.Length) _graceEnds[index] = 0d;
        }

        public void LinkDamaged(int castId, double serverTime)
        {
            if (Matches(castId)) _lastLinkDamageServerTime = serverTime;
        }

        public void ReviveWindowOpenedAt(int castId)
        {
            if (Matches(castId)) _reviveWindowOpened = true;
        }

        public void ClearCast(int castId)
        {
            if (!Matches(castId) && ((!_awaitingPhaseTransition) || (castId != _castId))) return;
            _endedCastId = Math.Max(_endedCastId, castId);
            ClearVisuals();
            _anchors.Clear();
            _awaitingPhaseTransition = false;
            EndWardMotion(true);
        }

        public void Succeeded(int castId)
        {
            if (!Matches(castId)) return;
            _endedCastId = Math.Max(_endedCastId, castId);
            ClearVisuals();
            _anchors.Clear();
            _awaitingPhaseTransition = true;
        }

        // 성공 뒤 서버 페이즈 상태가 Active를 벗어난 시점에만 그로기를 끝낸다.
        public void PhaseTransitionStarted()
        {
            if (!_awaitingPhaseTransition) return;
            _awaitingPhaseTransition = false;
            EndWardMotion(true);
        }

        public void Clear()
        {
            if (_active || _awaitingPhaseTransition) _endedCastId = Math.Max(_endedCastId, _castId);
            ClearVisuals();
            _anchors.Clear();
            _awaitingPhaseTransition = false;
            EndWardMotion(true);
        }
        public void ResetNetworkSession()
        {
            ClearVisuals();
            _endedCastId = 0;
            _castId = 0;
            _anchors.Clear();
            _awaitingPhaseTransition = false;
            EndWardMotion(true);
        }

        private bool Matches(int castId) { return _active && castId == _castId; }
        private double Now() { return _clock != null ? _clock() : Time.timeAsDouble; }

        private int MemberIndex(int combatId)
        {
            if (_start.Members == null) return -1;
            for (int i = 0; i < _start.Members.Count; ++i)
                if (_start.Members[i].CombatId == combatId) return i;
            return -1;
        }

        private WolbaekWardPlayer SpawnWard(WolbaekWardPlayer prefab, Vector3 center, float radius,
            EWolbaekWardSign sign, Color color, double now)
        {
            if (prefab == null) return null;
            WolbaekWardPlayer player = Instantiate(prefab);
            var value = new WolbaekWardPlayer.Snapshot((ulong)_castId, _sequence, (uint)_stage.Index,
                sign, color, center, radius, _stage.StartServerTime, now,
                WolbaekWardPlayer.EPhase.Active, _stage.StartServerTime);
            player.Begin(value, now);
            return player;
        }

        private static WolbaekLinkPlayer SpawnLink(WolbaekLinkPlayer prefab)
        {
            return prefab == null ? null : Instantiate(prefab);
        }

        private void UpdateLink(WolbaekLinkPlayer player, EWolbaekWardColor color, double now)
        {
            if (player == null || !_hasStage || _start.Members == null) return;
            Transform first = null;
            Transform second = null;
            for (int i = 0; i < _start.Members.Count; ++i)
            {
                WolbaekWardMember member = _start.Members[i];
                if (member.Color != color || !_anchors.TryGetValue(member.CombatId, out Transform anchor) || anchor == null)
                    continue;
                if (first == null) first = anchor;
                else { second = anchor; break; }
            }
            if (first == null || second == null) { player.Stop(); return; }
            float distance = Vector3.Distance(first.position, second.position);
            WolbaekLinkPlayer.ESeverity severity = distance > _start.LinkDamageDistance
                ? WolbaekLinkPlayer.ESeverity.Danger : distance >= _start.LinkWarnDistance
                ? WolbaekLinkPlayer.ESeverity.Warning : WolbaekLinkPlayer.ESeverity.Normal;
            float amount = Mathf.Clamp01((distance - _start.LinkWarnDistance)
                / (_start.LinkDamageDistance - _start.LinkWarnDistance));
            bool red = color == EWolbaekWardColor.Red;
            var value = new WolbaekLinkPlayer.Snapshot((ulong)_castId, ++_sequence,
                red ? EWolbaekWardSign.Diamond : EWolbaekWardSign.Moon,
                red ? Color.red : Color.blue, first.position, second.position,
                _stage.StartServerTime, now, WolbaekLinkPlayer.EPhase.Active,
                _stage.StartServerTime, severity, amount, true);
            if (!player.IsRunning) player.Begin(value, now);
            else player.Reconcile(player.PlaybackId, value, now);
            if (player.IsRunning) player.Tick(player.PlaybackId, now);
        }

        private void StopStage()
        {
            StopTelegraph();
            if (_redWard != null) { _redWard.Stop(); Destroy(_redWard.gameObject); _redWard = null; }
            if (_blueWard != null) { _blueWard.Stop(); Destroy(_blueWard.gameObject); _blueWard = null; }
            if (_redLink != null) { _redLink.Stop(); Destroy(_redLink.gameObject); _redLink = null; }
            if (_blueLink != null) { _blueLink.Stop(); Destroy(_blueLink.gameObject); _blueLink = null; }
            _hasStage = false;
        }

        private void StopTelegraph()
        {
            _hasTelegraph = false;
            DestroyOwned(ref _telegraphEffect);
        }

        private void ClearVisuals()
        {
            _active = false;
            _gathering = false;
            _holding = false;
            StopStage();
            ClearHoldLine();
            DestroyOwned(ref _gather);
            DestroyOwned(ref _shield);
            for (int i = 0; i < _strikes.Count; ++i) if (_strikes[i] != null) Destroy(_strikes[i]);
            _strikes.Clear();
            Array.Clear(_graceEnds, 0, _graceEnds.Length);
            _reviveWindowOpened = false;
            _lastLinkDamageServerTime = 0d;
        }

        private void UpdateHoldLine(double now)
        {
            if (!_holding || _holdLine == null) return;
            const int points = 65;
            int count = Mathf.Clamp(Mathf.CeilToInt(HoldProgress * (points - 1)) + 1, 2, points);
            _holdLine.positionCount = count;
            for (int i = 0; i < count; ++i)
            {
                float angle = (i / (float)(points - 1)) * Mathf.PI * 2f;
                _holdLine.SetPosition(i, _start.GatherCenter + new Vector3(Mathf.Cos(angle) * _start.GatherRadius,
                    0.12f, Mathf.Sin(angle) * _start.GatherRadius));
            }
        }

        private void ClearHoldLine()
        {
            if (_holdLine != null) Destroy(_holdLine.gameObject);
            if (_holdMaterial != null) Destroy(_holdMaterial);
            _holdLine = null;
            _holdMaterial = null;
        }

        private void BeginWardMotion()
        {
            if (_bossView == null) _bossView = GetComponent<BossView>();
            if (_bossView == null) return;
            _awaitingPhaseTransition = false;
            _wardMotionActive = true;
            _bossView.PlayGimmickMotion(GROGGY_STATE, 1f, MOTION_BLEND_SECONDS);
        }

        private void EndWardMotion(bool returnToIdle)
        {
            if (!_wardMotionActive) return;
            _wardMotionActive = false;
            if (returnToIdle && (_bossView != null))
            {
                _bossView.PlayGimmickMotion(IDLE_STATE, 1f, MOTION_BLEND_SECONDS);
            }
        }

        private static void LoopParticles(GameObject root)
        {
            foreach (ParticleSystem particle in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particle.main;
                main.loop = true;
            }
        }

        private static void DestroyOwned(ref GameObject value)
        {
            if (value != null) Destroy(value);
            value = null;
        }
    }
}
