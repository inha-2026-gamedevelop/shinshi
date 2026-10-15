#if UNITY_EDITOR || DEVELOPMENT_BUILD
// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Dev
{
    /// <summary> 명시된 개발용 입력을 공급하며 거리나 피해로 핵 파괴를 판정하지 않는다 </summary>
    public sealed class WolbaekSealCorePreview : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/
        private const int CORE_COUNT                            = 2;
        private const double PREVIEW_CLOCK_GAP                  = 1d;
        [SerializeField] private WolbaekSealCorePlayer[] _cores = null;
        [SerializeField] private WolbaekSealDropPlayer _drop    = null;
        [SerializeField, Min(0.1f)] private float _blastRadius  = 2f;
        [SerializeField, Min(0.1f)] private float _openAt       = 2f;
        [SerializeField, Min(0.1f)] private float _lockAt       = 5f;
        [SerializeField, Min(0.1f)] private float _resolveAt    = 7f;
        [SerializeField, Min(0.1f)] private float _expireAt     = 9f;
        [SerializeField, Min(0.1f)] private float _loopSeconds  = 12f;
        private readonly uint[] _ids                            = new uint[CORE_COUNT];
        private readonly Vector3[] _centers                     = new Vector3[CORE_COUNT];
        private uint _dropId;
        private ulong _sequence;
        private ulong _cast;
        private double _origin;
        private double _lastNow;
        private double _started;
        private float _sampled;
        private bool _running;
        private bool _manual;
        private bool _cached;

        /****************************************
        *              Properties
        ****************************************/
        public WolbaekSealCorePlayer[] Cores => _cores;
        public WolbaekSealDropPlayer Drop => _drop;

        /****************************************
        *              Unity Event
        ****************************************/
        private void Start()
        {
            if (!_running)
            {
                Begin(false);
            }
        }

        private void Update()
        {
            if ((!_running) || (_manual))
            {
                return;
            }
            float seconds = (float)(Time.unscaledTimeAsDouble - _started);
            if (seconds >= _loopSeconds)
            {
                Begin(false);
                seconds = 0f;
            }
            Sample(seconds);
        }

        private void OnDisable() { Stop(); }

        /****************************************
        *                Methods
        ****************************************/
        /// <summary> 동일 시간축에서 새 개발용 시전을 시작한다 </summary>
        public void Begin(bool manual)
        {
            if (((((_cores == null) || (_cores.Length != CORE_COUNT)) || (_cores[0] == null)) || (_cores[1] == null)) || (_drop == null))
            {
                return;
            }
            Stop();
            if (!_cached)
            {
                _centers[0] = _cores[0].transform.position;
                _centers[1] = _cores[1].transform.position;
                _cached = true;
            }
            _manual = manual;
            _running = true;
            _started = Time.unscaledTimeAsDouble;
            _origin = _lastNow + PREVIEW_CLOCK_GAP;
            _sampled = -1f;
            ++_cast;
            _ids[0] = 0;
            _ids[1] = 0;
            _dropId = 0;
            Sample(0f);
        }

        /// <summary> 지정 시각에 해당하는 외부 상태를 입력한다. 뒤로 이동할 때는 Begin을 먼저 부른다 </summary>
        public void Sample(float seconds)
        {
            if ((((!_running) || (float.IsNaN(seconds))) || (float.IsInfinity(seconds))) || (seconds < _sampled))
            {
                return;
            }
            _sampled = seconds;
            _lastNow = _origin + seconds;
            ++_sequence;
            for (int i = 0; i < CORE_COUNT; ++i)
            {
                WolbaekSealCorePlayer.EPhase phase = seconds < _openAt ? WolbaekSealCorePlayer.EPhase.Dormant
                    : (seconds >= _resolveAt) && (i == 1) ? WolbaekSealCorePlayer.EPhase.Destroyed
                    : seconds >= _expireAt ? WolbaekSealCorePlayer.EPhase.Expired : WolbaekSealCorePlayer.EPhase.Opened;
                double phaseAt = phase == WolbaekSealCorePlayer.EPhase.Dormant ? _origin
                    : phase == WolbaekSealCorePlayer.EPhase.Destroyed ? _origin + _resolveAt
                    : phase == WolbaekSealCorePlayer.EPhase.Expired ? _origin + _expireAt : _origin + _openAt;
                var snapshot = new WolbaekSealCorePlayer.Snapshot(_cast, _sequence, 0, i, _centers[i],
                    _origin, _lastNow, phase, phaseAt);
                if (_ids[i] == 0)
                {
                    _ids[i] = _cores[i].Begin(snapshot, _lastNow);
                }
                else if (_cores[i].IsRunning)
                {
                    _cores[i].Reconcile(_ids[i], snapshot, _lastNow);
                }
            }
            WolbaekSealDropPlayer.EPhase dropPhase = seconds < _lockAt ? WolbaekSealDropPlayer.EPhase.Tracking
                : seconds < _resolveAt ? WolbaekSealDropPlayer.EPhase.Locked : WolbaekSealDropPlayer.EPhase.Resolved;
            double dropPhaseAt = dropPhase == WolbaekSealDropPlayer.EPhase.Tracking ? _origin
                : dropPhase == WolbaekSealDropPlayer.EPhase.Locked ? _origin + _lockAt : _origin + _resolveAt;
            // 개발 큐의 고정 목표다. 핵 근접 여부는 이 코드에서도 계산하지 않는다
            Vector3 target = _centers[1];
            var dropSnapshot = new WolbaekSealDropPlayer.Snapshot(_cast, _sequence, 0, 0, target, _blastRadius,
                _origin, _origin + _lockAt, _origin + _resolveAt, _lastNow, dropPhase, dropPhaseAt);
            if (_dropId == 0)
            {
                _dropId = _drop.Begin(dropSnapshot, _lastNow);
            }
            else if (_drop.IsRunning)
            {
                _drop.Reconcile(_dropId, dropSnapshot, _lastNow);
            }
        }

        /// <summary> 모든 개발 입력과 표현을 정지한다 </summary>
        public void Stop()
        {
            _running = false;
            if (_cores != null)
            {
                foreach (WolbaekSealCorePlayer core in _cores)
                {
                    if (core != null)
                    {
                        core.Stop();
                    }
                }
            }
            if (_drop != null)
            {
                _drop.Stop();
            }
        }
    }
}
#endif
