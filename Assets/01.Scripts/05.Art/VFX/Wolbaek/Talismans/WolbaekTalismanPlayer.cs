// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;
using DG.Tweening.Core;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 확정 스냅샷과 외부 공통 시계로만 부적을 표시한다. 진행도를 시간으로 계산하지 않는다 </summary>
    public sealed class WolbaekTalismanPlayer : ClientView
    {
        /****************************************
        *                Types
        ****************************************/
        public enum EPhase
        {
            Holding,
            Reset,
            Success,
            Expired
        }

        /// <summary> 전송 계약과 독립된 표현 입력. PhaseStartedAt은 리셋마다 갱신되는 유지 구간의 시작 시각이다 </summary>
        public readonly struct Snapshot
        {
            public ulong CastId { get; }
            public ulong Sequence { get; }
            public EWolbaekTalismanSign Sign { get; }
            public Color Color { get; }
            public Vector3 WorldCenter { get; }
            public float Radius { get; }
            public double AppearedAt { get; }
            public double SnapshotAt { get; }
            public EPhase Phase { get; }
            public double PhaseStartedAt { get; }
            public float HoldProgress { get; }

            public Snapshot(ulong castId, ulong sequence, EWolbaekTalismanSign sign, Color color,
                Vector3 worldCenter, float radius, double appearedAt, double snapshotAt,
                EPhase phase, double phaseStartedAt, float holdProgress)
            {
                CastId         = castId;
                Sequence       = sequence;
                Sign           = sign;
                Color          = color;
                WorldCenter    = worldCenter;
                Radius         = radius;
                AppearedAt     = appearedAt;
                SnapshotAt     = snapshotAt;
                Phase          = phase;
                PhaseStartedAt = phaseStartedAt;
                HoldProgress   = holdProgress;
            }
        }

        /****************************************
        *                Fields
        ****************************************/
        private const float MIN_SIZE              = 0.001f;
        private const float ENVELOPE_SECONDS      = 1f;
        private const float TAU                   = Mathf.PI * 2f;
        private const float HALF                  = 0.5f;
        private const float FLOW_SECONDARY        = 0.61f;
        private const float FLOW_LATERAL          = 0.035f;
        private const float FLOW_VERTICAL         = 0.05f;
        private const float PETAL_LIFT            = 0.08f;
        private const float PETAL_SPIN_DEGREES    = 180f;
        private const float MOTE_DRIFT            = 0.04f;
        private const float MIST_DRIFT            = 0.025f;
        private const float PAPER_SWAY_DEGREES    = 4f;
        private const float PAPER_RESET_DIP       = 0.16f;
        private const float GLYPH_BOB              = 0.025f;
        private const float CORE_BREATH            = 0.035f;
        private const float GROUND_ALPHA           = 0.45f;
        private const float RESET_WHITE_SHARE      = 0.65f;
        private const float SUCCESS_WHITE_SHARE    = 0.35f;
        private const float PAPER_HIGHLIGHT_SHARE  = 0.12f;
        private const float RESET_RUNE_ALPHA       = 0.35f;
        private const float RESET_ENERGY_ALPHA     = 0.28f;
        private const float PETAL_SUCCESS_DRIFT    = 1.4f;
        private const float SUCCESS_MIST_RISE      = 0.65f;
        private const float SUCCESS_WISP_RISE      = 0.8f;
        private const float SUCCESS_WISP_ANCHOR_Y  = 0.2f;
        private const float AIR_MIST_MIN_LIGHT     = 0.65f;
        private const float AIR_MIST_LIGHT_SWING   = 0.35f;
        private const float MIN_LIGHT_RANGE        = 0.01f;
        private const float LIGHT_SUCCESS_BOOST    = 0.35f;
        private const int SIGN_COUNT              = 4;
        private const int RIBBON_LAYER            = 0;
        private const int PETAL_LAYER             = 1;
        private const int MIST_LAYER              = 2;
        private const int GROUND_LAYER            = 3;
        private const int PAPER_LAYER             = 4;
        private const int PAPER_RUNES_LAYER       = 5;
        private const int MOTE_LAYER              = 6;
        private const int FOOT_CORE_LAYER         = 7;
        private const int AIR_MIST_LAYER          = 8;
        private const int SUCCESS_MIST_LAYER      = 9;
        private const int RIBBON_MOTION           = 0;
        private const int PETAL_MOTION            = 1;
        private const int MOTE_MOTION             = 2;
        private const int MIST_MOTION             = 3;
        private const int PAPER_MOTION            = 4;
        private const int GROUND_PETAL_MOTION     = 5;
        private const int FOOT_CORE_MOTION        = 6;
        private const int SUCCESS_WISP_MOTION     = 7;
        private static readonly int _colorId          = Shader.PropertyToID("_Color");
        private static readonly int _progressId       = Shader.PropertyToID("_Progress");
        private static readonly int _baseMapStId      = Shader.PropertyToID("_BaseMap_ST");

        // 미니맵처럼 씬을 탐색하지 않고 지금 표시 중인 부적 위치를 읽는 쪽이 쓰는 목록. Begin에서 넣고 Stop에서 뺀다
        private static readonly List<WolbaekTalismanPlayer> _runningPlayers = new List<WolbaekTalismanPlayer>(4);

        [SerializeField] private WolbaekTalismanSettingsSO _settings = null;
        [SerializeField] private Renderer[] _layers                 = null;
        [SerializeField] private int[] _layerKinds                   = null;
        [SerializeField] private Renderer[] _glyphs                 = null;
        [SerializeField] private Transform[] _bones                  = null;
        [SerializeField] private Vector3[] _rest                    = null;
        // x=종류, y=진폭 배율 또는 성공 잔상의 길이 비율, z=위상, w=회전 방향
        [SerializeField] private Vector4[] _motion    = null;
        [SerializeField] private Transform _radiusRoot = null;
        [SerializeField] private Transform _glyphRoot  = null;
        [SerializeField] private Renderer _boundary    = null;
        [SerializeField] private Renderer _progress    = null;
        [SerializeField] private Renderer[] _pillar    = null;
        [SerializeField] private Light _footLight       = null;

        private MaterialPropertyBlock _properties;
        private Tweener _appearTween;
        private Tweener _resetTween;
        private Tweener _successTween;
        private Tweener _fadeTween;
        private Quaternion[] _restRotations;
        private Vector3[] _restScales;
        private Vector3 _rootPosition;
        private Quaternion _rootRotation;
        private Vector3 _rootScale;
        private Vector3 _radiusScale;
        private Vector3 _glyphPosition;
        private Quaternion _glyphRotation;
        private Vector3 _glyphScale;
        private Quaternion _glyphFacing;
        private Snapshot _snapshot;
        private uint _playbackId;
        private double _lastNow;
        private float _appear;
        private float _reset;
        private float _success;
        private float _fade;
        private float _appearSeconds;
        private float _resetSeconds;
        private float _successSeconds;
        private float _fadeSeconds;
        private float _flowCycle;
        private float _petalCycle;
        private float _petalDrift;
        private float _mistOpacity;
        private float _paperOpacity;
        private float _runeStrength;
        private Color _petalTint;
        private float _assignedPetalTintShare;
        private float _groundLightStrength;
        private float _airMistOpacity;
        private float _successMistOpacity;
        private float _successRise;
        private float _paperSuccessRise;
        private float _footLightIntensity;
        private float _resetFromProgress;
        private float _displayedProgress;
        private bool _posesCached;
        private bool _running;
        private bool _hasGlyphFacing;

        /****************************************
        *              Properties
        ****************************************/
        public WolbaekTalismanSettingsSO Settings => _settings;
        public bool IsRunning => _running;

        /// <summary> 지금 표시 중인 부적, 표시가 끝나면 빠진다 </summary>
        public static IReadOnlyList<WolbaekTalismanPlayer> RunningPlayers => _runningPlayers;
        public uint PlaybackId => _playbackId;
        public ulong CastId => _running ? _snapshot.CastId : 0;
        public Snapshot CurrentSnapshot => _snapshot;
        public float DisplayedProgress => _displayedProgress;
        /// <summary> 공급된 종료 상태의 시각에서 표현이 실제 끝났을 때만 발생한다 </summary>
        public event Action<uint> Completed;

        /****************************************
        *              Unity Event
        ****************************************/
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _runningPlayers.Clear();
        }

        protected override void OnViewAwake()
        {
            _properties = new MaterialPropertyBlock();
            _appearTween = CreateEnvelope(() => _appear, value => _appear = value, Ease.OutCubic);
            _resetTween = CreateEnvelope(() => _reset, value => _reset = value, Ease.OutCubic);
            _successTween = CreateEnvelope(() => _success, value => _success = value, Ease.OutCubic);
            _fadeTween = CreateEnvelope(() => _fade, value => _fade = value, Ease.InCubic);
            Stop();
        }

        protected override void OnServerBuildAwake()
        {
            Stop();
        }

        private void OnDisable()
        {
            Stop();
        }

        private void OnDestroy()
        {
            _appearTween?.Kill();
            _resetTween?.Kill();
            _successTween?.Kill();
            _fadeTween?.Kill();
        }

        /****************************************
        *                Methods
        ****************************************/
        /// <summary> 등장 중이거나 재접속한 부적을 확정 스냅샷의 시각에서 시작한다 </summary>
        public uint Begin(Snapshot snapshot, double now)
        {
            if ((!Ready(now)) || (!ValidSnapshot(snapshot, now)) || ((_running) && (now < _lastNow)))
            {
                return 0;
            }

            CachePoses();
            Stop();
            ++_playbackId;
            if (_playbackId == 0)
            {
                ++_playbackId;
            }
            _snapshot = snapshot;
            _lastNow = now;
            _running = true;
            if (!_runningPlayers.Contains(this))
            {
                _runningPlayers.Add(this);
            }

            CacheSettings();
            ConfigureFootLight();
            _radiusRoot.localScale = new Vector3(_radiusScale.x * snapshot.Radius,
                _radiusScale.y, _radiusScale.z * snapshot.Radius);
            // 완료 콜백이 풀에서 새 재생을 시작해도 이번 호출의 세대만 반환한다
            uint playbackId = _playbackId;
            Tick(playbackId, now);
            return playbackId;
        }

        /// <summary> 같은 시전의 새로운 스냅샷을 적용한다. 거절된 입력은 표현과 시계를 변경하지 않는다 </summary>
        public bool Reconcile(uint playbackId, Snapshot snapshot, double now)
        {
            if ((!Current(playbackId, now)) || (!ValidSnapshot(snapshot, now))
                || (!SameCast(snapshot)) || (!ValidTransition(snapshot)))
            {
                return false;
            }

            if ((snapshot.Phase == EPhase.Reset) && ((_snapshot.Phase != EPhase.Reset)
                || (snapshot.PhaseStartedAt != _snapshot.PhaseStartedAt)))
            {
                _resetFromProgress = _snapshot.HoldProgress;
            }
            _snapshot = snapshot;
            Tick(playbackId, now);
            return true;
        }

        /// <summary> 단조 증가하는 공통 시계를 공급한다. 대기 중에는 로컬 시간이 아무리 흘러도 성공하지 않는다 </summary>
        public void Tick(uint playbackId, double now)
        {
            if (!Current(playbackId, now))
            {
                return;
            }

            _lastNow = now;
            double age = now - _snapshot.AppearedAt;
            double phaseAge = now - _snapshot.PhaseStartedAt;
            bool success = _snapshot.Phase == EPhase.Success;
            bool expired = _snapshot.Phase == EPhase.Expired;
            double fadeStart = success ? _successSeconds : 0d;
            if (((success) || (expired)) && (phaseAge >= fadeStart + _fadeSeconds))
            {
                Complete(playbackId);
                return;
            }

            _appearTween.Goto(Normalized(age, _appearSeconds), false);
            _resetTween.Goto(_snapshot.Phase == EPhase.Reset ? Normalized(phaseAge, _resetSeconds) : ENVELOPE_SECONDS, false);
            _successTween.Goto(success ? Normalized(phaseAge, _successSeconds) : 0f, false);
            _fadeTween.Goto((success) || (expired) ? Normalized(phaseAge - fadeStart, _fadeSeconds) : 0f, false);
            float resetPulse = _snapshot.Phase == EPhase.Reset ? 1f - _reset : 0f;
            float resetStrength = _snapshot.Phase == EPhase.Reset ? _reset : 0f;
            _displayedProgress = _snapshot.Phase == EPhase.Reset
                ? _resetFromProgress * (1f - _reset) : _snapshot.HoldProgress;
            float opacity = _appear * (1f - _fade);
            float phase = Phase(age, _flowCycle);
            Color color = Color.Lerp(_snapshot.Color, Color.white,
                Mathf.Max(resetPulse * RESET_WHITE_SHARE, _success * SUCCESS_WHITE_SHARE));
            color.a = _snapshot.Color.a;
            transform.SetPositionAndRotation(_snapshot.WorldCenter, Quaternion.identity);
            AnimateLayers(color, opacity, resetStrength, phase);
            AnimateBones(age, phase, resetStrength);
            AnimateGlyph(phase, resetStrength);
            AnimateFootLight(opacity, resetStrength);
        }

        /// <summary> 문양만 외부 관찰 위치를 향하게 한다. 반경과 월드 중심은 변경하지 않는다 </summary>
        public bool FaceGlyph(uint playbackId, Vector3 viewerPosition)
        {
            if ((!_running) || (playbackId != _playbackId) || (!Valid(viewerPosition)) || (_glyphRoot == null))
            {
                return false;
            }

            Vector3 direction = viewerPosition - _glyphRoot.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < MIN_SIZE * MIN_SIZE)
            {
                return false;
            }
            // 원본 문양의 앞면은 -Z이므로 +Z를 관찰자에게 돌리면 초승달이 좌우 반전된다
            _glyphFacing = Quaternion.LookRotation(-direction, Vector3.up);
            _hasGlyphFacing = true;
            _glyphRoot.rotation = _glyphFacing;
            return true;
        }

        /// <summary> 현재 재생 세대가 맞을 때만 취소한다. 취소에는 완료 이벤트가 없다 </summary>
        public bool Stop(uint playbackId)
        {
            if ((!_running) || (playbackId != _playbackId))
            {
                return false;
            }
            Stop();
            return true;
        }

        /// <summary> 비활성화와 풀 반환에 사용한다. 속성 블록과 모든 소유 포즈를 복원한다 </summary>
        [ContextMenu("Stop")]
        public void Stop()
        {
            _running = false;
            _runningPlayers.Remove(this);
            _hasGlyphFacing = false;
            _snapshot = default;
            _lastNow = 0d;
            _resetFromProgress = 0f;
            _displayedProgress = 0f;
            Hide(_layers);
            Hide(_glyphs);
            Hide(_pillar);
            Hide(_boundary);
            Hide(_progress);
            if (_footLight != null)
            {
                _footLight.enabled = false;
                _footLight.intensity = 0f;
            }
            _appearTween?.Goto(0f, false);
            _resetTween?.Goto(0f, false);
            _successTween?.Goto(0f, false);
            _fadeTween?.Goto(0f, false);
            RestorePoses();
        }

        private Tweener CreateEnvelope(DOGetter<float> getter, DOSetter<float> setter, Ease ease)
        {
            // 공유 시계의 임의 시점 복구를 위해 자동 재생 대신 캐싱한 DOTween을 Goto로 샘플링한다
            return DOTween.To(getter, setter, ENVELOPE_SECONDS, ENVELOPE_SECONDS)
                .SetEase(ease).SetUpdate(UpdateType.Manual).SetAutoKill(false)
                .SetLink(gameObject, LinkBehaviour.KillOnDestroy).Pause();
        }

        private void CacheSettings()
        {
            _appearSeconds  = _settings.AppearSeconds;
            _resetSeconds   = _settings.ResetSeconds;
            _successSeconds = _settings.SuccessSeconds;
            _fadeSeconds    = _settings.FadeSeconds;
            _flowCycle      = _settings.FlowCycle;
            _petalCycle     = _settings.PetalCycle;
            _petalDrift     = _settings.PetalDrift;
            _mistOpacity    = _settings.MistOpacity;
            _paperOpacity   = _settings.PaperOpacity;
            _runeStrength   = _settings.RuneStrength;
            _petalTint      = _settings.PetalTint;
            _assignedPetalTintShare = _settings.AssignedPetalTintShare;
            _groundLightStrength   = _settings.GroundLightStrength;
            _airMistOpacity        = _settings.AirMistOpacity;
            _successMistOpacity    = _settings.SuccessMistOpacity;
            _successRise           = _settings.SuccessRise;
            _paperSuccessRise      = _settings.PaperSuccessRise;
        }

        private void CachePoses()
        {
            if (_posesCached)
            {
                return;
            }

            _rootPosition = transform.localPosition;
            _rootRotation = transform.localRotation;
            _rootScale = transform.localScale;
            _radiusScale = _radiusRoot.localScale;
            _glyphPosition = _glyphRoot.localPosition;
            _glyphRotation = _glyphRoot.localRotation;
            _glyphScale = _glyphRoot.localScale;
            _restRotations = new Quaternion[_bones.Length];
            _restScales = new Vector3[_bones.Length];
            for (int i = 0; i < _bones.Length; ++i)
            {
                _restRotations[i] = _bones[i].localRotation;
                _restScales[i] = _bones[i].localScale;
            }
            _posesCached = true;
        }

        private void ConfigureFootLight()
        {
            Color color = _snapshot.Color;
            float peak = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            _footLight.color = peak > 0f
                ? new Color(color.r / peak, color.g / peak, color.b / peak, 1f) : Color.black;
            _footLight.range = _settings.GroundLightRange;
            _footLightIntensity = (float)Math.Min(float.MaxValue, (double)_settings.GroundLightIntensity * peak);
        }

        private void RestorePoses()
        {
            if (!_posesCached)
            {
                return;
            }

            transform.SetLocalPositionAndRotation(_rootPosition, _rootRotation);
            transform.localScale = _rootScale;
            if (_radiusRoot != null)
            {
                _radiusRoot.localScale = _radiusScale;
            }
            for (int i = 0; i < _bones.Length; ++i)
            {
                if (_bones[i] == null)
                {
                    continue;
                }
                _bones[i].SetLocalPositionAndRotation(_rest[i], _restRotations[i]);
                _bones[i].localScale = _restScales[i];
            }
            if (_glyphRoot != null)
            {
                _glyphRoot.SetLocalPositionAndRotation(_glyphPosition, _glyphRotation);
                _glyphRoot.localScale = _glyphScale;
            }
        }

        private void AnimateLayers(Color color, float opacity, float resetStrength, float phase)
        {
            float resetAlpha = Mathf.Lerp(1f, RESET_ENERGY_ALPHA, resetStrength);
            for (int i = 0; i < _layers.Length; ++i)
            {
                int kind = _layerKinds[i];
                float alpha = opacity * resetAlpha;
                Color tint = color;
                switch (kind)
                {
                    case MIST_LAYER:
                        alpha *= _mistOpacity;
                        break;
                    case GROUND_LAYER:
                        tint = Color.Lerp(_petalTint, color, _assignedPetalTintShare) * _groundLightStrength;
                        alpha *= GROUND_ALPHA * _petalTint.a;
                        break;
                    case PAPER_LAYER:
                        tint = Color.Lerp(_snapshot.Color, Color.white, PAPER_HIGHLIGHT_SHARE);
                        alpha = opacity * _paperOpacity;
                        break;
                    case PAPER_RUNES_LAYER:
                        tint *= _runeStrength;
                        alpha *= 1f - resetStrength * RESET_RUNE_ALPHA;
                        break;
                    case AIR_MIST_LAYER:
                        alpha *= _airMistOpacity * (AIR_MIST_MIN_LIGHT
                            + (HALF + HALF * Mathf.Sin(phase)) * AIR_MIST_LIGHT_SWING);
                        break;
                    case SUCCESS_MIST_LAYER:
                        alpha *= _snapshot.Phase == EPhase.Success ? _success * _successMistOpacity : 0f;
                        break;
                    case PETAL_LAYER:
                        tint = Color.Lerp(_petalTint, color, _assignedPetalTintShare);
                        alpha *= _petalTint.a;
                        break;
                    case FOOT_CORE_LAYER:
                        tint *= _groundLightStrength;
                        break;
                    case RIBBON_LAYER:
                    case MOTE_LAYER:
                        break;
                }
                tint.a = alpha * _snapshot.Color.a;
                Paint(_layers[i], tint);
            }
            Color ground = color;
            ground.a = opacity * _snapshot.Color.a * GROUND_ALPHA;
            Paint(_boundary, ground);
            ground *= _groundLightStrength;
            ground.a = opacity * _snapshot.Color.a * resetAlpha;
            Paint(_progress, ground, true);
            for (int i = 0; i < _pillar.Length; ++i)
            {
                // 불투명 석재는 원본 재질을 유지하고 부적 전체 종료 때 함께 치운다
                if (_pillar[i] != null)
                {
                    _pillar[i].enabled = true;
                }
            }
            for (int i = 0; i < _glyphs.Length; ++i)
            {
                Color glyph = color * _runeStrength;
                glyph.a = i == (int)_snapshot.Sign ? opacity * _snapshot.Color.a * resetAlpha : 0f;
                Paint(_glyphs[i], glyph);
            }
        }

        private void AnimateBones(double age, float phase, float resetPulse)
        {
            // 유기적인 메시 움직임은 진행도가 아니라 절대 시계에서 샘플링해야 재접속 포즈가 같아진다
            float petalPhase = Phase(age, _petalCycle);
            for (int i = 0; i < _bones.Length; ++i)
            {
                Transform bone = _bones[i];
                if (bone == null)
                {
                    continue;
                }
                Vector4 motion = _motion[i];
                float wave = phase + motion.z;
                float amplitude = motion.y;
                Vector3 offset = Vector3.zero;
                Quaternion turn = Quaternion.identity;
                float scale = 1f;
                switch ((int)motion.x)
                {
                    case RIBBON_MOTION:
                        offset = new Vector3(Mathf.Sin(wave) * FLOW_LATERAL,
                            Mathf.Sin(wave * FLOW_SECONDARY) * FLOW_VERTICAL,
                            Mathf.Cos(wave) * FLOW_LATERAL) * amplitude;
                        offset.y += _success * SUCCESS_WISP_RISE;
                        break;
                    case PETAL_MOTION:
                    case GROUND_PETAL_MOTION:
                        float petal = petalPhase + motion.z;
                        float drift = _petalDrift * amplitude * (1f + _success * PETAL_SUCCESS_DRIFT);
                        offset = new Vector3(Mathf.Sin(petal) * drift,
                            Mathf.Sin(petal * FLOW_SECONDARY) * PETAL_LIFT * amplitude
                                + _success * _successRise,
                            Mathf.Cos(petal) * drift);
                        turn = Quaternion.Euler(Mathf.Sin(wave) * PAPER_SWAY_DEGREES,
                            petalPhase * PETAL_SPIN_DEGREES * motion.w,
                            Mathf.Cos(wave) * PAPER_SWAY_DEGREES);
                        break;
                    case MOTE_MOTION:
                        offset = new Vector3(Mathf.Sin(wave), Mathf.Sin(wave * FLOW_SECONDARY),
                            Mathf.Cos(wave)) * (MOTE_DRIFT * amplitude);
                        offset.y += _success * _successRise;
                        break;
                    case MIST_MOTION:
                        offset = new Vector3(Mathf.Sin(wave), 0f, Mathf.Cos(wave * FLOW_SECONDARY))
                            * (MIST_DRIFT * amplitude);
                        offset.y += _success * SUCCESS_MIST_RISE;
                        break;
                    case PAPER_MOTION:
                        offset.y = Mathf.Sin(wave) * FLOW_VERTICAL * amplitude
                            - resetPulse * PAPER_RESET_DIP + _success * _paperSuccessRise;
                        turn = Quaternion.Euler(Mathf.Sin(wave * FLOW_SECONDARY) * PAPER_SWAY_DEGREES,
                            Mathf.Sin(wave) * PAPER_SWAY_DEGREES, Mathf.Cos(wave) * PAPER_SWAY_DEGREES);
                        break;
                    case FOOT_CORE_MOTION:
                        scale += Mathf.Sin(wave) * CORE_BREATH * amplitude;
                        break;
                    case SUCCESS_WISP_MOTION:
                        // 원본 잔상의 시작 높이를 유지하고 각 절점만 펼쳐 루트 축척과 풀 복원 기준을 보존한다
                        offset = new Vector3(Mathf.Sin(wave) * FLOW_LATERAL * amplitude * _success,
                            (SUCCESS_WISP_ANCHOR_Y - _rest[i].y) * (1f - _success),
                            Mathf.Cos(wave * FLOW_SECONDARY) * FLOW_LATERAL * amplitude * _success);
                        turn = Quaternion.Euler(Mathf.Sin(wave) * PAPER_SWAY_DEGREES * amplitude * _success,
                            0f, Mathf.Cos(wave) * PAPER_SWAY_DEGREES * amplitude * _success);
                        break;
                }
                bone.SetLocalPositionAndRotation(_rest[i] + offset, _restRotations[i] * turn);
                bone.localScale = _restScales[i] * scale;
            }
        }

        private void AnimateGlyph(float phase, float resetPulse)
        {
            if (_glyphRoot == null)
            {
                return;
            }
            _glyphRoot.localPosition = _glyphPosition + Vector3.up * (Mathf.Sin(phase) * GLYPH_BOB
                - resetPulse * PAPER_RESET_DIP + _success * _paperSuccessRise);
            if (_hasGlyphFacing)
            {
                _glyphRoot.rotation = _glyphFacing;
            }
        }

        private void AnimateFootLight(float opacity, float resetStrength)
        {
            if (_footLight == null)
            {
                return;
            }
            float resetAlpha = Mathf.Lerp(1f, RESET_ENERGY_ALPHA, resetStrength);
            double intensity = (double)_footLightIntensity * opacity * _snapshot.Color.a
                * resetAlpha * (1f + _success * LIGHT_SUCCESS_BOOST);
            _footLight.intensity = (float)Math.Min(float.MaxValue, intensity);
            _footLight.enabled = _footLight.intensity > MIN_SIZE;
        }

        private void Paint(Renderer renderer, Color color, bool progress = false)
        {
            if (renderer == null)
            {
                return;
            }
            renderer.enabled = (color.a > MIN_SIZE) && ((!progress) || (_displayedProgress > MIN_SIZE));
            _properties.Clear();
            _properties.SetColor(_colorId, color);
            if (progress)
            {
                _properties.SetFloat(_progressId, _displayedProgress);
                // 클램프된 반쪽 마스크의 이동으로 기존 파티클 셰이더에서도 링 길이를 표시한다
                _properties.SetVector(_baseMapStId, new Vector4(HALF, 1f,
                    HALF - HALF * _displayedProgress, 0f));
            }
            renderer.SetPropertyBlock(_properties);
        }

        private static void Hide(Renderer[] renderers)
        {
            if (renderers == null)
            {
                return;
            }
            for (int i = 0; i < renderers.Length; ++i)
            {
                Hide(renderers[i]);
            }
        }

        private static void Hide(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }
            renderer.enabled = false;
            renderer.SetPropertyBlock(null);
        }

        private void Complete(uint playbackId)
        {
            Stop();
            Completed?.Invoke(playbackId);
        }

        private bool Current(uint playbackId, double now)
        {
            return (!IsServerBuild) && (_running) && (playbackId != 0) && (playbackId == _playbackId)
                && (isActiveAndEnabled) && (Finite(now)) && (now >= _lastNow);
        }

        private bool Ready(double now)
        {
            return (!IsServerBuild) && (_properties != null) && (_settings != null)
                && (isActiveAndEnabled) && (Finite(now)) && (ValidSettings()) && (ValidRig());
        }

        private bool ValidSettings()
        {
            return (Positive(_settings.AppearSeconds)) && (Positive(_settings.ResetSeconds))
                && (Positive(_settings.SuccessSeconds)) && (Positive(_settings.FadeSeconds))
                && (Positive(_settings.FlowCycle)) && (Positive(_settings.PetalCycle))
                && (Finite(_settings.PetalDrift)) && (_settings.PetalDrift >= 0f)
                && (Unit(_settings.MistOpacity)) && (Unit(_settings.PaperOpacity))
                && (NonNegative(_settings.RuneStrength)) && (Valid(_settings.PetalTint))
                && (Unit(_settings.AssignedPetalTintShare)) && (NonNegative(_settings.GroundLightStrength))
                && (NonNegative(_settings.GroundLightIntensity)) && (Finite(_settings.GroundLightRange))
                && (_settings.GroundLightRange >= MIN_LIGHT_RANGE)
                && (Unit(_settings.AirMistOpacity)) && (Unit(_settings.SuccessMistOpacity))
                && (NonNegative(_settings.SuccessRise)) && (NonNegative(_settings.PaperSuccessRise));
        }

        private bool ValidRig()
        {
            if ((_layers == null) || (_layerKinds == null) || (_layers.Length != _layerKinds.Length)
                || (_glyphs == null) || (_glyphs.Length != SIGN_COUNT)
                || (_bones == null) || (_rest == null) || (_motion == null)
                || (_bones.Length != _rest.Length) || (_bones.Length != _motion.Length)
                || (_radiusRoot == null) || (_glyphRoot == null) || (_boundary == null)
                || (_progress == null) || (_pillar == null) || (_footLight == null))
            {
                return false;
            }
            for (int i = 0; i < _layers.Length; ++i)
            {
                if ((_layers[i] == null) || (_layerKinds[i] < RIBBON_LAYER) || (_layerKinds[i] > SUCCESS_MIST_LAYER))
                {
                    return false;
                }
            }
            for (int i = 0; i < _glyphs.Length; ++i)
            {
                if (_glyphs[i] == null)
                {
                    return false;
                }
            }
            for (int i = 0; i < _pillar.Length; ++i)
            {
                if (_pillar[i] == null)
                {
                    return false;
                }
            }
            for (int i = 0; i < _bones.Length; ++i)
            {
                Vector4 motion = _motion[i];
                if ((_bones[i] == null) || (!Valid(_rest[i])) || (!Finite(motion.x)) || (!Finite(motion.y))
                    || (!Finite(motion.z)) || (!Finite(motion.w)) || (motion.x < RIBBON_MOTION)
                    || (motion.x > SUCCESS_WISP_MOTION) || (motion.x != Mathf.Floor(motion.x))
                    || ((motion.x == SUCCESS_WISP_MOTION) && (!Unit(motion.y))))
                {
                    return false;
                }
            }
            return true;
        }

        private bool SameCast(Snapshot snapshot)
        {
            return (snapshot.CastId == _snapshot.CastId) && (snapshot.Sign == _snapshot.Sign)
                && (snapshot.Color.Equals(_snapshot.Color)) && (snapshot.WorldCenter.Equals(_snapshot.WorldCenter))
                && (snapshot.Radius == _snapshot.Radius) && (snapshot.AppearedAt == _snapshot.AppearedAt);
        }

        private bool ValidTransition(Snapshot snapshot)
        {
            if ((snapshot.Sequence <= _snapshot.Sequence) || (snapshot.SnapshotAt < _snapshot.SnapshotAt)
                || (snapshot.PhaseStartedAt < _snapshot.PhaseStartedAt))
            {
                return false;
            }
            bool terminal = (_snapshot.Phase == EPhase.Success) || (_snapshot.Phase == EPhase.Expired);
            if ((terminal) && ((snapshot.Phase != _snapshot.Phase) || (snapshot.PhaseStartedAt != _snapshot.PhaseStartedAt)))
            {
                return false;
            }
            if ((_snapshot.Phase == EPhase.Holding) && (snapshot.Phase == EPhase.Holding)
                && (snapshot.PhaseStartedAt == _snapshot.PhaseStartedAt)
                && (snapshot.HoldProgress < _snapshot.HoldProgress))
            {
                return false;
            }
            return true;
        }

        private static bool ValidSnapshot(Snapshot snapshot, double now)
        {
            if ((snapshot.CastId == 0) || (snapshot.Sequence == 0)
                || (snapshot.Sign < EWolbaekTalismanSign.Moon) || (snapshot.Sign > EWolbaekTalismanSign.Diamond)
                || (!Valid(snapshot.WorldCenter)) || (!Positive(snapshot.Radius)) || (!Valid(snapshot.Color))
                || (!Finite(snapshot.AppearedAt)) || (!Finite(snapshot.SnapshotAt)) || (!Finite(snapshot.PhaseStartedAt))
                || (snapshot.AppearedAt < 0d) || (snapshot.AppearedAt > snapshot.PhaseStartedAt)
                || (snapshot.PhaseStartedAt > snapshot.SnapshotAt) || (snapshot.SnapshotAt > now)
                || (snapshot.Phase < EPhase.Holding) || (snapshot.Phase > EPhase.Expired)
                || (!Finite(snapshot.HoldProgress)) || (snapshot.HoldProgress < 0f) || (snapshot.HoldProgress > 1f)
                || ((snapshot.Phase == EPhase.Reset) && (snapshot.HoldProgress != 0f))
                || ((snapshot.Phase == EPhase.Success) && (snapshot.HoldProgress != 1f)))
            {
                return false;
            }
            return true;
        }

        private static bool Finite(double value) => (!double.IsNaN(value)) && (!double.IsInfinity(value));
        private static bool Positive(float value) => (Finite(value)) && (value >= MIN_SIZE);
        private static bool NonNegative(float value) => (Finite(value)) && (value >= 0f);
        private static bool Unit(float value) => (NonNegative(value)) && (value <= 1f);
        private static bool Valid(Vector3 value) => (Finite(value.x)) && (Finite(value.y)) && (Finite(value.z));
        private static bool Valid(Color value) => (Finite(value.r)) && (Finite(value.g)) && (Finite(value.b))
            && (Finite(value.a)) && (value.r >= 0f) && (value.g >= 0f) && (value.b >= 0f) && (value.a >= 0f) && (value.a <= 1f);
        private static float Normalized(double age, float duration) => (float)Math.Max(0d, Math.Min(ENVELOPE_SECONDS, age / duration));
        private static float Phase(double age, float cycle) => (float)(age % cycle / cycle) * TAU;
    }
}
