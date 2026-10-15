// System
using System;
using System.Collections;

// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    public enum EWolbaekFoxfireVariant
    {
        Blue,
        GoldTarget,
        RedHazard
    }

    public enum EWolbaekFoxfireState
    {
        Idle,
        Flight,
        Locked,
        Impact
    }

    public enum EWolbaekFoxfirePreviewMode
    {
        Sequence,
        Flight,
        Locked,
        Impact,
        GoldTarget,
        RedHazard
    }

    /// <summary> 여우불의 외부 시계와 확정 이벤트만 표현하며 재사용 세대로 늦은 이전 호출을 거절한다 </summary>
    public sealed class WolbaekFoxfirePlayer : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MIN_SECONDS      = 0.001f;
        private const float START_SCALE      = 0.18f;
        private const float FLOW_ALPHA_FLOOR = 0.88f;
        private const float FLOW_ALPHA_RANGE = 0.12f;
        private const int   FLIGHT_ROOT      = 0;
        private const int   LOCKED_ROOT      = 1;
        private const int   IMPACT_ROOT      = 2;
        private const int   GOLD_ROOT        = 3;
        private const int   RED_ROOT         = 4;
        private const int   GOLD_IMPACT_ROOT = 5;
        private const int   RED_IMPACT_ROOT  = 6;
        private const int   ROOT_COUNT       = 7;

        private static readonly int G_ColorId = Shader.PropertyToID("_Color");
        private static readonly int G_MapId   = Shader.PropertyToID("_BaseMap_ST");

        [Header("표현 설정, 시작 때 스냅샷")]
        [SerializeField] private WolbaekFoxfireSettingsSO _settings = null;
        [SerializeField] private Transform[] _stateRoots = null;
        [SerializeField] private float[] _baseGroundRadii = null;
        [SerializeField] private EWolbaekFoxfirePreviewMode _previewMode = EWolbaekFoxfirePreviewMode.Sequence;

        private Renderer[][] _renderers;
        private bool[][] _flowLayers;
        private bool[][] _petalLayers;
        private bool[][] _groundLayers;
        private WolbaekFoxfireMotionRig[][] _motionRigs;
        private WolbaekFoxfirePath _flightPath;
        private MaterialPropertyBlock _properties;
        private Tweener _appearTween;
        private Tweener _expansionTween;
        private Tweener _fadeTween;
        private Tweener _flowTween;
        private Tweener _previewMoveTween;
        private Coroutine _previewRoutine;
        private readonly WaitForEndOfFrame _frameWait = new WaitForEndOfFrame();

        private bool _initialized;
        private bool _running;
        private bool _confirmed;
        private bool _samplingPreview;
        private uint _playbackId;
        private double _startTime;
        private double _impactTime;
        private double _hitTime;
        private double _lastNow;
        private Vector3 _anchor;
        private Vector3 _previewAnchor;
        private Quaternion _previewRotation;
        private float _lastPreviewElapsed;
        private float _appear;
        private float _expansion;
        private float _fade;
        private float _flow;
        private float _requestedGroundRadius;
        private float _snapAppear;
        private float _snapFlowCycle;
        private float _snapUvSpeed;
        private float _snapPetalDrift;
        private float _snapPetalRise;
        private float _snapFlightTimeout;
        private float _snapGrace;
        private float _snapExpansion;
        private float _snapHold;
        private float _snapFade;
        private float _snapPreviewFlight;
        private float _snapPreviewLocked;
        private float _snapPreviewDistance;
        private float _snapTailLag;
        private float _snapTeleportDistance;
        private float _snapBend;
        private float _snapFragmentSpread;
        private EWolbaekFoxfireVariant _variant;
        private EWolbaekFoxfireState _state;

        /****************************************
        *              Properties
        ****************************************/

        public WolbaekFoxfireSettingsSO Settings => _settings;
        public uint PlaybackId                  => _playbackId;
        public bool IsRunning                   => _running;
        public bool IsConfirmed                 => _confirmed;
        public EWolbaekFoxfireState State        => _state;
        public EWolbaekFoxfirePreviewMode PreviewMode => _previewMode;
        public event Action<uint> Completed;

        public float PreviewDuration => _previewMode == EWolbaekFoxfirePreviewMode.Sequence
            ? _snapPreviewFlight + _snapPreviewLocked + _snapExpansion + _snapHold + _snapFade
            : _previewMode == EWolbaekFoxfirePreviewMode.Flight ? _snapPreviewFlight
            : _previewMode == EWolbaekFoxfirePreviewMode.Impact ? _snapExpansion + _snapHold + _snapFade : _snapPreviewLocked;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            Initialize();
            Stop();
        }

        protected override void OnServerBuildAwake() { Stop(); }
        private void OnDisable() { Stop(); }

        private void OnDestroy()
        {
            Stop();
            _appearTween?.Kill();
            _expansionTween?.Kill();
            _fadeTween?.Kill();
            _flowTween?.Kill();
            _previewMoveTween?.Kill();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 머리는 로컬 +Z를 향하며 반환한 세대 ID를 같은 재생의 후속 호출에 전달한다 </summary>
        public uint PlayFlight(Vector3 position, Quaternion rotation, double startTime, double now)
        {
            if ((!CanPlay()) || (!HasRoot(FLIGHT_ROOT)) || (!Finite(startTime)) || (!Finite(now))
                || (!Finite(position)) || (!Finite(rotation)))
            {
                return 0;
            }

            Begin(startTime, now);
            transform.SetPositionAndRotation(position, rotation);
            _state = EWolbaekFoxfireState.Flight;
            Tick(_playbackId, now);
            return _playbackId;
        }

        /// <summary> 고정 월드 위치에 완전한 예고를 표시하며 외부 확정을 기다린다 </summary>
        public uint BeginTelegraph(Vector3 anchor, EWolbaekFoxfireVariant variant,
            double startTime, double impactTime, double now)
        {
            if ((!CanPlay()) || (!ValidTimes(startTime, impactTime, now))
                || (!Finite(anchor)) || (!ValidVariant(variant)) || (!HasRoot(TelegraphRoot(variant))))
            {
                return 0;
            }

            Begin(startTime, now);
            LockAt(_playbackId, anchor, variant, startTime, impactTime, now);
            return _playbackId;
        }

        /// <summary> 이동 중인 같은 세대를 고정 위치로 옮기며 오래된 세대의 호출은 무시한다 </summary>
        public bool LockAt(uint playbackId, Vector3 anchor, EWolbaekFoxfireVariant variant,
            double startTime, double impactTime, double now)
        {
            if ((!Current(playbackId)) || (_confirmed) || (!ValidTimes(startTime, impactTime, now))
                || (now < _lastNow) || (!Finite(anchor)) || (!ValidVariant(variant)) || (!HasRoot(TelegraphRoot(variant))))
            {
                return false;
            }

            _anchor     = anchor;
            _variant    = variant;
            _startTime  = startTime;
            _impactTime = impactTime;
            _state      = EWolbaekFoxfireState.Locked;
            _flightPath.Clear();
            Tick(playbackId, now);
            return true;
        }

        public bool SetFlightPose(uint playbackId, Vector3 position, Quaternion rotation)
        {
            if ((!Current(playbackId)) || (_state != EWolbaekFoxfireState.Flight)
                || (!Finite(position)) || (!Finite(rotation)))
            {
                return false;
            }

            transform.SetPositionAndRotation(position, rotation);
            return true;
        }

        /// <summary> 서버가 전달한 지면 반경만 조절하며 코어 높이나 피해 판정은 바꾸지 않는다 </summary>
        public bool SetGroundRadius(uint playbackId, float radius)
        {
            if ((!Current(playbackId)) || (!Finite(radius)) || (radius < MIN_SECONDS))
            {
                return false;
            }

            _requestedGroundRadius = radius;
            return true;
        }

        /// <summary> 예정 시간이 아니라 실제 확정 이벤트를 받은 세대만 타격을 시작한다 </summary>
        public bool ConfirmImpact(uint playbackId, double hitTime, double now)
        {
            if ((!Current(playbackId)) || (_state != EWolbaekFoxfireState.Locked)
                || (_confirmed) || (!Finite(hitTime)) || (!Finite(now))
                || (hitTime > now) || (hitTime < _startTime) || (now < _lastNow)
                || (now > _impactTime + _snapGrace) || (!HasRoot(ImpactRoot(_variant))))
            {
                return false;
            }

            _confirmed = true;
            _hitTime   = hitTime;
            _state     = EWolbaekFoxfireState.Impact;
            Tick(playbackId, now);
            return true;
        }

        /// <summary> 호출자가 받은 실제 확정 시각으로 단독 타격을 재생하며 예정 시각으로 호출하지 않는다 </summary>
        public uint PlayImpact(Vector3 anchor, EWolbaekFoxfireVariant variant, double confirmedHitTime, double now)
        {
            if ((!CanPlay()) || (!Finite(anchor)) || (!ValidVariant(variant))
                || (!Finite(confirmedHitTime)) || (!Finite(now)) || (confirmedHitTime > now)
                || (!HasRoot(ImpactRoot(variant))))
            {
                return 0;
            }

            Begin(confirmedHitTime, now);
            _anchor = anchor;
            _variant = variant;
            _impactTime = confirmedHitTime;
            _hitTime = confirmedHitTime;
            _confirmed = true;
            _state = EWolbaekFoxfireState.Impact;
            Tick(_playbackId, now);
            return _playbackId;
        }

        /// <summary> 같은 시계의 현재 시각을 외부에서 전달한다, 비활성 상태에는 자체 Tick이 없다 </summary>
        public void Tick(uint playbackId, double now)
        {
            if ((!Current(playbackId)) || (!Finite(now)) || (now < _lastNow))
            {
                return;
            }

            _lastNow = now;
            double age = now - _startTime;
            if (age < 0d)
            {
                HideRoots();
                return;
            }

            if ((_state == EWolbaekFoxfireState.Flight) && (age > _snapFlightTimeout))
            {
                Finish();
                return;
            }

            if ((_state == EWolbaekFoxfireState.Locked) && (now > _impactTime + _snapGrace))
            {
                Finish();
                return;
            }

            _appearTween.Goto((float)age, false);
            _flowTween.Goto((float)(age % (_snapFlowCycle * 2f)), false);
            float opacity = _appear;
            float scale   = 1f;
            int root      = FLIGHT_ROOT;
            if (_state != EWolbaekFoxfireState.Flight)
            {
                transform.SetPositionAndRotation(_anchor, Quaternion.identity);
                root = _variant == EWolbaekFoxfireVariant.GoldTarget ? GOLD_ROOT
                    : _variant == EWolbaekFoxfireVariant.RedHazard ? RED_ROOT : LOCKED_ROOT;
            }

            if (_confirmed)
            {
                double hitAge = now - _hitTime;
                double end    = _snapExpansion + _snapHold + _snapFade;
                if (hitAge >= end)
                {
                    Finish();
                    return;
                }

                _expansionTween.Goto((float)hitAge, false);
                _fadeTween.Goto((float)Math.Max(0d, hitAge - _snapExpansion - _snapHold), false);
                root    = _variant == EWolbaekFoxfireVariant.GoldTarget ? GOLD_IMPACT_ROOT
                    : _variant == EWolbaekFoxfireVariant.RedHazard ? RED_IMPACT_ROOT : IMPACT_ROOT;
                opacity = _fade;
                scale   = _expansion;
                age     = hitAge;
            }

            Apply(root, opacity, scale, (float)age);
        }

        public bool Stop(uint playbackId)
        {
            if ((!_running) || (playbackId != _playbackId))
            {
                return false;
            }

            Stop();
            return true;
        }

        /// <summary> 취소·풀 반환·비활성화 전에 표현과 로컬 미리보기를 모두 정리한다 </summary>
        [ContextMenu("Stop")]
        public void Stop()
        {
            if (_previewRoutine != null)
            {
                StopCoroutine(_previewRoutine);
                _previewRoutine = null;
            }

            _running         = false;
            _confirmed       = false;
            _samplingPreview = false;
            _state           = EWolbaekFoxfireState.Idle;
            _appearTween?.Pause();
            _expansionTween?.Pause();
            _fadeTween?.Pause();
            _flowTween?.Pause();
            _previewMoveTween?.Pause();
            _flightPath?.Clear();
            if (_motionRigs != null)
            {
                for (int i = 0; i < _motionRigs.Length; ++i)
                {
                    if (_motionRigs[i] == null)
                    {
                        continue;
                    }
                    for (int j = 0; j < _motionRigs[i].Length; ++j)
                    {
                        _motionRigs[i][j]?.ResetPose();
                    }
                }
            }
            HideRoots();
        }

        /// <summary> 로컬 미리보기만 자동 확정하며 네트워크 재생과 섞지 않는다 </summary>
        [ContextMenu("Preview Local Default")]
        public void Preview()
        {
            if ((!Application.isPlaying) || (!CanPlay()))
            {
                return;
            }

            Stop();
            _previewRoutine = StartCoroutine(PreviewRoutine());
        }

        public void SamplePreview(float elapsedSeconds, EWolbaekFoxfireVariant variant = EWolbaekFoxfireVariant.Blue)
        {
            // Inspector 샘플링이 진행 중인 외부 재생을 덮어쓰면 실제 확정 계약을 잃는다.
            if ((_running) && (!_samplingPreview))
            {
                return;
            }

            if ((!CanPlay()) || (!Finite(elapsedSeconds)) || (elapsedSeconds < 0f)
                || (!ValidVariant(variant)))
            {
                Stop();
                return;
            }

            if (_previewMode != EWolbaekFoxfirePreviewMode.Sequence)
            {
                SampleSinglePreview(elapsedSeconds, variant);
                return;
            }

            if ((!_samplingPreview) || (elapsedSeconds < _lastPreviewElapsed) || (variant != _variant))
            {
                Vector3 anchor = transform.position;
                Quaternion rotation = transform.rotation;
                PlayFlight(anchor, rotation, 0d, 0d);
                _previewAnchor   = anchor;
                _previewRotation = rotation;
                _variant         = variant;
                _samplingPreview = true;
                _previewMoveTween.ChangeStartValue(anchor - rotation * Vector3.forward * _snapPreviewDistance);
                _previewMoveTween.ChangeEndValue(anchor, _snapPreviewFlight, false);
            }

            _lastPreviewElapsed = elapsedSeconds;
            if (elapsedSeconds < _snapPreviewFlight)
            {
                _previewMoveTween.Goto(elapsedSeconds, false);
                Tick(_playbackId, elapsedSeconds);
                return;
            }

            if (_state == EWolbaekFoxfireState.Flight)
            {
                LockAt(_playbackId, _previewAnchor, variant, _snapPreviewFlight,
                    _snapPreviewFlight + _snapPreviewLocked, _snapPreviewFlight);
            }

            if ((!_confirmed) && (elapsedSeconds >= _snapPreviewFlight + _snapPreviewLocked))
            {
                ConfirmImpact(_playbackId, _snapPreviewFlight + _snapPreviewLocked,
                    _snapPreviewFlight + _snapPreviewLocked);
            }

            Tick(_playbackId, elapsedSeconds);
        }

        private void SampleSinglePreview(float elapsedSeconds, EWolbaekFoxfireVariant variant)
        {
            variant = _previewMode == EWolbaekFoxfirePreviewMode.GoldTarget ? EWolbaekFoxfireVariant.GoldTarget
                : _previewMode == EWolbaekFoxfirePreviewMode.RedHazard ? EWolbaekFoxfireVariant.RedHazard
                : _previewMode == EWolbaekFoxfirePreviewMode.Impact ? variant : EWolbaekFoxfireVariant.Blue;
            if ((!_samplingPreview) || (elapsedSeconds < _lastPreviewElapsed) || (variant != _variant))
            {
                Vector3 anchor = transform.position;
                Quaternion rotation = transform.rotation;
                uint id;
                if (_previewMode == EWolbaekFoxfirePreviewMode.Flight)
                {
                    id = PlayFlight(anchor, rotation, 0d, 0d);
                }
                else if (_previewMode == EWolbaekFoxfirePreviewMode.Impact)
                {
                    id = PlayImpact(anchor, variant, 0d, 0d);
                }
                else
                {
                    id = BeginTelegraph(anchor, variant, 0d, Seconds(_settings.PreviewLockedSeconds), 0d);
                }
                if (id == 0)
                {
                    Stop();
                    return;
                }
                _variant = variant;
                _samplingPreview = true;
                _previewMoveTween.ChangeStartValue(anchor - rotation * Vector3.forward * _snapPreviewDistance);
                _previewMoveTween.ChangeEndValue(anchor, _snapPreviewFlight, false);
            }
            _lastPreviewElapsed = elapsedSeconds;
            if (elapsedSeconds >= PreviewDuration)
            {
                Stop();
                return;
            }
            if (_previewMode == EWolbaekFoxfirePreviewMode.Flight)
            {
                _previewMoveTween.Goto(elapsedSeconds, false);
            }
            Tick(_playbackId, elapsedSeconds);
        }

        private IEnumerator PreviewRoutine()
        {
            double started = Time.unscaledTimeAsDouble;
            SamplePreview(0f);
            while (_running)
            {
                SamplePreview((float)(Time.unscaledTimeAsDouble - started));
                yield return _frameWait;
            }

            _previewRoutine = null;
        }

        private void Initialize()
        {
            if ((_initialized) || (_stateRoots == null) || (_stateRoots.Length != ROOT_COUNT)
                || (_baseGroundRadii == null) || (_baseGroundRadii.Length != ROOT_COUNT))
            {
                return;
            }

            _properties = new MaterialPropertyBlock();
            _renderers  = new Renderer[ROOT_COUNT][];
            _flowLayers = new bool[ROOT_COUNT][];
            _petalLayers = new bool[ROOT_COUNT][];
            _groundLayers = new bool[ROOT_COUNT][];
            _motionRigs = new WolbaekFoxfireMotionRig[ROOT_COUNT][];
            _flightPath = new WolbaekFoxfirePath();
            for (int i = 0; i < ROOT_COUNT; ++i)
            {
                if (_stateRoots[i] == null)
                {
                    continue;
                }

                _renderers[i]  = _stateRoots[i].GetComponentsInChildren<Renderer>(true);
                _flowLayers[i] = new bool[_renderers[i].Length];
                _petalLayers[i] = new bool[_renderers[i].Length];
                _groundLayers[i] = new bool[_renderers[i].Length];
                _motionRigs[i] = new WolbaekFoxfireMotionRig[_renderers[i].Length];
                for (int j = 0; j < _renderers[i].Length; ++j)
                {
                    string layerName = _renderers[i][j].name;
                    _flowLayers[i][j] = (layerName.StartsWith("Flame_")) || (layerName.StartsWith("Core_"));
                    _petalLayers[i][j] = layerName.StartsWith("Petals_");
                    _groundLayers[i][j] = layerName.StartsWith("Ground_");
                    _motionRigs[i][j] = _renderers[i][j].GetComponent<WolbaekFoxfireMotionRig>();
                }
            }

            _appearTween = DOTween.To(() => _appear, value => _appear = value, 1f, 1f)
                .SetAutoKill(false).SetEase(Ease.OutSine).Pause();
            _expansionTween = DOTween.To(() => _expansion, value => _expansion = value, 1f, 1f)
                .SetAutoKill(false).SetEase(Ease.OutCubic).Pause();
            _fadeTween = DOTween.To(() => _fade, value => _fade = value, 0f, 1f)
                .SetAutoKill(false).SetEase(Ease.InSine).Pause();
            _flowTween = DOTween.To(() => _flow, value => _flow = value, 1f, 1f)
                .SetAutoKill(false).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).Pause();
            _previewMoveTween = transform.DOMove(transform.position, 1f)
                .SetAutoKill(false).SetEase(Ease.InOutSine).Pause();
            _initialized = true;
        }

        private void Begin(double startTime, double now)
        {
            Stop();
            ++_playbackId;
            if (_playbackId == 0)
            {
                ++_playbackId;
            }

            Snapshot();
            _startTime = startTime;
            _lastNow   = now;
            _running   = true;
            _appear    = 0f;
            _expansion = START_SCALE;
            _fade      = 1f;
            _flow      = 0f;
            _requestedGroundRadius = 0f;
            _appearTween.ChangeStartValue(0f).ChangeEndValue(1f, _snapAppear, false);
            _expansionTween.ChangeStartValue(START_SCALE).ChangeEndValue(1f, _snapExpansion, false);
            _fadeTween.ChangeStartValue(1f).ChangeEndValue(0f, _snapFade, false);
            _flowTween.ChangeStartValue(0f).ChangeEndValue(1f, _snapFlowCycle, false);
        }

        private void Snapshot()
        {
            _snapAppear          = Seconds(_settings.AppearSeconds);
            _snapFlowCycle       = Seconds(_settings.FlowCycleSeconds);
            _snapUvSpeed         = Finite(_settings.UvFlowSpeed) ? _settings.UvFlowSpeed : 0f;
            _snapPetalDrift      = Mathf.Max(0f, Finite(_settings.PetalDriftDistance) ? _settings.PetalDriftDistance : 0f);
            _snapPetalRise       = Mathf.Max(0f, Finite(_settings.ImpactPetalRiseSpeed) ? _settings.ImpactPetalRiseSpeed : 0f);
            _snapFlightTimeout   = Seconds(_settings.FlightTimeoutSeconds);
            _snapGrace           = Mathf.Max(0f, Seconds(_settings.ConfirmationGraceSeconds));
            _snapExpansion       = Seconds(_settings.ExpansionSeconds);
            _snapHold            = Mathf.Max(0f, Seconds(_settings.HoldSeconds));
            _snapFade            = Seconds(_settings.FadeSeconds);
            _snapPreviewFlight   = Seconds(_settings.PreviewFlightSeconds);
            _snapPreviewLocked   = Seconds(_settings.PreviewLockedSeconds);
            _snapPreviewDistance = Finite(_settings.PreviewFlightDistance) ? _settings.PreviewFlightDistance : 0f;
            _snapTailLag = Seconds(_settings.TailLagSeconds);
            _snapTeleportDistance = Seconds(_settings.TeleportResetDistance);
            _snapBend = Mathf.Max(0f, Finite(_settings.FlowBendDistance) ? _settings.FlowBendDistance : 0f);
            _snapFragmentSpread = Mathf.Max(0f, Finite(_settings.ImpactFragmentSpread) ? _settings.ImpactFragmentSpread : 0f);
        }

        private void Apply(int root, float opacity, float expansion, float age)
        {
            if (!HasRoot(root))
            {
                Stop();
                return;
            }
            if (root == FLIGHT_ROOT)
            {
                _flightPath.Record(transform.position, transform.rotation, _lastNow, _snapTailLag, _snapTeleportDistance);
            }
            for (int i = 0; i < ROOT_COUNT; ++i)
            {
                if (_stateRoots[i] == null)
                {
                    continue;
                }
                bool active = i == root;
                if (_stateRoots[i].gameObject.activeSelf != active)
                {
                    _stateRoots[i].gameObject.SetActive(active);
                }
            }

            for (int i = 0; i < _renderers[root].Length; ++i)
            {
                Renderer renderer = _renderers[root][i];
                bool flow         = _flowLayers[root][i];
                bool petal        = _petalLayers[root][i];
                bool ground       = _groundLayers[root][i];
                WolbaekFoxfireMotionRig rig = _motionRigs[root][i];
                _properties.Clear();
                float layerAlpha = flow ? opacity * (FLOW_ALPHA_FLOOR + FLOW_ALPHA_RANGE * _flow) : opacity;
                _properties.SetColor(G_ColorId, new Color(1f, 1f, 1f, layerAlpha * (rig != null ? rig.OpacityMultiplier : 1f)));
                _properties.SetVector(G_MapId, new Vector4(1f, 1f, 0f, flow ? age * _snapUvSpeed : 0f));
                renderer.SetPropertyBlock(_properties);
                if ((ground) && (_requestedGroundRadius > 0f) && (_baseGroundRadii[root] > 0f))
                {
                    float ratio = _requestedGroundRadius / _baseGroundRadii[root];
                    renderer.transform.localScale = new Vector3(ratio, 1f, ratio);
                }
                else
                {
                    renderer.transform.localScale = Vector3.one;
                }
                renderer.transform.localPosition = Vector3.zero;
                SkinnedMeshRenderer skin = renderer as SkinnedMeshRenderer;
                if ((skin != null) && (skin.sharedMesh.blendShapeCount != 0))
                {
                    skin.SetBlendShapeWeight(0, 0f);
                }
                rig?.Sample(transform, _flightPath, _lastNow, age, _snapFlowCycle, _snapBend,
                    _snapTailLag, _snapPetalDrift, _snapPetalRise, _snapExpansion,
                    _snapExpansion + _snapHold + _snapFade, _snapFade, _snapFragmentSpread);
            }
        }

        private void HideRoots()
        {
            if (_stateRoots == null)
            {
                return;
            }

            for (int i = 0; i < _stateRoots.Length; ++i)
            {
                if (_stateRoots[i] != null)
                {
                    _stateRoots[i].gameObject.SetActive(false);
                }
            }
        }

        private void Finish()
        {
            uint finished = _playbackId;
            Stop();
            Completed?.Invoke(finished);
        }

        private bool CanPlay()
        {
            Initialize();
            return (!IsServerBuild) && (_initialized) && (_settings != null) && (gameObject.activeInHierarchy);
        }

        private bool HasRoot(int index)
        {
            return (_stateRoots != null) && (index >= 0) && (index < _stateRoots.Length)
                && (_stateRoots[index] != null) && (_renderers != null) && (_renderers[index] != null)
                && (_renderers[index].Length > 0);
        }

        private static int TelegraphRoot(EWolbaekFoxfireVariant variant)
        {
            return variant == EWolbaekFoxfireVariant.GoldTarget ? GOLD_ROOT
                : variant == EWolbaekFoxfireVariant.RedHazard ? RED_ROOT : LOCKED_ROOT;
        }

        private static int ImpactRoot(EWolbaekFoxfireVariant variant)
        {
            return variant == EWolbaekFoxfireVariant.GoldTarget ? GOLD_IMPACT_ROOT
                : variant == EWolbaekFoxfireVariant.RedHazard ? RED_IMPACT_ROOT : IMPACT_ROOT;
        }

        private bool Current(uint playbackId)
        {
            return (!IsServerBuild) && (_running) && (playbackId != 0) && (playbackId == _playbackId)
                && (gameObject.activeInHierarchy);
        }

        private static bool ValidTimes(double start, double impact, double now)
        {
            return (Finite(start)) && (Finite(impact)) && (Finite(now)) && (impact >= start);
        }

        private static bool ValidVariant(EWolbaekFoxfireVariant variant)
        {
            return (variant >= EWolbaekFoxfireVariant.Blue) && (variant <= EWolbaekFoxfireVariant.RedHazard);
        }

        private static bool Finite(double value) { return (!double.IsNaN(value)) && (!double.IsInfinity(value)); }
        private static bool Finite(Vector3 value) { return (Finite(value.x)) && (Finite(value.y)) && (Finite(value.z)); }
        private static bool Finite(Quaternion value) { return (Finite(value.x)) && (Finite(value.y)) && (Finite(value.z)) && (Finite(value.w)); }
        private static float Seconds(float value) { return Finite(value) ? Mathf.Max(MIN_SECONDS, value) : MIN_SECONDS; }
    }
}
