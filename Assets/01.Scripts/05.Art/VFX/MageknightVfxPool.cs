// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 마검사 효과 프리팹 하나의 고정 크기 풀, 생성 때만 Instantiate와 GetComponentsInChildren을 쓰고 재생 중에는 할당하지 않는다. 시각은 모두 서버 시각이다 </summary>
    public sealed class MageknightVfxPool
    {
        /****************************************
        *                Fields
        ****************************************/

        // 검기가 사거리 끝에 닿은 뒤 남은 입자가 흩어지도록 두는 시간
        private const double WAVE_FADE_SECONDS = 0.35;

        private enum ESlotMode : byte
        {
            Idle,
            Brief,
            Timed,
            Wave,
            Fading
        }

        private readonly GameObject[]       _objects;
        private readonly Transform[]        _transforms;
        private readonly ParticleSystem[][] _systems;
        private readonly GameObject[][]     _groups;
        private readonly ESlotMode[]        _modes;
        private readonly double[]           _expireAt;
        private readonly Vector3[]          _waveOrigin;
        private readonly Vector3[]          _waveDirection;
        private readonly double[]           _waveStartAt;
        private readonly float[]            _waveSpeed;
        private readonly float[]            _waveRange;
        private int                         _next;

        /****************************************
        *              Properties
        ****************************************/

        public bool HasPrefab { get; }

        public int SlotCount => (_objects != null) ? _objects.Length : 0;

        /// <summary> 효과 하나를 이루는 묶음 수, 기본 공격 콤보처럼 묶음 하나만 골라 재생할 때 쓴다 </summary>
        public int GroupCount => ((_groups != null) && (_groups.Length > 0)) ? _groups[0].Length : 0;

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

        public MageknightVfxPool(GameObject prefab, Transform root, int size)
        {
            HasPrefab = (prefab != null) && (size > 0);
            if (!HasPrefab)
            {
                return;
            }

            _objects       = new GameObject[size];
            _transforms    = new Transform[size];
            _systems       = new ParticleSystem[size][];
            _groups        = new GameObject[size][];
            _modes         = new ESlotMode[size];
            _expireAt      = new double[size];
            _waveOrigin    = new Vector3[size];
            _waveDirection = new Vector3[size];
            _waveStartAt   = new double[size];
            _waveSpeed     = new float[size];
            _waveRange     = new float[size];
            for (int i = 0; i < size; ++i)
            {
                GameObject instance = UnityEngine.Object.Instantiate(prefab, root);
                instance.SetActive(false);
                _objects[i]    = instance;
                _transforms[i] = instance.transform;
                _systems[i]    = instance.GetComponentsInChildren<ParticleSystem>(true);
                _groups[i]     = CollectGroups(instance.transform);
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

        /// <summary> 짧은 베기, 입자가 모두 끝나거나 상한 시간이 지나면 정리한다. group이 0 이상이면 그 묶음만 재생한다 </summary>
        public void PlayBrief(Vector3 position, Quaternion rotation, double now, float maxSeconds, int group = -1)
        {
            if (!HasPrefab)
            {
                return;
            }

            int index = Acquire();
            _transforms[index].SetPositionAndRotation(position, rotation);
            SelectGroup(index, group);
            Begin(index, ESlotMode.Brief, now + maxSeconds);
        }

        /// <summary> 재생 중인 모든 칸을 한 위치로 옮긴다, 캐릭터를 따라다니는 방벽에 쓴다 </summary>
        public void MoveActive(Vector3 position)
        {
            for (int i = 0; i < SlotCount; ++i)
            {
                if ((_modes[i] != ESlotMode.Idle) && (_objects[i] != null))
                {
                    _transforms[i].position = position;
                }
            }
        }

        /// <summary> 제자리 지속 효과, 입자 상태와 무관하게 종료 시각까지 유지한다 </summary>
        public void PlayTimed(Vector3 position, Quaternion rotation, double expireAt)
        {
            if (!HasPrefab)
            {
                return;
            }

            int index = Acquire();
            _transforms[index].SetPositionAndRotation(position, rotation);
            SelectGroup(index, -1);
            Begin(index, ESlotMode.Timed, expireAt);
        }

        /// <summary> 서버 출발 시각 기준의 직선 이동 효과, 위치는 속도 곱하기 경과 시간으로만 정하며 사거리에서 멈춘다 </summary>
        public void PlayWave(Vector3 origin, Vector3 direction, double startAt, float speed, float range, double now)
        {
            if ((!HasPrefab) || (speed <= 0f) || (range <= 0f))
            {
                return;
            }

            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                direction = Vector3.forward;
            }

            direction.Normalize();
            int index = Acquire();
            _waveOrigin[index]    = origin;
            _waveDirection[index] = direction;
            _waveStartAt[index]   = startAt;
            _waveSpeed[index]     = speed;
            _waveRange[index]     = range;
            _transforms[index].SetPositionAndRotation(origin + (direction * MageknightVfxTracker.WaveDistance(speed, range, now - startAt)),
                Quaternion.LookRotation(direction));
            Begin(index, ESlotMode.Wave, 0.0);
        }

        /// <summary> 만료, 입자 종료, 검기 이동과 사거리 도달을 처리한다 </summary>
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
                    case ESlotMode.Timed:
                    case ESlotMode.Fading:
                        if (now >= _expireAt[i])
                        {
                            StopSlot(i);
                        }

                        break;
                    case ESlotMode.Wave:
                        TickWave(i, now);
                        break;
                    default:
                        break;
                }
            }
        }

        public void StopAll()
        {
            for (int i = 0; i < SlotCount; ++i)
            {
                StopSlot(i);
            }
        }

        private void TickWave(int index, double now)
        {
            double elapsed  = now - _waveStartAt[index];
            float  distance = MageknightVfxTracker.WaveDistance(_waveSpeed[index], _waveRange[index], elapsed);
            _transforms[index].position = _waveOrigin[index] + (_waveDirection[index] * distance);
            if (distance < _waveRange[index])
            {
                return;
            }

            // 서버가 충돌 위치를 알려 주지 않으므로 사거리 끝에서 새 입자만 멈추고 남은 입자를 잠시 둔다
            ParticleSystem[] systems = _systems[index];
            for (int i = 0; i < systems.Length; ++i)
            {
                systems[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }

            _modes[index]    = ESlotMode.Fading;
            _expireAt[index] = now + WAVE_FADE_SECONDS;
        }

        // 생성 사본은 루트 아래 효과 하나를 두고, 그 효과의 자식이 따로 재생할 수 있는 묶음이다
        private static GameObject[] CollectGroups(Transform root)
        {
            if (root.childCount == 0)
            {
                return new GameObject[0];
            }

            Transform effect = root.GetChild(0);
            var groups = new GameObject[effect.childCount];
            for (int i = 0; i < groups.Length; ++i)
            {
                groups[i] = effect.GetChild(i).gameObject;
            }

            return groups;
        }

        // 음수면 모든 묶음, 아니면 group번 묶음 하나만 켠다. 묶음 수를 넘으면 마지막 묶음이다
        private void SelectGroup(int index, int group)
        {
            GameObject[] groups = _groups[index];
            if ((groups == null) || (groups.Length == 0))
            {
                return;
            }

            int selected = (group < 0) ? -1 : Mathf.Min(group, groups.Length - 1);
            for (int i = 0; i < groups.Length; ++i)
            {
                if (groups[i] != null)
                {
                    groups[i].SetActive((selected < 0) || (i == selected));
                }
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
        }
    }
}
