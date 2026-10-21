#if UNITY_EDITOR
// System
using System;

// Unity
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    /// <summary> 무철 패턴의 로컬 연출 초안. 서버 판정 없이 저장된 모션/VFX 시간표만 샘플링한다. </summary>
    public sealed class MucheolPatternPreview : MonoBehaviour
    {
        /****************************************
        *              Nested Types
        ****************************************/

        [Serializable]
        public sealed class MotionCue
        {
            [SerializeField] private AnimationClip _clip;
            [SerializeField] private float _start;
            [SerializeField] private float _impact;
            [SerializeField] private float _end;
            [SerializeField] private float _clipStart;
            [SerializeField] private float _clipImpact;
            [SerializeField] private float _clipEnd;
            [SerializeField] private bool _loop;
            [SerializeField] private bool _blendFromPrevious;
            [SerializeField, Tooltip("X: 패턴 경과 초, Y: 클립 초. 연타를 한 클립으로 이어 재생할 때 사용한다.")]
            private Vector2[] _timeKeys = Array.Empty<Vector2>();
            [SerializeField] private Vector3 _from;
            [SerializeField] private Vector3 _to;
            [SerializeField] private float _moveStart;
            [SerializeField] private float _moveEnd;
            [SerializeField] private float _yaw;

            public AnimationClip Clip => _clip;
            public float Start => _start;
            public float Impact => _impact;
            public float End => _end;
            public Vector3 From => _from;
            public Vector3 To => _to;
            public float MoveStart => _moveStart;
            public float MoveEnd => _moveEnd;
            public float Yaw => _yaw;
            public bool BlendFromPrevious => _blendFromPrevious;

            public MotionCue(AnimationClip clip, float start, float impact, float end,
                float clipStart, float clipImpact, float clipEnd, Vector3 from, Vector3 to,
                float moveStart = 0f, float moveEnd = 0f, float yaw = 0f)
            {
                _clip = clip;
                _start = start;
                _impact = impact;
                _end = end;
                _clipStart = clipStart;
                _clipImpact = clipImpact;
                _clipEnd = clipEnd;
                _from = from;
                _to = to;
                _moveStart = moveStart;
                _moveEnd = moveEnd;
                _yaw = yaw;
            }

            public float ClipTime(float now)
            {
                if (_loop && _clipEnd > _clipStart)
                {
                    return _clipStart + Mathf.Repeat(now - _start, _clipEnd - _clipStart);
                }
                // 연타마다 클립을 다시 시작하지 않고, 같은 클립 안에서 타격 시점 사이의 속도만 조절한다.
                if (_timeKeys != null && _timeKeys.Length > 1)
                {
                    if (now <= _timeKeys[0].x)
                    {
                        return _timeKeys[0].y;
                    }
                    for (int i = 1; i < _timeKeys.Length; ++i)
                    {
                        if (now <= _timeKeys[i].x)
                        {
                            return Mathf.Lerp(_timeKeys[i - 1].y, _timeKeys[i].y,
                                Mathf.InverseLerp(_timeKeys[i - 1].x, _timeKeys[i].x, now));
                        }
                    }
                    return _timeKeys[_timeKeys.Length - 1].y;
                }
                return now <= _impact
                    ? Mathf.Lerp(_clipStart, _clipImpact, Mathf.InverseLerp(_start, _impact, now))
                    : Mathf.Lerp(_clipImpact, _clipEnd, Mathf.InverseLerp(_impact, _end, now));
            }
        }

        [Serializable]
        public sealed class EffectCue
        {
            [SerializeField] private int _sourceIndex;
            [SerializeField] private float _start;
            [SerializeField] private float _impact;
            [SerializeField] private float _moveStart;
            [SerializeField] private Vector3 _position;
            [SerializeField] private float _yaw;
            [SerializeField] private Vector3 _scale;

            public int SourceIndex => _sourceIndex;
            public float Start => _start;
            public float Impact => _impact;
            public float MoveStart => _moveStart;
            public Vector3 Position => _position;
            public float Yaw => _yaw;
            public Vector3 Scale => _scale;

            public EffectCue(int sourceIndex, float start, float impact, Vector3 position,
                float yaw, Vector3 scale, float moveStart = -1f)
            {
                _sourceIndex = sourceIndex;
                _start = start;
                _impact = impact;
                _position = position;
                _yaw = yaw;
                _scale = scale;
                _moveStart = moveStart;
            }
        }

        [Serializable]
        public sealed class Pattern
        {
            [SerializeField] private string _label;
            [SerializeField] private int _phase;
            [SerializeField] private bool _hardOnly;
            [SerializeField, TextArea] private string _description;
            [SerializeField] private MotionCue[] _motions;
            [SerializeField] private EffectCue[] _effects;
            [SerializeField] private float _recoveryEnd;
            [SerializeField] private float _cameraDistance;
            [SerializeField] private bool _isCandidate;
            [SerializeField] private int _gimmickKind;

            public string Label => _label;
            public int Phase => _phase;
            public bool HardOnly => _hardOnly;
            public string Description => _description;
            public MotionCue[] Motions => _motions;
            public EffectCue[] Effects => _effects;
            public float RecoveryEnd => _recoveryEnd;
            public float CameraDistance => _cameraDistance;
            public bool IsCandidate => _isCandidate;
            public int GimmickKind => _gimmickKind;

            public Pattern(string label, int phase, bool hardOnly, string description,
                MotionCue[] motions, EffectCue[] effects, float recoveryEnd, float cameraDistance, bool isCandidate = false)
            {
                _label = label;
                _phase = phase;
                _hardOnly = hardOnly;
                _description = description;
                _motions = motions;
                _effects = effects;
                _recoveryEnd = recoveryEnd;
                _cameraDistance = cameraDistance;
                _isCandidate = isCandidate;
            }
        }

        private sealed class EffectInstance
        {
            public GameObject Root { get; set; }
            public MucheolBatchVfxPlayer Batch { get; set; }
            public MucheolImpactVfxPlayer Impact { get; set; }
            public float Telegraph { get; set; }
            public float Charge { get; set; }
            public float Tail { get; set; }
        }

        /****************************************
        *                Fields
        ****************************************/

        private const float EPSILON = 0.001f;
        private const float PANEL_WIDTH = 340f;
        private const float MARGIN = 10f;
        private const float FLOOR_Y = 0.025f;
        private const float BLEND_SECONDS = 0.12f;
        private const float MIN_SPEED = 0.1f;
        private const float MAX_SPEED = 2f;
        private const float MIN_CAMERA_DISTANCE = 8f;
        private const float MAX_CAMERA_DISTANCE = 70f;
        private static readonly string[] G_Difficulties = { "이지", "하드" };
        private static readonly string[] G_Phases = { "1페이즈", "2페이즈" };
        private static readonly string[] G_Modes = { "패턴 재생", "P01 후보 비교", "기믹" };

        [SerializeField] private Animator _animator;
        [SerializeField] private AnimationClip _idle;
        [SerializeField] private GameObject[] _effectTemplates;
        [SerializeField] private Camera _camera;
        [SerializeField] private Pattern[] _patterns;
        [SerializeField] private int _selection;
        [SerializeField] private int _phase = 1;
        [SerializeField] private bool _hard;
        [SerializeField] private bool _repeat;
        [SerializeField] private bool _playOnStart = true;
        [SerializeField, Range(MIN_SPEED, MAX_SPEED)] private float _speed = 1f;
        [SerializeField] private bool _showEffects = true;
        [SerializeField] private bool _candidateMode;
        [SerializeField] private bool _frontCamera = true;
        [SerializeField] private bool _gimmickMode;
        [SerializeField] private GameObject _gimmickProps;
        [SerializeField] private GameObject[] _pillars = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] _rubble = Array.Empty<GameObject>();
        [SerializeField] private float[] _pillarBreakTimes = Array.Empty<float>();
        [SerializeField] private MucheolSealPreview _sealPreview;

        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private AnimationClipPlayable _idlePlayable;
        private AnimationClipPlayable _actionPlayable;
        private AnimationClipPlayable _previousPlayable;
        private AnimationClip _previousClip;
        private AnimationClip _activeClip;
        private EffectInstance[] _effects = Array.Empty<EffectInstance>();
        private Vector3 _origin;
        private Quaternion _originRotation;
        private Vector3 _cameraFocus;
        private float _cameraDistance;
        private float _elapsed;
        private float _idleTime;
        private float _duration;
        private bool _running;
        private bool _paused;
        private bool _initialized;
        private Vector2 _scroll;
        private GUIStyle _wrappedLabel;
        private GUIStyle _wrappedButton;

        public int PatternCount => _patterns.Length;
        public int Selection => _selection;
        public float Elapsed => _elapsed;
        public float Duration => _duration;
        public bool IsRunning => _running;
        public bool IsPaused => _paused;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            _origin = _animator.transform.position;
            _originRotation = _animator.transform.rotation;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _initialized = true;
            SelectPattern(Mathf.Clamp(_selection, 0, _patterns.Length - 1));
            if (_playOnStart)
            {
                Play();
            }
        }

        private void OnEnable()
        {
            if (_initialized && Application.isPlaying)
            {
                SelectPattern(_selection);
            }
        }

        private void Update()
        {
            if (!_running)
            {
                _idleTime += Time.unscaledDeltaTime;
                SampleIdle();
                return;
            }
            if (_paused)
            {
                return;
            }
            _elapsed += Time.unscaledDeltaTime * _speed;
            if (_elapsed > _duration)
            {
                if (_repeat)
                {
                    Play();
                }
                else
                {
                    Stop();
                }
                return;
            }
            Sample();
        }

        private void OnDisable()
        {
            _running = false;
            if (_sealPreview != null)
            {
                _sealPreview.Sample(0f, false);
            }
            if (_gimmickProps != null)
            {
                _gimmickProps.SetActive(false);
            }
            DestroyEffects();
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
            _activeClip = null;
            _previousClip = null;
            if (_initialized && _animator != null)
            {
                _animator.transform.SetPositionAndRotation(_origin, _originRotation);
            }
        }

        private void OnGUI()
        {
            if (!_initialized)
            {
                return;
            }
            if (_wrappedLabel == null)
            {
                _wrappedLabel = new GUIStyle(GUI.skin.label) { wordWrap = true };
                _wrappedButton = new GUIStyle(GUI.skin.button) { wordWrap = true };
            }
            float width = Mathf.Min(PANEL_WIDTH, Screen.width * 0.37f);
            float left = width + MARGIN * 2f;
            _camera.rect = new Rect(left / Screen.width, 0f, 1f - left / Screen.width, 1f);
            Color previous = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, left, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
            GUILayout.BeginArea(new Rect(MARGIN, MARGIN, width, Screen.height - MARGIN * 2f));
            GUILayout.Label("무철 패턴 프리뷰 · 배정 초안", _wrappedLabel);
            int currentMode = _gimmickMode ? 2 : (_candidateMode ? 1 : 0);
            int mode = GUILayout.Toolbar(currentMode, G_Modes);
            if (mode != currentMode)
            {
                _candidateMode = mode == 1;
                _gimmickMode = mode == 2;
                if (!_gimmickMode)
                {
                    _phase = Mathf.Clamp(_phase, 1, 2);
                }
                _speed = 1f;
                SelectFirstVisible();
            }
            if (!_candidateMode && !_gimmickMode)
            {
                int difficulty = GUILayout.Toolbar(_hard ? 1 : 0, G_Difficulties);
                int phase = GUILayout.Toolbar(_phase - 1, G_Phases) + 1;
                if (difficulty != (_hard ? 1 : 0) || phase != _phase)
                {
                    _hard = difficulty == 1;
                    _phase = phase;
                    SelectFirstVisible();
                }
            }
            else if (_candidateMode)
            {
                GUILayout.Label("모션만 비교 · 원본 동작 확인", _wrappedLabel);
                bool front = GUILayout.Toggle(_frontCamera, "정면 카메라 (끄면 사선)");
                if (front != _frontCamera)
                {
                    _frontCamera = front;
                    ResetCamera();
                }
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("재생"))
            {
                Play();
            }
            if (GUILayout.Button(_paused ? "계속" : "멈춤"))
            {
                _paused = !_paused;
            }
            if (GUILayout.Button("초기화"))
            {
                Stop();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(_elapsed.ToString("F2") + " / " + _duration.ToString("F2") + "초", _wrappedLabel);
            float seek = GUILayout.HorizontalSlider(_elapsed, 0f, _duration);
            if (!Mathf.Approximately(seek, _elapsed))
            {
                Seek(seek);
            }
            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _patterns.Length; ++i)
            {
                if (!IsVisible(_patterns[i]))
                {
                    continue;
                }
                string label = (i == _selection ? "▶ " : "") + _patterns[i].Label;
                if (GUILayout.Button(label, _wrappedButton, GUILayout.Width(width - MARGIN * 4f)))
                {
                    SelectPattern(i);
                    Play();
                }
            }
            GUILayout.Label(_patterns[_selection].Description, _wrappedLabel);
            if (_patterns[_selection].GimmickKind == 1)
            {
                int broken = 0;
                for (int i = 0; i < _pillarBreakTimes.Length; ++i)
                {
                    if (_running && _elapsed >= _pillarBreakTimes[i])
                    {
                        ++broken;
                    }
                }
                GUILayout.Label("구조물 파괴 " + broken + " / " + _pillars.Length, _wrappedLabel);
            }
            if (_patterns[_selection].GimmickKind == 2 && _sealPreview != null)
            {
                GUILayout.Label(_sealPreview.Describe(_elapsed), _wrappedLabel);
            }
            _repeat = GUILayout.Toggle(_repeat, "반복 재생");
            bool effects = _candidateMode ? _showEffects : GUILayout.Toggle(_showEffects, "VFX 표시");
            if (effects != _showEffects)
            {
                _showEffects = effects;
                if (_running)
                {
                    Sample();
                }
            }
            GUILayout.Label("재생 속도 " + _speed.ToString("F2") + "x", _wrappedLabel);
            _speed = GUILayout.HorizontalSlider(_speed, MIN_SPEED, MAX_SPEED);
            GUILayout.Label("카메라 거리", _wrappedLabel);
            float distance = GUILayout.HorizontalSlider(_cameraDistance, MIN_CAMERA_DISTANCE, MAX_CAMERA_DISTANCE);
            if (!Mathf.Approximately(distance, _cameraDistance))
            {
                _cameraDistance = distance;
                ResetCamera();
            }
            if (GUILayout.Button("카메라 원위치"))
            {
                FramePattern();
            }
            GUILayout.Label(_candidateMode
                ? "원본 속도는 1.00x입니다.\n후보 비교는 현재 패턴 배정을 자동으로 바꾸지 않습니다."
                : "WASD 카메라 이동 / Shift 빠르게\n로컬 타이밍 초안 · 서버 판정 미연결\n세부 시간·클립은 Inspector Patterns에서 조정", _wrappedLabel);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Configure(Animator animator, AnimationClip idle, GameObject[] templates, Camera camera, Pattern[] patterns)
        {
            _animator = animator;
            _idle = idle;
            _effectTemplates = templates;
            _camera = camera;
            _patterns = patterns;
        }

        public void SelectPattern(int index)
        {
            if (index < 0 || index >= _patterns.Length)
            {
                return;
            }
            Stop();
            DestroyEffects();
            _selection = index;
            Pattern pattern = _patterns[index];
            _candidateMode = pattern.IsCandidate;
            _gimmickMode = pattern.Phase == 0;
            _phase = pattern.Phase;
            if (pattern.HardOnly)
            {
                _hard = true;
            }
            _duration = pattern.RecoveryEnd;
            _effects = new EffectInstance[pattern.Effects.Length];
            for (int i = 0; i < _effects.Length; ++i)
            {
                EffectCue cue = pattern.Effects[i];
                GameObject root = Instantiate(_effectTemplates[cue.SourceIndex], transform);
                root.name = "PreviewCue_" + i;
                root.transform.SetPositionAndRotation(_origin + cue.Position + Vector3.up * (FLOOR_Y - _origin.y),
                    Quaternion.Euler(0f, cue.Yaw, 0f));
                root.transform.localScale = cue.Scale;
                var instance = new EffectInstance
                {
                    Root = root,
                    Batch = root.GetComponentInChildren<MucheolBatchVfxPlayer>(true),
                    Impact = root.GetComponentInChildren<MucheolImpactVfxPlayer>(true)
                };
                // 원본 프로필은 읽기만 한다. 공유 SO의 시간을 바꾸지 않고 로컬 시각을 변환한다.
                var serialized = new UnityEditor.SerializedObject(instance.Impact);
                var profile = (MucheolPatternVfxSettingsSO)serialized.FindProperty("_profile").objectReferenceValue;
                instance.Telegraph = profile.TelegraphSeconds;
                instance.Charge = instance.Batch != null ? instance.Batch.ChargeSeconds : 0f;
                instance.Tail = Mathf.Max(profile.ExpansionSeconds + profile.HoldSeconds + profile.FadeSeconds, profile.DebrisSeconds);
                _effects[i] = instance;
                _duration = Mathf.Max(_duration, cue.Impact + instance.Tail);
            }
            Stop();
            FramePattern();
        }

        public void Play()
        {
            _elapsed = 0f;
            _paused = false;
            _running = true;
            Sample();
        }

        public void Stop()
        {
            _running = false;
            _paused = false;
            _elapsed = 0f;
            _idleTime = 0f;
            SampleStructures();
            for (int i = 0; i < _effects.Length; ++i)
            {
                if (_effects[i].Root != null)
                {
                    _effects[i].Root.SetActive(false);
                }
            }
            if (_initialized)
            {
                SampleIdle();
                _animator.transform.position = _origin + _patterns[_selection].Motions[0].From;
                _animator.transform.rotation = _originRotation;
            }
        }

        public void Seek(float time)
        {
            _elapsed = Mathf.Clamp(time, 0f, _duration);
            _paused = true;
            _running = true;
            Sample();
        }

        private bool IsVisible(Pattern pattern)
        {
            if (_gimmickMode)
            {
                return pattern.Phase == 0;
            }
            return pattern.IsCandidate == _candidateMode
                && (_candidateMode || (pattern.Phase == _phase && (!pattern.HardOnly || _hard)));
        }

        private void SelectFirstVisible()
        {
            for (int i = 0; i < _patterns.Length; ++i)
            {
                if (IsVisible(_patterns[i]))
                {
                    SelectPattern(i);
                    return;
                }
            }
        }

        private void EnsureGraph()
        {
            if (_graph.IsValid())
            {
                return;
            }
            _graph = PlayableGraph.Create("Mucheol pattern preview");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            _mixer = AnimationMixerPlayable.Create(_graph, 3);
            _idlePlayable = AnimationClipPlayable.Create(_graph, _idle);
            _idlePlayable.SetApplyFootIK(false);
            _graph.Connect(_idlePlayable, 0, _mixer, 0);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Pattern", _animator);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
        }

        private void SampleIdle()
        {
            EnsureGraph();
            _idlePlayable.SetTime(_idleTime % _idle.length);
            _mixer.SetInputWeight(0, 1f);
            _mixer.SetInputWeight(1, 0f);
            _mixer.SetInputWeight(2, 0f);
            _graph.Evaluate(0f);
        }

        private void Sample()
        {
            EnsureGraph();
            Pattern pattern = _patterns[_selection];
            MotionCue active = null;
            int activeIndex = -1;
            Vector3 position = pattern.Motions[0].From;
            Quaternion rotation = _originRotation;
            for (int i = 0; i < pattern.Motions.Length; ++i)
            {
                MotionCue cue = pattern.Motions[i];
                if (_elapsed < cue.Start)
                {
                    continue;
                }
                if (_elapsed <= cue.End)
                {
                    active = cue;
                    activeIndex = i;
                }
                float turn = Mathf.InverseLerp(cue.Start, Mathf.Min(cue.Impact, cue.Start + BLEND_SECONDS), _elapsed);
                rotation = Quaternion.Slerp(rotation, Quaternion.Euler(0f, cue.Yaw, 0f), turn);
                position = cue.MoveEnd > cue.MoveStart
                    ? Vector3.Lerp(cue.From, cue.To, Mathf.InverseLerp(cue.MoveStart, cue.MoveEnd, _elapsed))
                    : cue.To;
            }
            _idlePlayable.SetTime(_elapsed % _idle.length);
            float weight = 0f;
            float previousWeight = 0f;
            if (active != null)
            {
                if (_activeClip != active.Clip)
                {
                    if (_actionPlayable.IsValid())
                    {
                        _graph.Disconnect(_mixer, 1);
                        _graph.DestroyPlayable(_actionPlayable);
                    }
                    _actionPlayable = AnimationClipPlayable.Create(_graph, active.Clip);
                    _actionPlayable.SetApplyFootIK(false);
                    _graph.Connect(_actionPlayable, 0, _mixer, 1);
                    _activeClip = active.Clip;
                }
                _actionPlayable.SetTime(active.ClipTime(_elapsed));
                bool continuousIn = active.BlendFromPrevious && activeIndex > 0;
                bool continuousOut = activeIndex + 1 < pattern.Motions.Length
                    && pattern.Motions[activeIndex + 1].BlendFromPrevious
                    && Mathf.Abs(pattern.Motions[activeIndex + 1].Start - active.End) < EPSILON;
                weight = Mathf.Min(continuousIn ? 1f : Mathf.Clamp01((_elapsed - active.Start) / BLEND_SECONDS),
                    continuousOut ? 1f : Mathf.Clamp01((active.End - _elapsed) / BLEND_SECONDS));
                if (continuousIn && _elapsed < active.Start + BLEND_SECONDS)
                {
                    // 연결 구간은 Idle을 거치지 않고 이전 클립의 마지막 자세와 직접 섞는다.
                    MotionCue previous = pattern.Motions[activeIndex - 1];
                    if (_previousClip != previous.Clip)
                    {
                        if (_previousPlayable.IsValid())
                        {
                            _graph.Disconnect(_mixer, 2);
                            _graph.DestroyPlayable(_previousPlayable);
                        }
                        _previousPlayable = AnimationClipPlayable.Create(_graph, previous.Clip);
                        _previousPlayable.SetApplyFootIK(false);
                        _graph.Connect(_previousPlayable, 0, _mixer, 2);
                        _previousClip = previous.Clip;
                    }
                    _previousPlayable.SetTime(previous.ClipTime(previous.End));
                    previousWeight = 1f - Mathf.Clamp01((_elapsed - active.Start) / BLEND_SECONDS);
                    weight = 1f - previousWeight;
                }
            }
            _mixer.SetInputWeight(0, 1f - weight - previousWeight);
            _mixer.SetInputWeight(1, weight);
            _mixer.SetInputWeight(2, previousWeight);
            _graph.Evaluate(0f);
            _animator.transform.SetPositionAndRotation(_origin + position, rotation);
            SampleStructures();
            for (int i = 0; i < _effects.Length; ++i)
            {
                SampleEffect(pattern.Effects[i], _effects[i]);
            }
        }

        private void SampleEffect(EffectCue cue, EffectInstance effect)
        {
            bool visible = _showEffects && _elapsed >= cue.Start && _elapsed <= cue.Impact + effect.Tail;
            effect.Root.SetActive(visible);
            if (!visible)
            {
                return;
            }
            float sampleTime;
            if (_elapsed >= cue.Impact)
            {
                sampleTime = effect.Telegraph + effect.Charge + _elapsed - cue.Impact;
            }
            else if (cue.MoveStart >= cue.Start && effect.Charge > EPSILON)
            {
                sampleTime = _elapsed < cue.MoveStart
                    ? effect.Telegraph * Mathf.InverseLerp(cue.Start, cue.MoveStart, _elapsed)
                    : effect.Telegraph + effect.Charge * Mathf.InverseLerp(cue.MoveStart, cue.Impact, _elapsed);
            }
            else
            {
                sampleTime = effect.Telegraph * Mathf.InverseLerp(cue.Start, cue.Impact, _elapsed);
            }
            if (effect.Batch != null)
            {
                effect.Batch.SamplePreview(sampleTime);
            }
            else
            {
                effect.Impact.SamplePreview(sampleTime);
            }
        }

        private void SampleStructures()
        {
            if (_sealPreview != null)
            {
                _sealPreview.Sample(_elapsed, _patterns[_selection].GimmickKind == 2 && _showEffects);
            }
            if (_gimmickProps == null)
            {
                return;
            }
            bool visible = _patterns[_selection].GimmickKind == 1;
            _gimmickProps.SetActive(visible);
            for (int i = 0; i < _pillars.Length; ++i)
            {
                // 프리뷰 시간만 되감는다. 실제 구조물 파괴 판정은 서버가 담당한다.
                bool broken = visible && _running && _elapsed >= _pillarBreakTimes[i];
                _pillars[i].SetActive(!broken);
                _rubble[i].SetActive(broken);
            }
        }

        private void DestroyEffects()
        {
            for (int i = 0; i < _effects.Length; ++i)
            {
                if (_effects[i].Root != null)
                {
                    _effects[i].Root.SetActive(false);
                    Destroy(_effects[i].Root);
                }
            }
            _effects = Array.Empty<EffectInstance>();
        }

        private void FramePattern()
        {
            Pattern pattern = _patterns[_selection];
            Vector3 min = pattern.Motions[0].From;
            Vector3 max = min;
            for (int i = 0; i < pattern.Motions.Length; ++i)
            {
                min = Vector3.Min(min, pattern.Motions[i].From);
                min = Vector3.Min(min, pattern.Motions[i].To);
                max = Vector3.Max(max, pattern.Motions[i].From);
                max = Vector3.Max(max, pattern.Motions[i].To);
            }
            _cameraFocus = _origin + (min + max) * 0.5f + Vector3.up * (_candidateMode ? 2.4f : 1f);
            _cameraDistance = pattern.CameraDistance;
            ResetCamera();
        }

        private void ResetCamera()
        {
            Vector3 direction = _candidateMode && _frontCamera
                ? new Vector3(0f, 0.12f, 1f) : new Vector3(0.6f, 0.7f, 1f);
            _camera.transform.position = _cameraFocus + direction.normalized * _cameraDistance;
            _camera.transform.LookAt(_cameraFocus);
        }
    }
}
#endif
