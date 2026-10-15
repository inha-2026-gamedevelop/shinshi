// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 서버 확정 시간과 영역을 그리는 무철 바닥 효과, 피해 판정은 하지 않는다 </summary>
    public sealed class MucheolGroundVfx : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MIN_EXTENT = 0.001f;
        private const float HALF = 0.5f;
        private const float FULL_ANGLE = 360f;
        private static readonly int SHAPE_ID = Shader.PropertyToID("_Shape");
        private static readonly int RADIUS_ID = Shader.PropertyToID("_Radius");
        private static readonly int ANGLE_ID = Shader.PropertyToID("_Angle");
        private static readonly int WIDTH_ID = Shader.PropertyToID("_Width");
        private static readonly int LENGTH_ID = Shader.PropertyToID("_Length");
        private static readonly int EXTENT_ID = Shader.PropertyToID("_Extent");
        private static readonly int PROGRESS_ID = Shader.PropertyToID("_Progress");
        private static readonly int IMPACT_ID = Shader.PropertyToID("_Impact");
        private static readonly int OPACITY_ID = Shader.PropertyToID("_Opacity");
        private static readonly int PHASE_ID = Shader.PropertyToID("_Phase");

        [SerializeField] private MeshRenderer _surface;
        [SerializeField] private ParticleSystem[] _impactParticles;
        [SerializeField, Min(MIN_EXTENT)] private float _impactLifetime = 1.2f;

        // 풀 반납으로 지우면 뒤늦은 시작 메시지가 취소된 예고를 되살린다
        private readonly HashSet<(ulong, int)> _retired = new HashSet<(ulong, int)>();
        private MaterialPropertyBlock _block;
        private ulong _castId;
        private int _effectId;
        private bool _running;
        private bool _impacted;
        private double _start;
        private double _activate;
        private double _end;
        private double _hitTime;
        private Vector3 _origin;
        private Quaternion _rotation;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsRunning => _running;
        public bool HasConfirmedImpact => _impacted;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _block = new MaterialPropertyBlock();
            Hide();
        }

        protected override void OnServerBuildAwake() { Hide(); }

        private void OnDisable()
        {
            RetireCurrent();
            Hide();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 월드 고정 영역을 시작한다, 한 인스턴스는 한 타만 소유한다 </summary>
        public void Begin(ulong castId, int effectId, Vector3 origin, float yaw,
            EMucheolVfxShape shape, float radius, float angle, float width, float length,
            double start, double activate, double end, double now)
        {
            if ((IsServerBuild) || (!isActiveAndEnabled) || (_surface == null)
                || (_retired.Contains((castId, effectId))))
            {
                return;
            }
            if ((_running) && (_castId == castId) && (_effectId == effectId))
            {
                Tick(now);
                return;
            }
            if ((!Finite(start)) || (!Finite(activate)) || (!Finite(end)) || (!Finite(now))
                || (activate < start) || (end < activate) || (end <= now)
                || (!Finite(origin.x)) || (!Finite(origin.y)) || (!Finite(origin.z))
                || (!Finite(yaw)) || (!Finite(radius)) || (!Finite(angle))
                || (!Finite(width)) || (!Finite(length))
                || (radius < 0f) || (angle < 0f) || (angle > FULL_ANGLE)
                || (width < 0f) || (length < 0f)
                || (shape < EMucheolVfxShape.Circle) || (shape > EMucheolVfxShape.Rectangle))
            {
                _retired.Add((castId, effectId));
                return;
            }

            RetireCurrent();
            Hide();
            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }
            _castId = castId;
            _effectId = effectId;
            _start = start;
            _activate = activate;
            _end = end;
            _origin = origin;
            _rotation = Quaternion.Euler(0f, yaw, 0f);
            _running = true;
            _impacted = false;
            float extent = Mathf.Max(MIN_EXTENT, Mathf.Max(radius, Mathf.Max(width * HALF, length)));
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(_origin, _rotation);
            transform.localScale = Vector3.one;
            _surface.transform.localScale = new Vector3(extent, 1f, extent);
            _block.Clear();
            _block.SetFloat(SHAPE_ID, (float)shape);
            _block.SetFloat(RADIUS_ID, radius);
            _block.SetFloat(ANGLE_ID, angle);
            _block.SetFloat(WIDTH_ID, width);
            _block.SetFloat(LENGTH_ID, length);
            _block.SetFloat(EXTENT_ID, extent);
            Tick(now);
        }

        /// <summary> 서버 타격 확인에서만 타격 연출을 시작하며 지연된 시간만큼 이어 그린다 </summary>
        public void ConfirmImpact(ulong castId, int effectId, double serverHitTime, double now)
        {
            if ((IsServerBuild) || (!Finite(serverHitTime)) || (!Finite(now)) || (serverHitTime > now))
            {
                return;
            }
            if ((!_running) || (_castId != castId) || (_effectId != effectId))
            {
                _retired.Add((castId, effectId));
                return;
            }
            if (_impacted)
            {
                return;
            }
            _retired.Add((castId, effectId));
            _impacted = true;
            _hitTime = serverHitTime;
            if ((now < _end) && (now < _hitTime + _impactLifetime))
            {
                PlayParticles((float)(now - _hitTime));
            }
            Tick(now);
        }

        /// <summary> 아직 발동하지 않은 타만 취소한다 </summary>
        public void Cancel(ulong castId, int effectId)
        {
            _retired.Add((castId, effectId));
            if ((!_running) || (_castId != castId) || (_effectId != effectId) || (_impacted))
            {
                return;
            }
            _running = false;
            Hide();
        }

        /// <summary> 호출자가 서버 시각을 전달한다, 시간 도달만으로 타격을 생성하지 않는다 </summary>
        public void Tick(double now)
        {
            if ((IsServerBuild) || (!_running) || (!Finite(now)) || (_surface == null))
            {
                return;
            }
            if ((now >= _end) || ((_impacted) && (now >= _hitTime + _impactLifetime)))
            {
                RetireCurrent();
                Hide();
                return;
            }
            transform.SetPositionAndRotation(_origin, _rotation);
            _surface.enabled = (now >= _start) && ((_impacted) || (now < _activate));
            double duration = Math.Max(MIN_EXTENT, _activate - _start);
            float phase = (float)Math.Max(0d, now - (_impacted ? _hitTime : _start));
            _block.SetFloat(PROGRESS_ID, Mathf.Clamp01((float)((now - _start) / duration)));
            _block.SetFloat(IMPACT_ID, _impacted ? 1f : 0f);
            _block.SetFloat(OPACITY_ID, _impacted ? 1f - Mathf.Clamp01(phase / _impactLifetime) : 1f);
            _block.SetFloat(PHASE_ID, phase);
            _surface.SetPropertyBlock(_block);
        }

        /// <summary> 이전 전투의 메시지가 더 오지 않는 세션 경계에서만 기록을 지운다 </summary>
        public void ResetHistory()
        {
            Hide();
            _running = false;
            _impacted = false;
            _retired.Clear();
        }

        private void RetireCurrent()
        {
            if (_running)
            {
                _retired.Add((_castId, _effectId));
            }
            _running = false;
        }

        private void Hide()
        {
            if (_surface != null)
            {
                _surface.enabled = false;
                _surface.SetPropertyBlock(null);
            }
            if (_impactParticles == null)
            {
                return;
            }
            for (int i = 0; i < _impactParticles.Length; ++i)
            {
                if (_impactParticles[i] != null)
                {
                    _impactParticles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        private void PlayParticles(float elapsed)
        {
            if (_impactParticles == null)
            {
                return;
            }
            for (int i = 0; i < _impactParticles.Length; ++i)
            {
                ParticleSystem particle = _impactParticles[i];
                if (particle == null)
                {
                    continue;
                }
                particle.Simulate(elapsed, true, true, true);
                particle.Play(true);
            }
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
