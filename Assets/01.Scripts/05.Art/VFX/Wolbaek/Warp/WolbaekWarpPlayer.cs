// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 캐릭터 원본을 수정하지 않는 표현. 착지 성공 입력과 타격 확정을 분리한다 </summary>
    public sealed class WolbaekWarpPlayer : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/
        private const float MIN_SIZE = 0.001f;
        private const float SOURCE_WIDTH = 0.98f;
        private const float SOURCE_HEIGHT = 1.92f;
        private const float SOURCE_DEPTH = 0.5f;
        private const float FRONT_RANGE = 1.25f;
        private const float FRONT_SOFTNESS = 4f;
        private const float TAU = Mathf.PI * 2f;
        private const float FLOW_ALONG = 7f;
        private const float FLOW_SECONDARY = 0.61f;
        private const float FLOW_LATERAL = 0.055f;
        private const float FLOW_VERTICAL = 0.08f;
        private const float PETAL_TURN = 160f;
        private const float PETAL_UP = 0.35f;
        private const float BODY_NOISE_START = 0.02f;
        private const float WARNING_ALPHA = 0.55f;
        private const int GROUND_MIST_KIND       = 2;
        private const int RESIDUAL_KIND_START    = 3;
        private const int FOOT_CORE_KIND         = 5;
        private const float PETAL_MOTION_KIND    = 1f;
        private const float GROUND_PETAL_MOTION  = 2f;
        private const float MOTE_MOTION_KIND     = 3f;
        private const float FOOT_CORE_MOTION     = 4f;
        private const float GROUND_MIST_OPACITY  = 0.45f;
        private const float FOOT_CORE_OPACITY    = 0.85f;
        private const float RESIDUAL_FADE_POWER  = 3f;
        private const float PETAL_PITCH_DEGREES  = 12f;
        private const float PETAL_YAW_DEGREES    = 18f;
        private const float PETAL_ROLL_DEGREES   = 35f;
        private const float MOTE_DRIFT           = 0.2f;
        private const float GROUND_PETAL_DRIFT   = 0.035f;
        private const float GROUND_PETAL_LIFT    = 0.015f;
        private const float MOTE_PITCH_DEGREES   = 20f;
        private const float MOTE_YAW_DEGREES     = 25f;
        private const float GROUND_TURN_DEGREES  = 8f;
        private const float RESIDUAL_PETAL_SHARE = 0.12f;
        private static readonly int G_Color = Shader.PropertyToID("_Color");
        private static readonly int G_Map = Shader.PropertyToID("_BaseMap");
        private static readonly int G_MapST = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int G_Dissolve = Shader.PropertyToID("_Dissolve");

        [SerializeField] private EWolbaekWarpKind _kind;
        [SerializeField] private WolbaekWarpSettingsSO _settings;
        [SerializeField] private Renderer[] _bodySlices;
        [SerializeField] private Renderer[] _energy;
        [SerializeField] private int[] _energyKinds;
        [SerializeField] private Transform[] _bones;
        [SerializeField] private Vector3[] _rest;
        [SerializeField] private Vector4[] _motion;
        [SerializeField] private Texture _bodyNoise;
        [SerializeField] private Renderer _boundary;
        [SerializeField] private Transform _shockRoot;
        [SerializeField] private ParticleSystem[] _shockwaves;

        private MaterialPropertyBlock _properties;
        private bool _running;
        private bool _confirmed;
        private bool _showProxy;
        private uint _playbackId;
        private double _started;
        private double _expectedHit;
        private double _lastNow;
        private float _duration;
        private float _impact;
        private float _cycle;
        private float _drift;
        private float _opacity;
        private float _grace;
        private float _residual;
        private Vector3 _worldScale;

        /****************************************
        *              Properties
        ****************************************/
        public EWolbaekWarpKind Kind => _kind;
        public WolbaekWarpSettingsSO Settings => _settings;
        public bool IsRunning => _running;
        public bool IsStrikeConfirmed => _confirmed;
        public uint PlaybackId => _playbackId;
        public ulong CastId { get; private set; }
        public event Action<uint> Completed;

        /****************************************
        *              Unity Event
        ****************************************/
        protected override void OnViewAwake()
        {
            _properties = new MaterialPropertyBlock();
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

        /****************************************
        *                Methods
        ****************************************/
        /// <summary> bounds는 외부 캐릭터의 확정 표현 범위다. neutralProxy는 개발용 인체를 명시적으로 표시한다 </summary>
        public uint PlayBody(ulong castId, Bounds bounds, double started, double now, bool neutralProxy = false)
        {
            if ((_kind == EWolbaekWarpKind.Landing) || (!Ready(now)) || (!Finite(started))
                || (started > now) || (!Valid(bounds)))
            {
                return 0;
            }

            Begin(castId, now);
            _started = started;
            _showProxy = neutralProxy;
            transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), Quaternion.identity);
            _worldScale = new Vector3(bounds.size.x / SOURCE_WIDTH, bounds.size.y / SOURCE_HEIGHT, bounds.size.z / SOURCE_DEPTH);
            transform.localScale = _worldScale;
            Tick(_playbackId, now);
            return _playbackId;
        }

        /// <summary> 서버가 실제 착지 성공을 알린 이후에만 호출한다. 후보 목적지 예고 API는 없다 </summary>
        public uint PlayLandedWarning(ulong castId, Vector3 actualLanding, float radius,
            double warningStart, double expectedHit, double now)
        {
            if ((_kind != EWolbaekWarpKind.Landing) || (!Ready(now)) || (!Valid(actualLanding))
                || (!Finite(radius)) || (radius < MIN_SIZE) || (!Finite(warningStart)) || (!Finite(expectedHit))
                || (warningStart > now) || (expectedHit < warningStart))
            {
                return 0;
            }

            Begin(castId, now);
            _started = warningStart;
            _expectedHit = expectedHit;
            transform.SetPositionAndRotation(actualLanding, Quaternion.identity);
            transform.localScale = Vector3.one;
            _worldScale = Vector3.one;
            _boundary.transform.localScale = new Vector3(radius, 1f, radius);
            _shockRoot.localScale = new Vector3(radius, 1f, radius);
            Tick(_playbackId, now);
            return _playbackId;
        }

        public bool ConfirmStrike(uint playbackId, double hitTime, double now)
        {
            if ((_kind != EWolbaekWarpKind.Landing) || (!Current(playbackId, now)) || (_confirmed)
                || (!Finite(hitTime)) || (hitTime < _started) || (hitTime > now) || (now > _expectedHit + _grace))
            {
                return false;
            }

            _confirmed = true;
            _started = hitTime;
            for (int i = 0; i < _shockwaves.Length; ++i)
            {
                ParticleSystem.MainModule main = _shockwaves[i].main;
                main.startLifetime = _impact;
            }
            Tick(playbackId, now);
            return true;
        }

        public void Tick(uint playbackId, double now)
        {
            if (!Current(playbackId, now))
            {
                return;
            }

            _lastNow = now;
            float age = (float)(now - _started);
            if (_kind == EWolbaekWarpKind.Landing)
            {
                if (!_confirmed)
                {
                    if (now > _expectedHit + _grace)
                    {
                        Stop();
                        return;
                    }
                    _boundary.enabled = true;
                    SetColor(_boundary, new Color(0.7f, 0.9f, 1f, WARNING_ALPHA), 0f);
                    return;
                }
                if (age >= _impact)
                {
                    Complete(playbackId);
                    return;
                }
                _boundary.enabled = true;
                SetColor(_boundary, new Color(0.7f, 0.9f, 1f, 1f - age / _impact), 0f);
                for (int i = 0; i < _shockwaves.Length; ++i)
                {
                    _shockwaves[i].Simulate(age, false, true, false);
                }
                AnimateEnergy(Mathf.Clamp01(age / _impact), age, false, true);
                return;
            }

            float progress = Mathf.Clamp01(age / _duration);
            if (age >= _duration + _residual)
            {
                Complete(playbackId);
                return;
            }
            bool reform = _kind == EWolbaekWarpKind.Reform;
            for (int i = 0; i < _bodySlices.Length; ++i)
            {
                float height = i / (float)_bodySlices.Length;
                float local = Mathf.Clamp01((progress * FRONT_RANGE - height) * FRONT_SOFTNESS);
                float alpha = reform ? local : 1f - local;
                Renderer slice = _bodySlices[i];
                slice.enabled = _showProxy && age < _duration && alpha > MIN_SIZE;
                _properties.Clear();
                _properties.SetFloat(G_Dissolve, 1f - alpha);
                slice.SetPropertyBlock(_properties);
            }
            AnimateEnergy(progress, age, reform, false);
        }

        private void AnimateEnergy(float progress, float age, bool reform, bool landing)
        {
            float bodyGlow = Mathf.Sin(progress * Mathf.PI) * _opacity;
            float remaining = Mathf.Clamp01(1f - Mathf.Max(0f, age - _duration) / Mathf.Max(MIN_SIZE, _residual));
            for (int i = 0; i < _energy.Length; ++i)
            {
                int kind = _energyKinds[i];
                float glow = kind >= GROUND_MIST_KIND ? remaining * _opacity : bodyGlow;
                if ((kind == GROUND_MIST_KIND) || (kind == FOOT_CORE_KIND))
                {
                    glow = age < _duration ? bodyGlow * (kind == GROUND_MIST_KIND ? GROUND_MIST_OPACITY : FOOT_CORE_OPACITY) : 0f;
                }
                else if (kind >= RESIDUAL_KIND_START)
                {
                    glow *= age < _duration ? 1f : Mathf.Pow(remaining, RESIDUAL_FADE_POWER);
                }
                if (landing)
                {
                    glow = Mathf.Sin(progress * Mathf.PI) * _opacity;
                }
                _energy[i].enabled = glow > MIN_SIZE;
                SetColor(_energy[i], new Color(1f, 1f, 1f, glow), ((kind == 0) || (kind == GROUND_MIST_KIND)) ? age / _cycle : 0f);
            }
            for (int i = 0; i < _bones.Length; ++i)
            {
                Vector3 rest = _rest[i];
                Vector4 data = _motion[i];
                float phase = age * TAU / _cycle - rest.y * FLOW_ALONG + data.z;
                if (data.x < PETAL_MOTION_KIND)
                {
                    Vector3 offset = new Vector3(Mathf.Sin(phase) * FLOW_LATERAL,
                        (Mathf.Sin(phase * FLOW_SECONDARY + data.z) + Mathf.Sin(phase)) * FLOW_VERTICAL,
                        Mathf.Cos(phase * FLOW_SECONDARY) * FLOW_LATERAL);
                    _bones[i].localPosition = rest + offset;
                    _bones[i].localRotation = Quaternion.identity;
                    _bones[i].localScale = Vector3.one;
                }
                else if (data.x < GROUND_PETAL_MOTION)
                {
                    float local = Mathf.Clamp01((progress * FRONT_RANGE - data.y) * FRONT_SOFTNESS);
                    float outward = reform ? 1f - local : local;
                    float fade = Mathf.Sin(local * Mathf.PI);
                    Vector3 offset = new Vector3(Mathf.Sin(data.z + outward) * _drift * outward,
                        PETAL_UP * outward + Mathf.Sin(phase) * FLOW_VERTICAL,
                        Mathf.Cos(data.z + outward) * _drift * outward);
                    _bones[i].localPosition = rest + offset;
                    _bones[i].localRotation = Quaternion.Euler(Mathf.Sin(phase) * PETAL_PITCH_DEGREES,
                        Mathf.Sin(data.z + phase * FLOW_SECONDARY) * PETAL_YAW_DEGREES, phase * PETAL_ROLL_DEGREES * data.w);
                    _bones[i].localScale = new Vector3(1f / _worldScale.x, 1f / _worldScale.y, 1f / _worldScale.z) * fade;
                }
                else
                {
                    float speed = data.x == MOTE_MOTION_KIND ? MOTE_DRIFT : data.x == FOOT_CORE_MOTION ? 0f : GROUND_PETAL_DRIFT;
                    Vector3 drift = new Vector3(Mathf.Sin(phase) * speed,
                        data.x == MOTE_MOTION_KIND ? age * speed : data.x == FOOT_CORE_MOTION ? 0f : GROUND_PETAL_LIFT,
                        Mathf.Cos(phase * FLOW_SECONDARY) * speed);
                    _bones[i].localPosition = rest + drift;
                    _bones[i].localRotation = data.x == MOTE_MOTION_KIND
                        ? Quaternion.Euler(phase * MOTE_PITCH_DEGREES, phase * MOTE_YAW_DEGREES, 0f)
                        : data.x == FOOT_CORE_MOTION ? Quaternion.identity : Quaternion.Euler(0f, phase * GROUND_TURN_DEGREES, 0f);
                    _bones[i].localScale = new Vector3(1f / _worldScale.x, 1f / _worldScale.y, 1f / _worldScale.z);
                    if ((data.x == GROUND_PETAL_MOTION) && (age >= _duration)
                        && (Mathf.Repeat(data.z, 1f) >= RESIDUAL_PETAL_SHARE))
                    {
                        _bones[i].localScale = Vector3.zero;
                    }
                }
            }
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

        [ContextMenu("Stop")]
        public void Stop()
        {
            _running = false;
            _confirmed = false;
            if (_bodySlices != null)
            {
                for (int i = 0; i < _bodySlices.Length; ++i)
                {
                    _bodySlices[i].enabled = false;
                }
            }
            if (_energy != null)
            {
                for (int i = 0; i < _energy.Length; ++i)
                {
                    _energy[i].enabled = false;
                }
            }
            if (_boundary != null)
            {
                _boundary.enabled = false;
            }
            if (_shockwaves != null)
            {
                for (int i = 0; i < _shockwaves.Length; ++i)
                {
                    _shockwaves[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        private void Begin(ulong castId, double now)
        {
            Stop();
            ++_playbackId;
            if (_playbackId == 0)
            {
                ++_playbackId;
            }
            CastId = castId;
            _running = true;
            _lastNow = now;
            _duration = Positive(_settings.BodySeconds);
            _impact = Positive(_settings.ImpactSeconds);
            _cycle = Positive(_settings.FlowCycle);
            _drift = Mathf.Max(0f, Safe(_settings.PetalDrift));
            _opacity = Mathf.Clamp01(Safe(_settings.MistOpacity));
            _grace = Mathf.Max(0f, Safe(_settings.ConfirmationGrace));
            _residual = Mathf.Max(0f, Safe(_settings.ResidualSeconds));
        }

        private void Complete(uint playbackId)
        {
            Stop();
            Completed?.Invoke(playbackId);
        }

        private bool Ready(double now) => !IsServerBuild && _properties != null && _settings != null && gameObject.activeInHierarchy && Finite(now);
        private bool Current(uint id, double now) => _running && id == _playbackId && Finite(now) && now >= _lastNow;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool Valid(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Valid(Bounds bounds) => Valid(bounds.center) && Valid(bounds.size) && bounds.size.x >= MIN_SIZE && bounds.size.y >= MIN_SIZE && bounds.size.z >= MIN_SIZE;
        private static float Safe(float value) => Finite(value) ? value : 0f;
        private static float Positive(float value) => Mathf.Max(MIN_SIZE, Safe(value));

        private void SetColor(Renderer renderer, Color color, float flow)
        {
            _properties.Clear();
            _properties.SetColor(G_Color, color);
            _properties.SetVector(G_MapST, new Vector4(1f, 1f, 0f, flow));
            renderer.SetPropertyBlock(_properties);
        }
    }
}
