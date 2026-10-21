// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 서버가 확정한 부적진 사건만 그린다. 체력 비율과 시험 씬 시각표로 시작하거나 성공시키지 않는다.
    /// <summary> 월백 75% 사방 부적진의 구조물, 보호막, 하드 압박 공격과 모션 표현 </summary>
    public sealed class WolbaekTalismanView : MonoBehaviour
    {
        private const int STRUCTURE_COUNT = 4; // 구조물 최대 수와 문양 종류 수, 서버는 참가 인원에 따라 1~4개를 보낸다
        private const int RING_SEGMENTS = 96;
        private const float SHIELD_HEIGHT = 1.4f;
        private const float SHIELD_SCALE = 0.9f;
        private const float SHIELD_FADE_SECONDS = 2.5f; // 정상 성공 때 보호막의 남은 파티클이 투명해지며 사라지는 시간, 시험 씬 GimmickShieldCue와 같다
        private const float RING_HEIGHT = 0.1f;
        private const float RANGE_HEIGHT = 0.05f;
        private const float LINE_HEIGHT = 1f;
        private const float RING_SCALE_PER_METRE = 0.27f;
        private const float RANGE_FADE_SECONDS = 0.4f;
        private const float EFFECT_LIFE_SECONDS = 4f;
        private const float BEAM_ACTIVE_SECONDS = 1.4f;
        private const float BEAM_LINGER_SECONDS = 0.5f;
        private const float STRIKE_GRACE_SECONDS = 0.25f;
        private const float RING_PEAK_SECONDS = 1.2f;
        private const float LINE_PEAK_SECONDS = 0.61f;
        private const float ATTACK_RECOVERY_SECONDS = 0.5f;
        private const float DEFENSE_SECONDS = 1.17f;
        private const float SUCCESS_IDLE_LEAD_SECONDS = 0.6f;
        private const float SUCCESS_ENTER_BLEND_SECONDS = 0.3f;
        private const float SUCCESS_EXIT_BLEND_SECONDS = 0.6f;
        private const float MARK_HEIGHT = 2.1f;      // 배정 문양을 앵커(발밑)에서 띄우는 높이
        private const float MARK_SIZE = 0.7f;        // 배정 문양의 긴 변 길이(m)
        private const float MIN_FACE_DISTANCE_SQR = 0.0001f; // 카메라와 문양이 거의 겹치면 방향을 바꾸지 않는다
        private const string GLYPH_NAME_PREFIX = "SM_Talisman_Glyph";
        private static readonly string[] _glyphNames = { "Flower", "Moon", "Star", "Diamond" };
        private static readonly int _markColorId = Shader.PropertyToID("_Color");
        private const string RANGE_SHADER = "Sprites/Default";
        private const string DEFENSE_STATE = "defense_inplace";
        private const string RING_STATE = "attack02_inplace";
        private const string LINE_STATE = "attack09_inplace";
        private const string SUCCESS_STATE = "down_groggy02_loop_inplace";
        private const string IDLE_STATE = "Idle";

        private static readonly Color[] _colors =
        {
            new Color(1f, 0.25f, 0.3f, 1f),
            new Color(0.3f, 0.55f, 1f, 1f),
            new Color(1f, 0.85f, 0.25f, 1f),
            new Color(0.3f, 0.9f, 0.5f, 1f)
        };

        [SerializeField] private BossView _bossView;
        [SerializeField] private Animator _animator;
        [SerializeField] private WolbaekTalismanPlayer[] _structurePrefabs; // 문양 ID 순서: 꽃, 달, 별, 마름모
        [SerializeField] private GameObject _shieldPrefab;
        [SerializeField] private GameObject _ringEffectPrefab;
        [SerializeField] private WolbaekBeamPlayer _lineEffectPrefab;

        private readonly WolbaekTalismanPlayer[] _structures = new WolbaekTalismanPlayer[STRUCTURE_COUNT];
        private readonly WolbaekServerEventGate _gate = new WolbaekServerEventGate(); // 시계 오차로 서버 시각이 아직 오지 않은 사건을 그 시각까지 보류한다
        private Transform _viewer; // 문양을 돌려 보일 카메라, 주입하거나 기믹 시작 때 한 번 찾는다
        private readonly uint[] _playbackIds = new uint[STRUCTURE_COUNT];
        private readonly WolbaekTalismanStructure[] _placements = new WolbaekTalismanStructure[STRUCTURE_COUNT];
        private readonly Dictionary<int, Transform> _anchors = new Dictionary<int, Transform>();
        private readonly Dictionary<int, GameObject> _marks = new Dictionary<int, GameObject>();
        private readonly List<int> _staleMarks = new List<int>();
        private MaterialPropertyBlock _markBlock;
        private readonly List<GameObject> _effects = new List<GameObject>(4);
        private readonly WolbaekEffectTails _tails = new WolbaekEffectTails();
        private readonly List<WolbaekBeamPlayer> _beams = new List<WolbaekBeamPlayer>(2);
        private Func<double> _serverClock;
        private WolbaekTalismanStart _start;
        private WolbaekTalismanAttack _attack;
        private GameObject _shield;
        private GameObject _range;
        private Mesh _rangeMesh;
        private Material _rangeMaterial;
        private int _castId;
        private int _attackId;
        private int _lastStruckAttackId;
        private int _lastEndedCastId;
        private ulong _sequence;
        private double _phaseStart;
        private double _holdStart;
        private double _counterEnd;
        private double _restoreTime;
        private bool _active;
        private bool _holding;
        private bool _resetting;
        private bool _counter;
        private bool _hasAttack;
        private bool _attackWasStruck;
        private bool _attackMotionStarted;
        private bool _attackSpeedReset;
        private bool _idleStarted;

        public bool IsActive => _active;
        public int CastId => _castId;
        public int VisibleStructureCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < STRUCTURE_COUNT; ++i)
                {
                    if ((_structures[i] != null) && (_structures[i].IsRunning))
                    {
                        ++count;
                    }
                }
                return count;
            }
        }

        private void OnDisable()
        {
            Clear();
        }

        private void OnDestroy()
        {
            Clear();
        }

        private void Update()
        {
            if (_serverClock != null)
            {
                _gate.Flush(_serverClock());
            }
            if ((_active) && (_serverClock != null))
            {
                Tick(_serverClock());
            }
            UpdateMemberMarks();
        }

        /// <summary> 참가자 머리 위 배정 문양의 기준이 될 몸체를 넣는다, null이면 지운다. 기믹이 끝나도 유지한다 </summary>
        public void SetMemberAnchor(int combatId, Transform anchor)
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

        public void SetServerClock(Func<double> serverClock)
        {
            _serverClock = serverClock;
        }

        private double Now()
        {
            return (_serverClock != null) ? _serverClock() : Time.timeAsDouble;
        }

        public bool StartFormation(in WolbaekTalismanStart start, double now)
        {
            WolbaekTalismanStart pending = start;
            if (_gate.MustDefer(start.StartServerTime, now))
            {
                _gate.Defer(start.StartServerTime, now, () => StartFormation(in pending, Now()));
                return false;
            }
            if ((!ValidStart(in start)) || (now < start.StartServerTime)
                || (_active && (start.CastInstanceId <= _castId))
                || (start.CastInstanceId <= _lastEndedCastId))
            {
                return false;
            }

            Clear();
            _start = start;
            _castId = start.CastInstanceId;
            _phaseStart = start.StartServerTime;
            _sequence = 1;
            _active = true;
            // 주입받은 카메라가 없으면 기믹이 시작될 때 한 번만 찾는다. 프레임마다 찾지 않고, 카메라가 아직 없으면 문양을 돌리지 않는다
            if (_viewer == null)
            {
                Camera main = Camera.main;
                _viewer = (main != null) ? main.transform : null;
            }
            for (int i = 0; i < start.Structures.Count; ++i)
            {
                WolbaekTalismanStructure structure = start.Structures[i];
                int symbol = structure.SymbolId;
                _placements[i] = structure;
                _structures[i] = Instantiate(_structurePrefabs[symbol]);
                _playbackIds[i] = _structures[i].Begin(BuildStructureSnapshot(i, WolbaekTalismanPlayer.EPhase.Holding,
                    start.StartServerTime, now, 0f), now);
            }

            BeginShield();
            if (now < start.StartServerTime + DEFENSE_SECONDS)
            {
                PlayMotion(DEFENSE_STATE, 1f, 0.05f);
            }
            return true;
        }

        // successServerTime은 서버 복원 기록이 함께 주는 성공 시각이다. 모르면 NaN이고 반격 진행을 지금부터 센다
        public bool Restore(in WolbaekTalismanSnapshot snapshot, double now, double successServerTime = double.NaN)
        {
            if ((snapshot.State != EWolbaekTalismanState.Transition)
                && (snapshot.State != EWolbaekTalismanState.Active)
                && (snapshot.State != EWolbaekTalismanState.Counter))
            {
                return false;
            }
            WolbaekTalismanStart start = snapshot.Start;
            if (!StartFormation(in start, now))
            {
                return false;
            }

            _restoreTime = now;
            if (snapshot.State == EWolbaekTalismanState.Counter)
            {
                // 성공 시각을 알면 반격 진행을 그 시각부터 이어 간다. 지난 타격 효과는 재생하지 않는다
                _counter = true;
                _counterEnd = snapshot.CounterEndServerTime;
                _phaseStart = ((!double.IsNaN(successServerTime)) && (successServerTime <= now)) ? successServerTime : now;
                _idleStarted = now >= _counterEnd - SUCCESS_IDLE_LEAD_SECONDS;
                StopShield();
                ReconcileStructures(now);
                PlayMotion(_idleStarted ? IDLE_STATE : SUCCESS_STATE, 1f,
                    _idleStarted ? SUCCESS_EXIT_BLEND_SECONDS : SUCCESS_ENTER_BLEND_SECONDS);
                return true;
            }

            if (snapshot.HasHold)
            {
                HoldStarted(_castId, snapshot.HoldStartServerTime, now);
            }
            if (snapshot.HasAttack && (snapshot.Attack.StrikeServerTime > now))
            {
                WolbaekTalismanAttack attack = snapshot.Attack;
                AttackTelegraphed(_castId, in attack, now);
            }
            return true;
        }

        public void HoldStarted(int castId, double serverTime, double now)
        {
            if (_gate.MustDefer(serverTime, now))
            {
                _gate.Defer(serverTime, now, () => HoldStarted(castId, serverTime, Now()));
                return;
            }
            if ((!SameCast(castId)) || (serverTime > now) || (_counter))
            {
                return;
            }
            _holding = true;
            _resetting = false;
            _holdStart = serverTime;
            _phaseStart = serverTime;
            ReconcileStructures(now);
        }

        public void HoldReset(int castId, double serverTime, double now)
        {
            if (_gate.MustDefer(serverTime, now))
            {
                _gate.Defer(serverTime, now, () => HoldReset(castId, serverTime, Now()));
                return;
            }
            if ((!SameCast(castId)) || (serverTime > now) || (_counter))
            {
                return;
            }
            _holding = false;
            _resetting = true;
            _phaseStart = serverTime;
            ReconcileStructures(now);
        }

        public void AttackTelegraphed(int castId, in WolbaekTalismanAttack attack, double now)
        {
            WolbaekTalismanAttack pending = attack;
            if (_gate.MustDefer(attack.TelegraphServerTime, now))
            {
                _gate.Defer(attack.TelegraphServerTime, now, () => AttackTelegraphed(castId, in pending, Now()));
                return;
            }
            if ((!SameCast(castId)) || (_counter) || (attack.AttackId <= _lastStruckAttackId)
                || ((_hasAttack) && (attack.AttackId <= _attackId))
                || (attack.StrikeServerTime <= now) || (attack.TelegraphServerTime > now))
            {
                return;
            }
            ClearRange();
            _attack = attack;
            _attackId = attack.AttackId;
            _hasAttack = true;
            _attackMotionStarted = false;
            _attackSpeedReset = false;
            _attackWasStruck = false;
            CreateRange(in attack);
        }

        public void AttackStruck(int castId, int attackId, double strikeServerTime, double now)
        {
            if ((!SameCast(castId)) || (attackId <= _lastStruckAttackId) || (!_hasAttack)
                || (attackId != _attackId))
            {
                return;
            }
            _lastStruckAttackId = attackId;
            _attackWasStruck = true;
            if ((strikeServerTime >= _restoreTime) && (now - strikeServerTime <= STRIKE_GRACE_SECONDS))
            {
                SpawnStrikeEffect(in _attack, strikeServerTime);
            }
        }

        public void AttackCancelled(int castId, int attackId)
        {
            if ((!SameCast(castId)) || (!_hasAttack) || (attackId != _attackId))
            {
                return;
            }
            _hasAttack = false;
            _attackWasStruck = false;
            _lastStruckAttackId = Math.Max(_lastStruckAttackId, attackId);
            ClearRange();
            ResetMotionSpeed();
        }

        public void Succeeded(int castId, double serverTime, double counterEndServerTime, double now)
        {
            if (_gate.MustDefer(serverTime, now))
            {
                _gate.Defer(serverTime, now, () => Succeeded(castId, serverTime, counterEndServerTime, Now()));
                return;
            }
            if ((!SameCast(castId)) || (counterEndServerTime < serverTime) || (serverTime > now))
            {
                return;
            }
            _counter = true;
            _holding = false;
            _resetting = false;
            _hasAttack = false;
            _attackWasStruck = false;
            _phaseStart = serverTime;
            _counterEnd = counterEndServerTime;
            _idleStarted = false;
            ClearRange();
            ClearEffects();
            StopShield();
            ReconcileStructures(now);
            if (now < counterEndServerTime - SUCCESS_IDLE_LEAD_SECONDS)
            {
                PlayMotion(SUCCESS_STATE, 1f, SUCCESS_ENTER_BLEND_SECONDS);
            }
            else
            {
                PlayMotion(IDLE_STATE, 1f, SUCCESS_EXIT_BLEND_SECONDS);
                _idleStarted = true;
            }
        }

        public void CounterEnded(int castId)
        {
            if (!SameCast(castId))
            {
                return;
            }
            PlayMotion(IDLE_STATE, 1f, SUCCESS_EXIT_BLEND_SECONDS);
            Clear();
        }

        public void Clear()
        {
            _gate.Clear();
            if (_active) _lastEndedCastId = Math.Max(_lastEndedCastId, _castId);
            _active = false;
            _holding = false;
            _resetting = false;
            _counter = false;
            _hasAttack = false;
            _attackWasStruck = false;
            _castId = 0;
            _attackId = 0;
            _lastStruckAttackId = 0;
            _restoreTime = 0d;
            _sequence = 0;
            ResetMotionSpeed();
            ClearRange();
            StopShield(true);
            _tails.Clear();
            for (int i = 0; i < STRUCTURE_COUNT; ++i)
            {
                if (_structures[i] != null)
                {
                    _structures[i].Stop();
                    Destroy(_structures[i].gameObject);
                    _structures[i] = null;
                }
                _playbackIds[i] = 0;
            }
            ClearEffects();
        }

        private void ClearEffects()
        {
            for (int i = 0; i < _effects.Count; ++i)
            {
                if (_effects[i] != null)
                {
                    Destroy(_effects[i]);
                }
            }
            _effects.Clear();
            _beams.Clear();
        }

        /// <summary> 정리 사건을 먼저 받은 시전도 종료 번호로 기억해 늦은 시작 사건을 버린다 </summary>
        public void ClearCast(int castId)
        {
            if (_active && (_castId == castId)) Clear();
            _lastEndedCastId = Math.Max(_lastEndedCastId, castId);
        }

        /// <summary> 새 전투 또는 새 복제 세션에서만 이전 시전의 종료 번호를 지운다 </summary>
        public void ResetNetworkSession()
        {
            _gate.Clear();
            Clear();
            _lastEndedCastId = 0;
        }

        public void Tick(double now)
        {
            if (!_active)
            {
                return;
            }
            if ((_holding) || (_resetting))
            {
                ReconcileStructures(now);
            }
            else
            {
                for (int i = 0; i < STRUCTURE_COUNT; ++i)
                {
                    if (_structures[i] != null)
                    {
                        _structures[i].Tick(_playbackIds[i], now);
                    }
                }
            }
            FaceGlyphs();
            if (_hasAttack)
            {
                TickAttack(now);
            }
            if (_counter && (!_idleStarted) && (now >= _counterEnd - SUCCESS_IDLE_LEAD_SECONDS))
            {
                _idleStarted = true;
                PlayMotion(IDLE_STATE, 1f, SUCCESS_EXIT_BLEND_SECONDS);
            }
            TickEffects(now);
        }

        /// <summary> 문양이 향할 카메라를 넘긴다. 비우면 다음 기믹 시작 때 주 카메라를 한 번 찾는다 </summary>
        public void SetViewer(Transform viewer)
        {
            _viewer = viewer;
        }

        // 시험 씬처럼 구조물 문양이 보는 쪽을 향하게 한다
        private void FaceGlyphs()
        {
            if (_viewer == null)
            {
                return;
            }

            Vector3 viewerPosition = _viewer.position;
            for (int i = 0; i < STRUCTURE_COUNT; ++i)
            {
                if ((_structures[i] != null) && (_structures[i].IsRunning))
                {
                    _structures[i].FaceGlyph(_playbackIds[i], viewerPosition);
                }
            }
        }

        // 서버가 구조물에 배정한 참가자 머리 위에 그 구조물과 같은 색·문양을 띄운다. 배정은 서버 값을 그대로 쓴다
        private void UpdateMemberMarks()
        {
            _staleMarks.Clear();
            foreach (KeyValuePair<int, GameObject> pair in _marks)
            {
                if (!_active || pair.Value == null || !_anchors.TryGetValue(pair.Key, out Transform held) || held == null)
                {
                    _staleMarks.Add(pair.Key);
                }
            }
            for (int i = 0; i < _staleMarks.Count; ++i)
            {
                if (_marks.TryGetValue(_staleMarks[i], out GameObject stale) && stale != null)
                {
                    Destroy(stale);
                }
                _marks.Remove(_staleMarks[i]);
            }
            if (!_active)
            {
                return;
            }

            for (int i = 0; i < STRUCTURE_COUNT; ++i)
            {
                if ((_structures[i] == null) || (!_anchors.TryGetValue(_placements[i].AssignedCombatId, out Transform anchor)) || (anchor == null))
                {
                    continue;
                }
                int combatId = _placements[i].AssignedCombatId;
                if (!_marks.TryGetValue(combatId, out GameObject mark) || mark == null)
                {
                    mark = CreateMemberMark(_placements[i]);
                    if (mark == null)
                    {
                        continue;
                    }
                    _marks[combatId] = mark;
                }

                Vector3 position = anchor.position + (Vector3.up * MARK_HEIGHT);
                mark.transform.position = position;
                if (_viewer != null)
                {
                    Vector3 toViewer = _viewer.position - position;
                    if (toViewer.sqrMagnitude > MIN_FACE_DISTANCE_SQR)
                    {
                        mark.transform.rotation = Quaternion.LookRotation(-toViewer, Vector3.up);
                    }
                }
            }
        }

        // 구조물 프리팹의 글리프 메시와 재질을 그대로 복제해 구조물과 같은 모양을 쓴다
        private GameObject CreateMemberMark(WolbaekTalismanStructure placement)
        {
            int symbol = placement.SymbolId;
            if ((symbol < 0) || (symbol >= STRUCTURE_COUNT) || (_structurePrefabs == null) || (_structurePrefabs[symbol] == null)
                || ((int)placement.Color < 0) || ((int)placement.Color >= _colors.Length))
            {
                return null;
            }

            string glyphName = GLYPH_NAME_PREFIX + _glyphNames[symbol];
            MeshRenderer source = null;
            MeshRenderer[] renderers = _structurePrefabs[symbol].GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
                if (renderers[i].name == glyphName)
                {
                    source = renderers[i];
                    break;
                }
            }
            if ((source == null) || (!source.TryGetComponent(out MeshFilter filter)) || (filter.sharedMesh == null))
            {
                return null;
            }

            Vector3 size = filter.sharedMesh.bounds.size;
            float extent = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (extent <= 0f)
            {
                return null;
            }

            var root = new GameObject("WolbaekTalismanMemberMark");
            var glyph = new GameObject(glyphName);
            glyph.transform.SetParent(root.transform, false);
            glyph.transform.localRotation = source.transform.localRotation;
            float scale = MARK_SIZE / extent;
            glyph.transform.localScale = Vector3.one * scale;
            // 메시 중심이 원점에서 벗어나 있어, 구조물에서는 글리프 부모가 상쇄하던 오프셋을 여기서 직접 없앤다
            glyph.transform.localPosition = -(glyph.transform.localRotation * (filter.sharedMesh.bounds.center * scale));
            glyph.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            MeshRenderer renderer = glyph.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = source.sharedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (_markBlock == null)
            {
                _markBlock = new MaterialPropertyBlock();
            }
            _markBlock.Clear();
            _markBlock.SetColor(_markColorId, _colors[(int)placement.Color]);
            renderer.SetPropertyBlock(_markBlock);
            return root;
        }

        private bool SameCast(int castId)
        {
            return _active && (castId == _castId);
        }

        private bool ValidStart(in WolbaekTalismanStart start)
        {
            if ((start.CastInstanceId <= 0) || (start.HoldSeconds <= 0f)
                || (start.Structures == null) || (start.Structures.Count < 1) || (start.Structures.Count > STRUCTURE_COUNT)
                || (_structurePrefabs == null) || (_structurePrefabs.Length != STRUCTURE_COUNT))
            {
                return false;
            }
            for (int i = 0; i < start.Structures.Count; ++i)
            {
                WolbaekTalismanStructure structure = start.Structures[i];
                if ((structure.SymbolId < 0) || (structure.SymbolId >= STRUCTURE_COUNT)
                    || (_structurePrefabs[structure.SymbolId] == null) || (structure.Radius <= 0f)
                    || ((int)structure.Color < 0) || ((int)structure.Color >= _colors.Length))
                {
                    return false;
                }
            }
            return true;
        }

        private WolbaekTalismanPlayer.Snapshot BuildStructureSnapshot(int index, WolbaekTalismanPlayer.EPhase phase,
            double phaseStart, double now, float progress)
        {
            WolbaekTalismanStructure placement = _placements[index];
            EWolbaekTalismanSign sign = placement.SymbolId == 0 ? EWolbaekTalismanSign.Flower
                : placement.SymbolId == 1 ? EWolbaekTalismanSign.Moon
                : placement.SymbolId == 2 ? EWolbaekTalismanSign.Star : EWolbaekTalismanSign.Diamond;
            return new WolbaekTalismanPlayer.Snapshot((ulong)_castId, _sequence, sign, _colors[(int)placement.Color],
                placement.Center, placement.Radius, _start.StartServerTime, now, phase, phaseStart, progress);
        }

        private void ReconcileStructures(double now)
        {
            ++_sequence;
            float progress = _holding ? Mathf.Clamp01((float)((now - _holdStart) / _start.HoldSeconds)) : 0f;
            WolbaekTalismanPlayer.EPhase phase = _counter ? WolbaekTalismanPlayer.EPhase.Success
                : _resetting ? WolbaekTalismanPlayer.EPhase.Reset : WolbaekTalismanPlayer.EPhase.Holding;
            if (_counter)
            {
                progress = 1f;
            }
            for (int i = 0; i < STRUCTURE_COUNT; ++i)
            {
                if ((_structures[i] != null) && (_playbackIds[i] != 0))
                {
                    _structures[i].Reconcile(_playbackIds[i], BuildStructureSnapshot(i, phase, _phaseStart, now, progress), now);
                }
            }
        }

        private void BeginShield()
        {
            if ((_shieldPrefab == null) || (_animator == null))
            {
                return;
            }
            _shield = Instantiate(_shieldPrefab, _animator.transform);
            _shield.transform.localPosition = Vector3.up * SHIELD_HEIGHT;
            _shield.transform.localRotation = Quaternion.identity;
            _shield.transform.localScale = _shieldPrefab.transform.localScale * SHIELD_SCALE;
        }

        private void StopShield(bool immediate = false)
        {
            if (_shield == null)
            {
                return;
            }
            if (immediate)
            {
                ParticleSystem[] particles = _shield.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < particles.Length; ++i)
                {
                    particles[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
                Destroy(_shield, 0f);
                _shield = null;
                return;
            }

            // 새 파티클을 멈추고 남은 파티클을 제거 시각에 투명해지도록 낮춘다. 사망, 취소, Reset은 _tails가 즉시 지운다
            WolbaekShieldFade.Begin(_shield, SHIELD_FADE_SECONDS);
            _tails.Keep(_shield);
            Destroy(_shield, SHIELD_FADE_SECONDS);
            _shield = null;
        }

        private void PlayMotion(string state, float speed, float blend)
        {
            if ((_bossView != null) && (_animator != null))
            {
                _bossView.PlayGimmickMotion(state, speed, blend);
            }
        }

        private void ResetMotionSpeed()
        {
            if (_animator != null)
            {
                _animator.speed = 1f;
            }
        }

        private void TickAttack(double now)
        {
            double lead = _attack.Kind == EWolbaekTalismanAttackKind.Ring ? RING_PEAK_SECONDS : LINE_PEAK_SECONDS;
            double telegraph = _attack.StrikeServerTime - _attack.TelegraphServerTime;
            float speed = telegraph < lead ? (float)(lead / telegraph) : 1f;
            double motionStart = _attack.StrikeServerTime - (lead / speed);
            if ((!_attackMotionStarted) && (now >= motionStart) && (now < _attack.StrikeServerTime))
            {
                _attackMotionStarted = true;
                PlayMotion(_attack.Kind == EWolbaekTalismanAttackKind.Ring ? RING_STATE : LINE_STATE, speed, 0.05f);
            }
            if ((!_attackSpeedReset) && (now >= _attack.StrikeServerTime + ATTACK_RECOVERY_SECONDS))
            {
                _attackSpeedReset = true;
                ResetMotionSpeed();
            }
            if (now >= _attack.StrikeServerTime + RANGE_FADE_SECONDS)
            {
                ClearRange();
                if ((_attackWasStruck) && (_attackSpeedReset))
                {
                    _hasAttack = false;
                }
            }
            else if (_rangeMaterial != null)
            {
                float alpha = now < _attack.StrikeServerTime ? Mathf.Lerp(0.3f, 0.5f,
                    (float)((now - _attack.TelegraphServerTime) / telegraph))
                    : Mathf.Lerp(0.75f, 0f, (float)((now - _attack.StrikeServerTime) / RANGE_FADE_SECONDS));
                _rangeMaterial.color = new Color(1f, 0.2f, 0.25f, alpha);
            }
        }

        private void CreateRange(in WolbaekTalismanAttack attack)
        {
            _range = new GameObject("WolbaekTalismanAttackRange");
            _range.transform.position = attack.Origin + (Vector3.up * RANGE_HEIGHT);
            if (attack.Kind == EWolbaekTalismanAttackKind.Ring)
            {
                _rangeMesh = BuildRingMesh(attack.InnerRadius, attack.OuterRadius);
            }
            else
            {
                _range.transform.rotation = Quaternion.LookRotation(attack.Direction);
                _rangeMesh = BuildLineMesh(attack.Length, attack.Width);
            }
            _range.AddComponent<MeshFilter>().sharedMesh = _rangeMesh;
            _rangeMaterial = new Material(Shader.Find(RANGE_SHADER));
            _rangeMaterial.color = new Color(1f, 0.2f, 0.25f, 0.3f);
            _range.AddComponent<MeshRenderer>().sharedMaterial = _rangeMaterial;
        }

        private static Mesh BuildRingMesh(float inner, float outer)
        {
            var mesh = new Mesh();
            var vertices = new Vector3[(RING_SEGMENTS + 1) * 2];
            var triangles = new int[RING_SEGMENTS * 6];
            for (int i = 0; i <= RING_SEGMENTS; ++i)
            {
                float angle = Mathf.PI * 2f * i / RING_SEGMENTS;
                vertices[i * 2] = new Vector3(Mathf.Cos(angle) * inner, 0f, Mathf.Sin(angle) * inner);
                vertices[(i * 2) + 1] = new Vector3(Mathf.Cos(angle) * outer, 0f, Mathf.Sin(angle) * outer);
            }
            for (int i = 0; i < RING_SEGMENTS; ++i)
            {
                int v = i * 2;
                int t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 1;
                triangles[t + 4] = v + 3;
                triangles[t + 5] = v + 2;
            }
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            return mesh;
        }

        private static Mesh BuildLineMesh(float length, float width)
        {
            float half = width * 0.5f;
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-half, 0f, 0f), new Vector3(half, 0f, 0f),
                new Vector3(-half, 0f, length), new Vector3(half, 0f, length) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            return mesh;
        }

        private void ClearRange()
        {
            if (_range != null) Destroy(_range);
            if (_rangeMesh != null) Destroy(_rangeMesh);
            if (_rangeMaterial != null) Destroy(_rangeMaterial);
            _range = null;
            _rangeMesh = null;
            _rangeMaterial = null;
        }

        private void SpawnStrikeEffect(in WolbaekTalismanAttack attack, double strikeServerTime)
        {
            if ((attack.Kind == EWolbaekTalismanAttackKind.Ring) && (_ringEffectPrefab != null))
            {
                GameObject effect = Instantiate(_ringEffectPrefab, attack.Origin + (Vector3.up * RING_HEIGHT), Quaternion.identity);
                effect.transform.localScale = _ringEffectPrefab.transform.localScale * (attack.OuterRadius * RING_SCALE_PER_METRE);
                _effects.Add(effect);
                Destroy(effect, EFFECT_LIFE_SECONDS);
            }
            else if ((attack.Kind == EWolbaekTalismanAttackKind.Line) && (_lineEffectPrefab != null))
            {
                WolbaekBeamPlayer beam = Instantiate(_lineEffectPrefab);
                uint id = beam.BeginCast((ulong)_castId, strikeServerTime);
                var shape = new WolbaekBeamShape(attack.Origin + (Vector3.up * LINE_HEIGHT),
                    attack.Origin + (Vector3.up * LINE_HEIGHT) + (attack.Direction * attack.Length), attack.Width);
                double end = strikeServerTime + BEAM_ACTIVE_SECONDS;
                beam.Warn(id, 0, 1, shape, strikeServerTime, strikeServerTime, end, strikeServerTime);
                beam.ConfirmActive(id, 0, 1, strikeServerTime, end, strikeServerTime);
                _effects.Add(beam.gameObject);
                _beams.Add(beam);
                Destroy(beam.gameObject, BEAM_ACTIVE_SECONDS + BEAM_LINGER_SECONDS);
            }
        }

        private void TickEffects(double now)
        {
            for (int i = _effects.Count - 1; i >= 0; --i)
            {
                if (_effects[i] == null)
                {
                    _effects.RemoveAt(i);
                }
            }
            for (int i = _beams.Count - 1; i >= 0; --i)
            {
                if (_beams[i] == null)
                {
                    _beams.RemoveAt(i);
                }
                else
                {
                    _beams[i].Tick(_beams[i].PlaybackId, now);
                }
            }
        }
    }
}
