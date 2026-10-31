// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 무철 팔 휘두르기 궤적, 붙은 본이 빠르게 움직이는 동안만 궤적을 그린다 </summary>
    [RequireComponent(typeof(TrailRenderer))]
    public sealed class MucheolArmTrail : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MIN_DELTA_TIME  = 0.0001f;
        private const float MIN_RANGE_SCALE = 0.01f;
        private const float MAX_SPEED       = 120f;   // m/s, 팔이 낼 수 없는 속도, 넘으면 순간이동으로 본다

        [SerializeField] private TrailRenderer  _trail;
        [SerializeField] private ParticleSystem _sparks;
        [SerializeField] private MucheolArmLightning _lightning;         // 손에 두르는 전기, 비우면 없다
        [SerializeField] private Transform      _facing;                 // 앞쪽의 기준, 비우면 최상위 오브젝트
        [SerializeField, Min(0f)] private float _startSpeed      = 6f;   // m/s, 이 속도를 넘으면 그리기 시작
        [SerializeField, Min(0f)] private float _stopSpeed       = 3f;   // m/s, 이 속도 아래로 내려가면 그리기 멈춤
        [SerializeField, Min(0f)] private float _forwardSpeedMin = 4f;   // m/s, 스파크에 앞쪽으로 얹는 속도의 하한
        [SerializeField, Min(0f)] private float _forwardSpeedMax = 12f;  // m/s, 스파크에 앞쪽으로 얹는 속도의 상한

        private Vector3 _lastPosition;
        private bool    _hasLast;
        private float   _rangeScale = 1f;
        private float   _baseSpeedMin;
        private float   _baseSpeedMax;
        private float   _baseRate;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Reset()
        {
            _trail = GetComponent<TrailRenderer>();
        }

        private void Awake()
        {
            if (_sparks == null)
            {
                return;
            }

            ParticleSystem.MinMaxCurve startSpeed = _sparks.main.startSpeed;
            _baseSpeedMin = startSpeed.constantMin;
            _baseSpeedMax = startSpeed.constantMax;
            _baseRate     = _sparks.emission.rateOverDistance.constant;
        }

        private void OnEnable()
        {
            _hasLast = false;
            _trail.Clear();
            SetEmitting(false);
        }

        private void LateUpdate()
        {
            Vector3 position = transform.position;
            if (!_hasLast)
            {
                _lastPosition = position;
                _hasLast = true;
                return;
            }

            float speed = (position - _lastPosition).magnitude / Mathf.Max(Time.deltaTime, MIN_DELTA_TIME);
            _lastPosition = position;

            // 정지 시 자세 복원처럼 순간이동한 프레임은 휘두른 것이 아니므로 궤적에 넣지 않는다
            if (speed > MAX_SPEED)
            {
                _trail.Clear();
                SetEmitting(false);
                return;
            }

            if (_trail.emitting)
            {
                SetEmitting(speed > _stopSpeed);
            }
            else if (speed > _startSpeed)
            {
                SetEmitting(true);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 스파크가 앞쪽으로 날아갈 방향의 기준, 보스 몸체를 넘긴다 </summary>
        public void SetFacing(Transform facing)
        {
            _facing = facing;
        }

        /// <summary> 공격 범위 배율, 스파크는 범위만큼 멀리(속도 비례) 범위 넓이만큼 많이(배율 제곱) 튄다. 이지는 1이다 </summary>
        public void SetRangeScale(float scale)
        {
            _rangeScale = Mathf.Max(scale, MIN_RANGE_SCALE);
            if (_lightning != null)
            {
                _lightning.SetRangeScale(_rangeScale);
            }

            if (_sparks == null)
            {
                return;
            }

            ParticleSystem.MainModule main = _sparks.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(_baseSpeedMin * _rangeScale, _baseSpeedMax * _rangeScale);
            ParticleSystem.EmissionModule emission = _sparks.emission;
            emission.rateOverDistance = _baseRate * _rangeScale * _rangeScale;
        }

        // 스파크는 이미 튄 입자가 자연스럽게 사라지도록 방출만 끈다
        private void SetEmitting(bool isEmitting)
        {
            _trail.emitting = isEmitting;
            if (_lightning != null)
            {
                _lightning.SetSwinging(isEmitting);
            }

            if (_sparks == null)
            {
                return;
            }

            ParticleSystem.EmissionModule emission = _sparks.emission;
            emission.enabled = isEmitting;
            if (isEmitting)
            {
                ApplyForwardVelocity();
            }
        }

        // 손은 휘두르며 회전하므로 본의 앞이 아니라 보스가 바라보는 방향으로 월드 속도를 얹는다
        private void ApplyForwardVelocity()
        {
            Vector3 forward = ((_facing != null) ? _facing : transform.root).forward;
            ParticleSystem.VelocityOverLifetimeModule velocity = _sparks.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            float min = _forwardSpeedMin * _rangeScale;
            float max = _forwardSpeedMax * _rangeScale;
            velocity.x = new ParticleSystem.MinMaxCurve(forward.x * min, forward.x * max);
            velocity.y = new ParticleSystem.MinMaxCurve(forward.y * min, forward.y * max);
            velocity.z = new ParticleSystem.MinMaxCurve(forward.z * min, forward.z * max);
        }
    }
}
