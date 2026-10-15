#if UNITY_EDITOR || DEVELOPMENT_BUILD
// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Dev
{
    /// <summary> 전용 검수 씬에서만 다섯 여우불 상태를 병렬로 보여준다 </summary>
    public sealed class WolbaekFoxfirePreviewBoard : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int CELL_COUNT = 5;
        private const int IMPACT_CELL = 2;
        private const float MIN_DURATION = 0.001f;
        private const float PREVIEW_FLIGHT_HEIGHT = 0.9f;
        private const float PREVIEW_FLIGHT_ANGLE = 50f;
        private const double PREVIEW_WARNING_END = 10000d;
        private const float CURVE_RADIUS = 1.15f;
        private const float CURVE_DEPTH = 0.72f;
        private const float CURVE_RADIANS_PER_SECOND = 2.2f;

        [SerializeField] private WolbaekFoxfirePlayer[] _players = null;
        [SerializeField] private bool _playOnStart = true;
        [SerializeField, Min(0.001f)] private float _travelSeconds = 2.5f;
        [SerializeField, Min(0f)] private float _travelDistance = 1.4f;

        private Vector3[] _positions;
        private uint[] _ids;
        private Tweener _flightTween;
        private bool _running;
        private bool _manual;
        private double _started;
        private int _impactCycle;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if ((_players == null) || (_players.Length != CELL_COUNT))
            {
                enabled = false;
                return;
            }

            _positions = new Vector3[CELL_COUNT];
            _ids = new uint[CELL_COUNT];
            for (int i = 0; i < CELL_COUNT; ++i)
            {
                _positions[i] = _players[i].transform.position;
            }

            _positions[0].y = PREVIEW_FLIGHT_HEIGHT;
            Vector3 end = _positions[0] + Quaternion.Euler(0f, PREVIEW_FLIGHT_ANGLE, 0f)
                * Vector3.forward * _travelDistance;
            _flightTween = _players[0].transform.DOMove(end, _travelSeconds)
                .SetAutoKill(false).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).Pause();
        }

        private void Start()
        {
            if (_playOnStart)
            {
                Begin(false);
            }
        }

        private void Update()
        {
            if ((_running) && (!_manual))
            {
                Sample((float)(Time.unscaledTimeAsDouble - _started));
            }
        }

        private void OnDisable() { Stop(); }
        private void OnDestroy() { _flightTween?.Kill(); }

        /****************************************
        *                Methods
        ****************************************/

        public void Begin(bool manual)
        {
            if ((!Application.isPlaying) || (_positions == null))
            {
                return;
            }

            Stop();
            enabled = true;
            _manual = manual;
            _running = true;
            _started = Time.unscaledTimeAsDouble;
            _impactCycle = -1;
            _ids[0] = _players[0].PlayFlight(_positions[0], Quaternion.Euler(0f, PREVIEW_FLIGHT_ANGLE, 0f), 0d, 0d);
            _flightTween.ChangeStartValue(_positions[0]);
            for (int i = 1; i < CELL_COUNT; ++i)
            {
                if (i == IMPACT_CELL)
                {
                    continue;
                }

                EWolbaekFoxfireVariant variant = i == 3 ? EWolbaekFoxfireVariant.GoldTarget
                    : i == 4 ? EWolbaekFoxfireVariant.RedHazard : EWolbaekFoxfireVariant.Blue;
                _ids[i] = _players[i].BeginTelegraph(_positions[i], variant, 0d, PREVIEW_WARNING_END, 0d);
            }

            Sample(0f);
        }

        public void Sample(float seconds)
        {
            if ((!_running) || (seconds < 0f))
            {
                return;
            }

            // 미리보기만 반복한다. 실전 프리팹에는 이 드라이버가 들어 있지 않다.
            if (!_players[0].IsRunning)
            {
                _ids[0] = _players[0].PlayFlight(_positions[0], Quaternion.Euler(0f, PREVIEW_FLIGHT_ANGLE, 0f), seconds, seconds);
            }

            float phase = seconds * CURVE_RADIANS_PER_SECOND;
            Vector3 position = _positions[0] + new Vector3(Mathf.Sin(phase) * CURVE_RADIUS,
                Mathf.Sin(phase * 0.63f) * 0.12f, (1f - Mathf.Cos(phase)) * CURVE_DEPTH);
            Vector3 tangent = new Vector3(Mathf.Cos(phase) * CURVE_RADIUS,
                Mathf.Cos(phase * 0.63f) * 0.0756f, Mathf.Sin(phase) * CURVE_DEPTH);
            _players[0].SetFlightPose(_ids[0], position, Quaternion.LookRotation(tangent, Vector3.up));
            for (int i = 0; i < CELL_COUNT; ++i)
            {
                if (i == IMPACT_CELL)
                {
                    WolbaekFoxfireSettingsSO settings = _players[i].Settings;
                    float duration = Mathf.Max(MIN_DURATION, settings.ExpansionSeconds + settings.HoldSeconds + settings.FadeSeconds);
                    int cycle = (int)(seconds / duration);
                    if (cycle != _impactCycle)
                    {
                        double hit = cycle * duration;
                        _ids[i] = _players[i].PlayImpact(_positions[i], EWolbaekFoxfireVariant.Blue, hit, hit);
                        _impactCycle = cycle;
                    }
                }

                _players[i].Tick(_ids[i], seconds);
            }
        }

        public void Stop()
        {
            _running = false;
            _flightTween?.Pause();
            if ((_players != null) && (_ids != null))
            {
                for (int i = 0; i < _players.Length; ++i)
                {
                    if (_players[i] != null)
                    {
                        _players[i].Stop(_ids[i]);
                    }
                }
            }

            // 유휴 상태에는 매 프레임 샘플링하지 않는다.
            enabled = false;
        }
    }
}
#endif
