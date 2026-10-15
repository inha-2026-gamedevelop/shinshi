// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 서버 규칙: 속도는 일정하고 발사 뒤 유도하지 않는다
    //   정면 연사(1P P04, 2P P05): 발마다 그때의 보스 위치에서 고정한 한 방향으로 쏜다
    //   전방위 난사(하드 2P P05): 원점을 시전 시작 순간에 고정하고 전방위를 같은 각도의 구역으로 나눠 구역마다 한 발씩 무작위 순서로 쏜다
    // 이 도구는 서버 연결이 없어서 정해 둔 거리 끝을 맞은 지점으로 보고, 도착 시각을 확정 시각으로 삼아 타격을 재생한다
    /// <summary> 패턴 판정 시각마다 월백에서 여우불을 쏘고 도착 지점에 타격을 재생하는 확인용 도구 </summary>
    public sealed class FoxfireProjectileCue : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float FULL_TURN    = 360f;
        private const float MAX_FRACTION = 0.9999f;

        /****************************************
        *              Nested Types
        ****************************************/

        public enum EAimMode
        {
            Forward,
            Radial
        }

        /// <summary> 패턴 하나의 발사 방식 </summary>
        [Serializable]
        public sealed class Cue
        {
            [SerializeField] private string         _label;
            [SerializeField] private EAimMode       _aimMode;
            [SerializeField] private int            _shots      = 1;
            [SerializeField] private float          _distance   = 16f;
            [SerializeField] private HumanBodyBones _originBone = HumanBodyBones.RightHand;

            public string         Label      => _label;
            public EAimMode       AimMode    => _aimMode;
            public int            Shots      => Mathf.Max(1, _shots);
            public float          Distance   => _distance;
            public HumanBodyBones OriginBone => _originBone;
        }

        private sealed class Shot
        {
            public uint    Id;
            public Vector3 Origin;
            public Vector3 End;
            public double  StartTime;
            public float   Duration;
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PatternMotionPreview _pattern;
        [SerializeField] private Animator _animator;
        [SerializeField] private Cue[] _cues;
        [SerializeField] private WolbaekFoxfirePlayer[] _flightPlayers;
        [SerializeField] private WolbaekFoxfirePlayer[] _impactPlayers;
        [SerializeField] private float _speed = 10f;
        [SerializeField] private float _endHeight = 1f;

        private Shot[]  _shots;
        private int[]   _sectorOrder;
        private Vector3 _radialOrigin;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_pattern == null)
            {
                _pattern = GetComponent<PatternMotionPreview>();
            }

            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }

            _shots = new Shot[_flightPlayers.Length];

            for (int i = 0; i < _shots.Length; ++i)
            {
                _shots[i] = new Shot();
            }

            int maxShots = 1;

            for (int i = 0; i < _cues.Length; ++i)
            {
                maxShots = Mathf.Max(maxShots, _cues[i].Shots);
            }

            _sectorOrder = new int[maxShots];
        }

        private void OnEnable()
        {
            _pattern.Struck += OnStruck;
        }

        private void OnDisable()
        {
            _pattern.Struck -= OnStruck;
            StopAll();
        }

        private void Update()
        {
            double now = Time.timeAsDouble;

            for (int i = 0; i < _shots.Length; ++i)
            {
                if (_shots[i].Id != 0)
                {
                    UpdateShot(i, now);
                }
            }

            for (int i = 0; i < _impactPlayers.Length; ++i)
            {
                if (_impactPlayers[i].IsRunning)
                {
                    _impactPlayers[i].Tick(_impactPlayers[i].PlaybackId, now);
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnStruck(string label, int shotIndex)
        {
            Cue cue = FindCue(label);

            if (cue == null)
            {
                return;
            }

            if (cue.AimMode == EAimMode.Radial)
            {
                FireRadial(cue, shotIndex);
                return;
            }

            Vector3 forward = _animator.transform.forward;
            forward.y = 0f;
            Fire(_animator.GetBoneTransform(cue.OriginBone).position, forward.normalized, cue.Distance);
        }

        private Cue FindCue(string label)
        {
            for (int i = 0; i < _cues.Length; ++i)
            {
                if (_cues[i].Label == label)
                {
                    return _cues[i];
                }
            }

            return null;
        }

        // 첫 발에서 원점과 구역 순서를 정하고 이후 발은 그 값을 그대로 쓴다
        private void FireRadial(Cue cue, int shotIndex)
        {
            if (shotIndex == 0)
            {
                _radialOrigin = _animator.GetBoneTransform(cue.OriginBone).position;
                ShuffleSectors(cue.Shots);
            }

            float arc = FULL_TURN / cue.Shots;
            float angle = (_sectorOrder[shotIndex] + (UnityEngine.Random.value * MAX_FRACTION)) * arc;
            float radian = angle * Mathf.Deg2Rad;
            Fire(_radialOrigin, new Vector3(Mathf.Sin(radian), 0f, Mathf.Cos(radian)), cue.Distance);
        }

        private void ShuffleSectors(int count)
        {
            for (int i = 0; i < count; ++i)
            {
                _sectorOrder[i] = i;
            }

            for (int i = count - 1; i > 0; --i)
            {
                int swap = UnityEngine.Random.Range(0, i + 1);
                int temp = _sectorOrder[i];
                _sectorOrder[i] = _sectorOrder[swap];
                _sectorOrder[swap] = temp;
            }
        }

        private void Fire(Vector3 origin, Vector3 direction, float distance)
        {
            int slot = FindIdleFlight();

            if (slot < 0)
            {
                return;
            }

            Shot shot = _shots[slot];
            shot.Origin = origin;
            shot.End = origin + (direction * distance);
            shot.End.y = _endHeight;
            shot.Duration = Vector3.Distance(shot.Origin, shot.End) / _speed;
            shot.StartTime = Time.timeAsDouble;
            shot.Id = _flightPlayers[slot].PlayFlight(shot.Origin, Quaternion.LookRotation(shot.End - shot.Origin), shot.StartTime, shot.StartTime);
        }

        private void UpdateShot(int slot, double now)
        {
            Shot shot = _shots[slot];
            WolbaekFoxfirePlayer player = _flightPlayers[slot];
            float progress = (float)((now - shot.StartTime) / shot.Duration);

            if (progress >= 1f)
            {
                player.Stop(shot.Id);
                shot.Id = 0;
                PlayImpact(shot.End, now);
                return;
            }

            player.SetFlightPose(shot.Id, Vector3.Lerp(shot.Origin, shot.End, progress), Quaternion.LookRotation(shot.End - shot.Origin));
            player.Tick(shot.Id, now);
        }

        // 타격 원점은 지면 원의 중심이라 도착 지점의 지면 위치를 쓴다
        private void PlayImpact(Vector3 hitPoint, double now)
        {
            for (int i = 0; i < _impactPlayers.Length; ++i)
            {
                if (!_impactPlayers[i].IsRunning)
                {
                    _impactPlayers[i].PlayImpact(new Vector3(hitPoint.x, 0f, hitPoint.z), EWolbaekFoxfireVariant.Blue, now, now);
                    return;
                }
            }
        }

        private int FindIdleFlight()
        {
            for (int i = 0; i < _shots.Length; ++i)
            {
                if ((_shots[i].Id == 0) && (!_flightPlayers[i].IsRunning))
                {
                    return i;
                }
            }

            return -1;
        }

        private void StopAll()
        {
            for (int i = 0; i < _flightPlayers.Length; ++i)
            {
                _flightPlayers[i].Stop();
                _shots[i].Id = 0;
            }

            for (int i = 0; i < _impactPlayers.Length; ++i)
            {
                _impactPlayers[i].Stop();
            }
        }
    }
}
