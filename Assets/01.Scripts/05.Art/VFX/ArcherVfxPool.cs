// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 궁수 효과 프리팹 하나의 고정 크기 풀, 생성 때만 Instantiate와 GetComponentsInChildren을 쓰고 재생 중에는 할당하지 않는다. 시각은 모두 서버 시각이다 </summary>
    public sealed class ArcherVfxPool
    {
        /****************************************
        *                Fields
        ****************************************/

        // 투사체가 사거리 끝에 닿거나 서버 적중 신호로 멈춘 뒤 남은 입자가 흩어지도록 두는 시간
        private const double FADE_SECONDS = 0.2;

        private enum ESlotMode : byte
        {
            Idle,
            Brief,
            Projectile,
            Fading
        }

        private readonly GameObject[]       _objects;
        private readonly Transform[]        _transforms;
        private readonly ParticleSystem[][] _systems;
        private readonly TrailRenderer[][]  _trails;
        private readonly ESlotMode[]        _modes;
        private readonly double[]           _expireAt;
        private readonly Vector3[]          _origin;
        private readonly Vector3[]          _direction;
        private readonly double[]           _startAt;
        private readonly float[]            _speed;
        private readonly float[]            _range;
        private readonly bool[]             _stopsOnHit;
        private int                         _next;

        /****************************************
        *              Properties
        ****************************************/

        public bool HasPrefab { get; }

        public int SlotCount => (_objects != null) ? _objects.Length : 0;

        public int ActiveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < SlotCount; ++i)
                {
                    if (_modes[i] != ESlotMode.Idle)
                    {
                        ++count;
                    }
                }

                return count;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public ArcherVfxPool(GameObject prefab, Transform root, int size)
        {
            HasPrefab = (prefab != null) && (size > 0);
            if (!HasPrefab)
            {
                return;
            }

            _objects    = new GameObject[size];
            _transforms = new Transform[size];
            _systems    = new ParticleSystem[size][];
            _trails     = new TrailRenderer[size][];
            _modes      = new ESlotMode[size];
            _expireAt   = new double[size];
            _origin     = new Vector3[size];
            _direction  = new Vector3[size];
            _startAt    = new double[size];
            _speed      = new float[size];
            _range      = new float[size];
            _stopsOnHit = new bool[size];
            for (int i = 0; i < size; ++i)
            {
                GameObject instance = UnityEngine.Object.Instantiate(prefab, root);
                instance.SetActive(false);
                _objects[i]    = instance;
                _transforms[i] = instance.transform;
                _systems[i]    = instance.GetComponentsInChildren<ParticleSystem>(true);
                _trails[i]     = instance.GetComponentsInChildren<TrailRenderer>(true);
                StopTrails(i);
            }
        }

        public bool IsSlotActive(int index)
        {
            return (index >= 0) && (index < SlotCount) && (_modes[index] != ESlotMode.Idle);
        }

        public Vector3 GetSlotPosition(int index)
        {
            return _transforms[index].position;
        }

        /// <summary> 총구 효과, 입자가 모두 끝나거나 상한 시간이 지나면 정리한다 </summary>
        public void PlayBrief(Vector3 position, Quaternion rotation, double now, float maxSeconds)
        {
            if (!HasPrefab)
            {
                return;
            }

            int index = Acquire();
            _transforms[index].SetPositionAndRotation(position, rotation);
            Begin(index, ESlotMode.Brief, now + maxSeconds);
        }

        /// <summary> 서버 출발 시각 기준의 직선 투사체, 위치는 속도 곱하기 경과 시간으로만 정하며 사거리에서 멈춘다 </summary>
        public void PlayProjectile(Vector3 origin, Vector3 direction, double startAt, float speed, float range, double now,
            bool stopsOnHit = false)
        {
            if ((!HasPrefab) || (speed <= 0f) || (range <= 0f))
            {
                return;
            }

            direction.y = 0f;
            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                direction = Vector3.forward;
            }

            direction.Normalize();
            int index = Acquire();
            _origin[index]    = origin;
            _direction[index] = direction;
            _startAt[index]   = startAt;
            _speed[index]     = speed;
            _range[index]     = range;
            _stopsOnHit[index] = stopsOnHit;
            _transforms[index].SetPositionAndRotation(origin + (direction * ArcherVfxTracker.Distance(speed, range, now - startAt)),
                Quaternion.LookRotation(direction));
            Begin(index, ESlotMode.Projectile, 0.0);
        }

        /// <summary> 만료, 입자 종료, 투사체 이동과 사거리 도달을 처리한다 </summary>
        public void Tick(double now)
        {
            for (int i = 0; i < SlotCount; ++i)
            {
                if (_modes[i] == ESlotMode.Idle)
                {
                    continue;
                }

                if (_objects[i] == null)
                {
                    _modes[i] = ESlotMode.Idle;
                    continue;
                }

                switch (_modes[i])
                {
                    case ESlotMode.Brief:
                        if ((now >= _expireAt[i]) || (!IsAlive(i)))
                        {
                            StopSlot(i);
                        }

                        break;
                    case ESlotMode.Fading:
                        if (now >= _expireAt[i])
                        {
                            StopSlot(i);
                        }

                        break;
                    case ESlotMode.Projectile:
                        TickProjectile(i, now);
                        break;
                    default:
                        break;
                }
            }
        }

        /// <summary> 적중으로 멈추는 비행 투사체 중 진행선이 적중 위치에 가장 가까운 것을 적중 거리에서 멈춘다, 없으면 거짓 </summary>
        /// <remarks> 적중 신호는 지연만큼 늦게 오므로 이미 지나친 화살은 적중 거리로 되돌려 흩어지게 하고, 아직 못 닿은 화살은 그 거리까지만 날린다 </remarks>
        public bool StopProjectileAtHit(Vector3 hitPosition, float maxLateral, double now)
        {
            int   nearest = -1;
            float best    = float.MaxValue;
            float stopAt  = 0f;
            for (int i = 0; i < SlotCount; ++i)
            {
                if ((_modes[i] != ESlotMode.Projectile) || (!_stopsOnHit[i]) || (_objects[i] == null))
                {
                    continue;
                }

                if ((ArcherVfxTracker.TryGetHitStopDistance(_origin[i], _direction[i], _range[i], hitPosition, maxLateral,
                        out float distance, out float lateral))
                    && (lateral < best))
                {
                    best    = lateral;
                    stopAt  = distance;
                    nearest = i;
                }
            }

            if (nearest < 0)
            {
                return false;
            }

            // 화살 하나는 한 번만 맞으므로 다음 적중 신호는 다른 화살을 고른다
            _range[nearest]      = stopAt;
            _stopsOnHit[nearest] = false;
            TickProjectile(nearest, now);
            return true;
        }

        public void StopAll()
        {
            for (int i = 0; i < SlotCount; ++i)
            {
                StopSlot(i);
            }
        }

        private void TickProjectile(int index, double now)
        {
            float distance = ArcherVfxTracker.Distance(_speed[index], _range[index], now - _startAt[index]);
            _transforms[index].position = _origin[index] + (_direction[index] * distance);
            if (distance < _range[index])
            {
                return;
            }

            BeginFade(index, now);
        }

        // 입자 방출만 멈추고 남은 입자와 궤적이 잠깐 흩어진 뒤 정리한다
        private void BeginFade(int index, double now)
        {
            ParticleSystem[] systems = _systems[index];
            for (int i = 0; i < systems.Length; ++i)
            {
                systems[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }

            _modes[index]    = ESlotMode.Fading;
            _expireAt[index] = now + FADE_SECONDS;
            foreach (TrailRenderer trail in _trails[index])
            {
                trail.emitting = false;
            }
        }

        // 비어 있는 칸을 먼저 쓰고, 모두 재생 중이면 돌아가며 가장 먼저 쓴 칸을 다시 시작한다
        private int Acquire()
        {
            int index = -1;
            for (int i = 0; i < _objects.Length; ++i)
            {
                if (_modes[i] == ESlotMode.Idle)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                index = _next;
            }

            _next = (index + 1) % _objects.Length;
            StopSlot(index);
            return index;
        }

        private void Begin(int index, ESlotMode mode, double expireAt)
        {
            if (_objects[index] == null)
            {
                return;
            }

            _objects[index].SetActive(true);
            // 위치를 새 발사지점으로 바꾼 뒤 지워야 이전 발사의 끝과 이어지지 않는다.
            foreach (TrailRenderer trail in _trails[index])
            {
                trail.Clear();
                trail.emitting = true;
            }

            ParticleSystem[] systems = _systems[index];
            for (int i = 0; i < systems.Length; ++i)
            {
                systems[i].Clear(false);
                systems[i].Play(false);
            }

            _modes[index]    = mode;
            _expireAt[index] = expireAt;
        }

        private bool IsAlive(int index)
        {
            ParticleSystem[] systems = _systems[index];
            for (int i = 0; i < systems.Length; ++i)
            {
                if (systems[i].IsAlive(false))
                {
                    return true;
                }
            }

            return false;
        }

        private void StopSlot(int index)
        {
            _modes[index] = ESlotMode.Idle;
            if (_objects[index] == null)
            {
                return;
            }

            ParticleSystem[] systems = _systems[index];
            for (int i = 0; i < systems.Length; ++i)
            {
                systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            _objects[index].SetActive(false);
            StopTrails(index);
        }

        private void StopTrails(int index)
        {
            foreach (TrailRenderer trail in _trails[index])
            {
                trail.emitting = false;
                trail.Clear();
            }
        }
    }
}
