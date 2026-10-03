// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 임시 표현이다. 여우불 하나(구체)가 보스 곁에서 서버가 정한 쪽으로 반원을 따라 이동해 공격 방향을 미리 알린다
    // 위치는 서버가 보낸 값과 서버 시각으로만 정하고 좌우 추첨과 타깃 추적을 하지 않는다. 늦게 받은 시작도 지금 시각의 위치부터 이어서 그린다
    // 구역 4개의 바닥 표시와 폭발은 이 컴포넌트가 아니라 구역마다 오는 부채꼴 예고와 서버 폭발 확정이 그린다
    /// <summary> 반월 연쇄 폭발의 여우불 이동 표현, 예고 동안 반원의 한가운데까지 가고 폭발이 진행되는 동안 나머지 반원을 따라간다 </summary>
    public sealed class BossSweepCueView : ClientView
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  ORBIT_RATIO       = 0.5f;  // 반경 대비 여우불이 도는 거리
        private const float  HEIGHT            = 1.2f;  // m, 바닥 위 높이
        private const float  SIZE              = 0.6f;  // m, 여우불 지름
        private const float  FADE_SECONDS      = 0.3f;  // 마지막 구역 폭발 뒤 사라지는 시간
        private const float  HALF              = 0.5f;
        private const float  MIN_SPAN_SECONDS  = 0.0001f;
        private const string ORB_NAME          = "BossSweepCue Orb";

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Material _material; // 비어 있으면 기본 재질로 보인다

        private Transform       _orb;
        private Func<double>    _serverClock;
        private BossSweepStart  _start;
        private bool            _isActive;
        private Vector3         _position;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsShown => _isActive;

        /// <summary> 진행 중인 시전 번호, 없으면 0이다 </summary>
        public int CastId => _isActive ? _start.CastId : 0;

        /// <summary> 가장 최근에 계산한 여우불 위치, 시험과 진단용이다 </summary>
        public Vector3 CurrentPosition => _position;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = ORB_NAME;
            if (orb.TryGetComponent(out Collider collider))
            {
                Destroy(collider);
            }

            if ((_material != null) && orb.TryGetComponent(out Renderer renderer))
            {
                renderer.sharedMaterial = _material;
            }

            _orb = orb.transform;
            _orb.SetParent(transform, false);
            _orb.localScale = Vector3.one * SIZE;
            orb.SetActive(false);
        }

        private void Update()
        {
            if (!_isActive)
            {
                return;
            }

            double now = (_serverClock != null) ? _serverClock() : Time.timeAsDouble;
            if (now >= (_start.EndServerTime + FADE_SECONDS))
            {
                Clear();
                return;
            }

            Place(now);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버가 알린 시작 값으로 이동을 시작한다, 이미 끝난 늦은 시작은 그리지 않는다 </summary>
        public bool Begin(in BossSweepStart start, Func<double> serverClock)
        {
            if (_orb == null)
            {
                return false;
            }

            _serverClock = serverClock;
            double now = (serverClock != null) ? serverClock() : Time.timeAsDouble;
            if (now >= (start.EndServerTime + FADE_SECONDS))
            {
                return false;
            }

            _start    = start;
            _isActive = true;
            _orb.gameObject.SetActive(true);
            Place(now);
            return true;
        }

        /// <summary> 진행 중인 시전의 이동을 지운다, 다른 시전 번호면 건드리지 않는다 </summary>
        public bool Clear(int castId)
        {
            if ((!_isActive) || (_start.CastId != castId))
            {
                return false;
            }

            Clear();
            return true;
        }

        /// <summary> 무엇이든 지운다 </summary>
        public void Clear()
        {
            _isActive = false;
            if (_orb != null)
            {
                _orb.gameObject.SetActive(false);
            }
        }

        // 예고 동안 반원 한가운데까지, 폭발이 진행되는 동안 나머지 반원을 따라간다. 표시 값이며 판정이나 서버 규칙이 아니다
        private void Place(double now)
        {
            float elapsed = (float)(now - _start.StartServerTime);
            float half    = _start.TotalArcAngle * HALF;
            float angle;
            if (elapsed <= _start.TelegraphSeconds)
            {
                angle = half * Mathf.Clamp01(elapsed / Mathf.Max(MIN_SPAN_SECONDS, _start.TelegraphSeconds));
            }
            else
            {
                float span = Mathf.Max(MIN_SPAN_SECONDS, _start.BlastSpanSeconds);
                angle = half + (half * Mathf.Clamp01((elapsed - _start.TelegraphSeconds) / span));
            }

            float side = _start.IsRight ? 1f : -1f;
            Vector3 direction = Quaternion.AngleAxis(side * angle, Vector3.up) * _start.BaseDirection;
            _position = _start.Origin + (direction * (_start.Radius * ORBIT_RATIO)) + (Vector3.up * HEIGHT);
            _orb.position = _position;
        }
    }
}
