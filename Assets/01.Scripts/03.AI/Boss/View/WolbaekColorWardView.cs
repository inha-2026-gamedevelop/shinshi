// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Combat;

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
        private const float GROGGY_ENTER_BLEND = 0.3f;      // 시험 씬 GimmickAttackCue 그로기 진입 섞기와 같은 정규화 값
        private const float STRIKE_LATE_GRACE_SECONDS = 0.5f; // 판정 시각 뒤 확정 사건을 받아도 표현하는 한계, 기본 예고의 CONFIRM_GRACE_SECONDS와 같다
        private const float SHIELD_FADE_SECONDS = 2.5f;     // 정상 성공 때 새 파티클을 멈춘 뒤 남은 파티클이 투명해지며 사라지는 시간, 시험 씬 GimmickShieldCue와 같다
        private static readonly Color G_RedWardColor = new Color(1f, 0.25f, 0.3f, 1f);  // 시험 씬 ColorWardCue 확정 색
        private static readonly Color G_BlueWardColor = new Color(0.3f, 0.55f, 1f, 1f);
        private const float MARK_RADIUS = 0.75f;           // 참가자 발밑 색 표식의 반경
        private const float MARK_WIDTH = 0.1f;
        private const float MARK_HEIGHT = 0.06f;
        private const float MARK_DIAMOND_SCALE = 1.2f;     // 마름모는 같은 반경의 원보다 작아 보여서 키운다
        private const int MARK_CIRCLE_POINTS = 32;
        private const int MARK_DIAMOND_POINTS = 4;
        private const float GUIDE_DISTANCE = 2.5f;        // 내 발밑에서 방향 화살표까지의 거리
        private const float GUIDE_LENGTH = 1.2f;
        private const float GUIDE_WING = 0.35f;
        private const float GUIDE_WIDTH = 0.16f;
        private const float GUIDE_HEIGHT = 0.12f;
        private const float GUIDE_HIDE_MARGIN = 0.5f;      // 결계 반경 안쪽에 들어오면 화살표를 끈다
        private const int GUIDE_POINTS = 5;
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
        private readonly Dictionary<int, GameObject> _marks = new Dictionary<int, GameObject>();
        private readonly List<int> _staleMarks = new List<int>();
        private readonly List<GameObject> _strikes = new List<GameObject>();
        private readonly double[] _graceEnds = new double[4];
        private Func<double> _clock;
        private readonly WolbaekEffectTails _tails = new WolbaekEffectTails();
        private WolbaekWardStart _start;
        private WolbaekWardStage _stage;
        private WolbaekWardStrike _telegraph;
        private GameObject _shield;
        private GameObject _gather;
        private GameObject _telegraphEffect;
        private LineRenderer _holdLine;
        private Material _holdMaterial;
        private Material _markMaterial;
        private Material _guideMaterial;
        private LineRenderer _redGuide;
        private LineRenderer _blueGuide;
        private PlayerCombatState _localCombat;
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
        private GameObject _fadingShield;      // 정상 성공으로 2.5초 소멸 중인 이 기믹의 보호막, 컷신 인계 때 이것만 정확히 넘긴다
        private int _fadingShieldCastId;
        private int _handoverCastId;           // 보호막을 컷신에 넘긴 시전, 이 시전의 늦은 성공이나 복원은 보호막을 다시 만들지 않는다

        private const float LINK_WARN_SOUND_INTERVAL   = 1.2f; // 초, 연결선 경고 단계에서 경고음을 반복하는 간격
        private const float LINK_DANGER_SOUND_INTERVAL = 0.5f; // 초, 위험 단계의 반복 간격

        private WolbaekSoundPlayer _sound;
        private bool               _isRestoring;
        private float              _nextLinkSoundTime;

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
        public bool HasFadingShield => _fadingShield != null;
        public int HandoverCastId => _handoverCastId;

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
            UpdateMemberMarks();
            UpdateGuides();
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
            PlaySound(EWolbaekSound.WardOpen, transform.position);
            return true;
        }

        // 재접속 복원 중에는 이미 지난 일회성 소리를 다시 내지 않는다
        private void PlaySound(EWolbaekSound id, Vector3 position)
        {
            if (_isRestoring)
            {
                return;
            }

            if (_sound == null)
            {
                _sound = GetComponent<WolbaekSoundPlayer>();
            }

            if (_sound != null)
            {
                _sound.Play(id, position);
            }
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
            _redWard = SpawnWard(_redWardPrefab, stage.RedCenter, stage.Radius, EWolbaekWardSign.Diamond, G_RedWardColor, now);
            _blueWard = SpawnWard(_blueWardPrefab, stage.BlueCenter, stage.Radius, EWolbaekWardSign.Moon, G_BlueWardColor, now);
            _redLink = SpawnLink(_redLinkPrefab);
            _blueLink = SpawnLink(_blueLinkPrefab);
            PlaySound(EWolbaekSound.WardOpen, stage.RedCenter);
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
            // 실네트워크의 확정 사건은 판정 시각보다 늦게 온다. 짧은 지연은 받은 순간부터 재생하고, 한계를 넘긴 지난 타격과 복원 이전 타격은 다시 만들지 않는다
            if (((now - strikeServerTime) > STRIKE_LATE_GRACE_SECONDS) || (_strikePrefab == null))
            {
                return;
            }
            GameObject effect = Instantiate(_strikePrefab, origin + Vector3.up * STRIKE_HEIGHT, Quaternion.identity);
            effect.transform.localScale = _strikePrefab.transform.localScale * STRIKE_SCALE;
            _strikes.Add(effect);
            Destroy(effect, STRIKE_LIFE);
            PlaySound(EWolbaekSound.BigBlast, origin);
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
            _isRestoring = true;
            bool started = StartWard(in start, now);
            if (!started)
            {
                _isRestoring = false;
                return;
            }
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
            _isRestoring = false;
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
            ForgetShieldHandover();
            _anchors.Clear();
            _awaitingPhaseTransition = false;
            EndWardMotion(true);
        }

        /// <summary> 컷신에 넘길 이 50% 시전의 보호막이 있다(반복 중이거나 성공 뒤 소멸 중) </summary>
        public bool HasShieldForCutscene => _active ? (_shield != null) : (_fadingShield != null);

        // 2페이즈 전환 컷신이 장비 연결까지 끝내고 실제 재생을 시작하는 순간에만 부른다. 사건 수신이나 재생 시도만으로는 부르지 않는다.
        // 실제 인스턴스를 그대로 넘긴다. 같은 오브젝트라 인계 순간 위치(보스 위 1.4m), 크기(0.9배), 밝기, 파티클 배치가 끊기지 않는다.
        // 넘긴 뒤 수명과 정리는 컷신 쪽만 책임진다: 소멸 목록(_tails)에서 빼고 소유 참조를 비운다. 성공 때 걸어 둔 2.5초 지연 제거는
        // 취소할 수 없지만 컷신이 0.75초에 먼저 지우므로 이미 사라진 대상을 가리켜 아무 일도 하지 않는다.
        // 넘긴 시전은 종료 기록에 올려 늦은 성공, 중복 성공, 같은 시전 재시작, 복원이 보호막을 다시 만들지 않게 한다.
        // 타격 VFX와 다른 잔여 효과는 그대로 둔다
        /// <summary> 이 50% 시전의 보호막 인스턴스를 컷신에 넘긴다. 넘길 것이 없으면 null이다 </summary>
        public GameObject TakeShieldForCutscene(out EWolbaekShieldHandover kind)
        {
            GameObject shield = null;
            kind = EWolbaekShieldHandover.None;
            if (_active)
            {
                // 전환 사건이 성공 사건보다 먼저 왔다
                _handoverCastId = _castId;
                _endedCastId = Math.Max(_endedCastId, _castId);
                shield = _shield;
                _shield = null;
                kind = (shield != null) ? EWolbaekShieldHandover.ActiveShield : EWolbaekShieldHandover.None;
                return shield;
            }

            if (_fadingShield != null)
            {
                _handoverCastId = _fadingShieldCastId;
                shield = _fadingShield;
                _tails.Release(shield);
                _fadingShield = null;
                kind = EWolbaekShieldHandover.FadingShield;
            }

            return shield;
        }

        public void Succeeded(int castId)
        {
            if (!Matches(castId)) return;
            _endedCastId = Math.Max(_endedCastId, castId);
            PlaySound(EWolbaekSound.Success, transform.position);
            // 정상 성공은 보호막의 새 파티클만 멈춰 자연 소멸시키고, 성공과 같은 순간의 마지막 광역 효과는 제 수명대로 둔다
            FadeShield();
            for (int i = 0; i < _strikes.Count; ++i)
            {
                _tails.Keep(_strikes[i]);
            }
            _strikes.Clear();
            ClearVisuals(true);
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
            ForgetShieldHandover();
            _anchors.Clear();
            _awaitingPhaseTransition = false;
            EndWardMotion(true);
        }
        public void ResetNetworkSession()
        {
            ClearVisuals();
            ForgetShieldHandover();
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
            // 재생기는 시작 시각이 현재 서버 시각보다 미래인 스냅샷을 조용히 거절한다. 지연이 짧으면 추정 서버 시각이
            // 단계 시작 시각보다 늦을 수 있어 현재 시각으로 잘라 넣는다
            double start = Math.Min(_stage.StartServerTime, now);
            var value = new WolbaekWardPlayer.Snapshot((ulong)_castId, _sequence, (uint)_stage.Index,
                sign, color, center, radius, start, now,
                WolbaekWardPlayer.EPhase.Active, start);
            if (player.Begin(value, now) == 0)
            {
                Debug.LogWarning($"[Boss] 월백 색 결계 재생기가 시작을 거절했다, cast={_castId}, stage={_stage.Index}, sign={sign}");
            }

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
            PlayLinkWarning(severity, first.position);
            bool red = color == EWolbaekWardColor.Red;
            // 재생 중이면 시작 때 정한 시각을 그대로 써서 같은 단계로 인정받게 한다
            double start = player.IsRunning ? player.CurrentSnapshot.AppearedAt : Math.Min(_stage.StartServerTime, now);
            var value = new WolbaekLinkPlayer.Snapshot((ulong)_castId, ++_sequence,
                red ? EWolbaekWardSign.Diamond : EWolbaekWardSign.Moon,
                red ? G_RedWardColor : G_BlueWardColor, first.position, second.position,
                start, now, WolbaekLinkPlayer.EPhase.Active,
                start, severity, amount, true);
            if (!player.IsRunning) player.Begin(value, now);
            else player.Reconcile(player.PlaybackId, value, now);
            if (player.IsRunning) player.Tick(player.PlaybackId, now);
        }

        // 경고 단계를 유지하는 동안 매 프레임 다시 울리지 않고 간격마다 한 번씩 낸다, 두 연결선이 같은 간격을 나눠 쓴다
        private void PlayLinkWarning(WolbaekLinkPlayer.ESeverity severity, Vector3 position)
        {
            if ((severity == WolbaekLinkPlayer.ESeverity.Normal) || (Time.time < _nextLinkSoundTime))
            {
                return;
            }

            _nextLinkSoundTime = Time.time + ((severity == WolbaekLinkPlayer.ESeverity.Danger)
                ? LINK_DANGER_SOUND_INTERVAL : LINK_WARN_SOUND_INTERVAL);
            PlaySound(EWolbaekSound.Warning, position);
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

        private void ClearVisuals(bool preserveTails = false)
        {
            if (!preserveTails)
            {
                _tails.Clear();
            }
            _active = false;
            _gathering = false;
            _holding = false;
            StopStage();
            ClearHoldLine();
            ClearMemberMarks();
            ClearGuides();
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

        // 서버가 정한 배정 색을 참가자 발밑에 그린다. 연결 띠는 같은 색이 둘일 때만 생겨 혼자인 색은 단서가 없기 때문이다.
        // 색만으로 구분하지 않도록 모양도 다르게 한다(빨강 마름모, 파랑 원, 결계의 기호와 같다). 배정은 서버가 정한 값을 그대로 쓴다
        private void UpdateMemberMarks()
        {
            if (_start.Members == null)
            {
                return;
            }

            _staleMarks.Clear();
            foreach (KeyValuePair<int, GameObject> pair in _marks)
            {
                if (!_anchors.TryGetValue(pair.Key, out Transform held) || held == null || pair.Value == null)
                {
                    _staleMarks.Add(pair.Key);
                }
            }

            for (int i = 0; i < _staleMarks.Count; ++i)
            {
                if (_marks.TryGetValue(_staleMarks[i], out GameObject stale) && stale != null) Destroy(stale);
                _marks.Remove(_staleMarks[i]);
            }

            for (int i = 0; i < _start.Members.Count; ++i)
            {
                WolbaekWardMember member = _start.Members[i];
                if (!_anchors.TryGetValue(member.CombatId, out Transform anchor) || anchor == null) continue;

                if (!_marks.TryGetValue(member.CombatId, out GameObject mark) || mark == null)
                {
                    mark = CreateMemberMark(member.Color);
                    if (mark == null) continue;
                    _marks[member.CombatId] = mark;
                }

                mark.transform.position = anchor.position + (Vector3.up * MARK_HEIGHT);
            }
        }

        private GameObject CreateMemberMark(EWolbaekWardColor color)
        {
            if (_markMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) return null;
                _markMaterial = new Material(shader);
            }

            bool red = color == EWolbaekWardColor.Red;
            Color tint = red ? G_RedWardColor : G_BlueWardColor;

            var mark = new GameObject("WolbaekWardMemberMark");
            mark.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            LineRenderer line = mark.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.alignment = LineAlignment.TransformZ;
            line.widthMultiplier = MARK_WIDTH;
            line.sharedMaterial = _markMaterial;
            line.startColor = tint;
            line.endColor = tint;

            int points = red ? MARK_DIAMOND_POINTS : MARK_CIRCLE_POINTS;
            line.positionCount = points;
            for (int i = 0; i < points; ++i)
            {
                float angle = (i / (float)points) * Mathf.PI * 2f;
                float radius = red ? (MARK_RADIUS * MARK_DIAMOND_SCALE) : MARK_RADIUS;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }

            return mark;
        }

        // 결계가 화면 밖에 생겨도 찾을 수 있게 내 발밑에서 내 색 결계 쪽을 가리키는 화살표를 그린다.
        // 내 배정 색을 모르면(관전, 늦은 입장) 두 색 모두 그린다. 표시만 하며 판정에는 쓰지 않는다
        private void UpdateGuides()
        {
            if (!_hasStage || (_start.Members == null))
            {
                HideGuides();
                return;
            }

            Transform self = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.transform : null;
            if (self == null)
            {
                HideGuides();
                return;
            }

            if ((_localCombat == null) || (_localCombat.transform != self))
            {
                self.TryGetComponent(out _localCombat);
            }

            bool showRed = true;
            bool showBlue = true;
            if (_localCombat != null)
            {
                for (int i = 0; i < _start.Members.Count; ++i)
                {
                    if (_start.Members[i].CombatId != _localCombat.CombatId) continue;
                    showRed = _start.Members[i].Color == EWolbaekWardColor.Red;
                    showBlue = !showRed;
                    break;
                }
            }

            UpdateGuide(ref _redGuide, "WolbaekWardGuideRed", showRed, self.position, _stage.RedCenter, G_RedWardColor);
            UpdateGuide(ref _blueGuide, "WolbaekWardGuideBlue", showBlue, self.position, _stage.BlueCenter, G_BlueWardColor);
        }

        private void UpdateGuide(ref LineRenderer line, string name, bool show, Vector3 from, Vector3 target, Color tint)
        {
            Vector3 flat = new Vector3(target.x - from.x, 0f, target.z - from.z);
            float distance = flat.magnitude;
            if (!show || (distance <= (_stage.Radius + GUIDE_HIDE_MARGIN)))
            {
                if (line != null) line.enabled = false;
                return;
            }

            if (line == null)
            {
                if (_guideMaterial == null)
                {
                    Shader shader = Shader.Find("Sprites/Default");
                    if (shader == null) return;
                    _guideMaterial = new Material(shader);
                }

                var go = new GameObject(name);
                line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = GUIDE_POINTS;
                line.widthMultiplier = GUIDE_WIDTH;
                line.numCapVertices = 2;
                line.sharedMaterial = _guideMaterial;
                line.startColor = tint;
                line.endColor = tint;
            }

            Vector3 dir = flat / distance;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
            Vector3 tail = new Vector3(from.x, from.y + GUIDE_HEIGHT, from.z) + (dir * GUIDE_DISTANCE);
            Vector3 tip = tail + (dir * GUIDE_LENGTH);
            line.enabled = true;
            line.SetPosition(0, tail);
            line.SetPosition(1, tip);
            line.SetPosition(2, tip - (dir * GUIDE_WING) + (side * GUIDE_WING));
            line.SetPosition(3, tip);
            line.SetPosition(4, tip - (dir * GUIDE_WING) - (side * GUIDE_WING));
        }

        private void HideGuides()
        {
            if (_redGuide != null) _redGuide.enabled = false;
            if (_blueGuide != null) _blueGuide.enabled = false;
        }

        private void ClearGuides()
        {
            if (_redGuide != null) Destroy(_redGuide.gameObject);
            if (_blueGuide != null) Destroy(_blueGuide.gameObject);
            _redGuide = null;
            _blueGuide = null;
            if (_guideMaterial != null) Destroy(_guideMaterial);
            _guideMaterial = null;
            _localCombat = null;
        }

        private void ClearMemberMarks()
        {
            foreach (KeyValuePair<int, GameObject> pair in _marks)
            {
                if (pair.Value != null) Destroy(pair.Value);
            }

            _marks.Clear();
            if (_markMaterial != null) Destroy(_markMaterial);
            _markMaterial = null;
        }

        private void FadeShield()
        {
            // 컷신에 이미 넘긴 시전이면 지울 보호막도 없고 새로 만들지도 않는다
            if ((_shield == null) || (_handoverCastId == _castId))
            {
                return;
            }

            // 새 파티클을 멈추고 남은 파티클을 제거 시각에 투명해지도록 낮춘다. 사망, 취소, Reset은 _tails가 즉시 지운다
            WolbaekShieldFade.Begin(_shield, SHIELD_FADE_SECONDS);
            _tails.Keep(_shield);
            Destroy(_shield, SHIELD_FADE_SECONDS);
            _fadingShield = _shield;
            _fadingShieldCastId = _castId;
            _shield = null;
        }

        private void ForgetShieldHandover()
        {
            _fadingShield = null;
            _fadingShieldCastId = 0;
            _handoverCastId = 0;
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
            _bossView.PlayGimmickMotion(GROGGY_STATE, 1f, GROGGY_ENTER_BLEND);
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
            WolbaekParticleLoop.Apply(root);
        }

        private static void DestroyOwned(ref GameObject value)
        {
            if (value != null) Destroy(value);
            value = null;
        }
    }
}
