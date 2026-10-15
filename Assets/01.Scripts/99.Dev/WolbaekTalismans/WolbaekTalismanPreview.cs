#if UNITY_EDITOR || DEVELOPMENT_BUILD
// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Dev
{
    /// <summary> 서버 연동 전의 부적 상태를 명시적인 개발용 큐로 반복 검수한다 </summary>
    public sealed class WolbaekTalismanPreview : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int SIGN_COUNT            = 4;
        private const float LOOP_SECONDS        = 16f;
        private const float FIRST_HOLD_START     = 2f;
        private const float RESET_START          = 4f;
        private const float SECOND_HOLD_START    = 5f;
        private const float SUCCESS_START        = 10f;
        private const float EXPIRED_START        = 14f;
        private const float FIRST_HOLD_PROGRESS  = 0.4f;

        [Header("개발용 상태 큐")]
        [SerializeField] private WolbaekTalismanPlayer[] _players = null;
        [SerializeField] private Camera _camera                  = null;
        [SerializeField, Min(0.01f)] private float _radius        = 3f;

        private readonly uint[] _ids = new uint[SIGN_COUNT];
        private readonly Color[] _colors = new Color[SIGN_COUNT];
        private bool _manual         = false;
        private bool _running        = false;
        private double _started      = 0d;
        private float _sampled       = -1f;
        private int _cycle           = -1;
        private ulong _sequence      = 0;

        /****************************************
        *              Properties
        ****************************************/

        public WolbaekTalismanPlayer[] Players => _players;
        public bool Running => _running;
        public bool Manual => _manual;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            if (_running)
            {
                return;
            }

            // 개발 씬을 수동 구성해도 카메라 탐색은 시작 시 한 번만 수행한다
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            Begin(false);
        }

        private void Update()
        {
            if ((!_running) || (_manual))
            {
                return;
            }

            Sample((float)(Time.unscaledTimeAsDouble - _started));
        }

        private void OnDisable()
        {
            Stop();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 수동 캡처 또는 자동 반복 검수를 처음부터 시작한다 </summary>
        public void Begin(bool manual)
        {
            ValidateReferences();
            Stop();
            _manual   = manual;
            _running  = true;
            _started  = Time.unscaledTimeAsDouble;
            _sampled  = -1f;
            _cycle    = -1;
            _sequence = 0;
            for (int i = 0; i < SIGN_COUNT; ++i)
            {
                _colors[i] = _players[i].Settings.DefaultColor;
            }
            Sample(0f);
        }

        /// <summary> 시작 이후 단조 증가한 초를 넣는다. 이전 시각 캡처는 Begin으로 초기화한다 </summary>
        public void Sample(float seconds)
        {
            if (!_running)
            {
                return;
            }

            if ((float.IsNaN(seconds)) || (float.IsInfinity(seconds)) || (seconds < 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(seconds), "검수 시각은 유한한 0 이상의 초여야 합니다.");
            }

            if (seconds < _sampled)
            {
                throw new InvalidOperationException("이전 시각 검수 전에 Begin으로 개발용 큐를 초기화하세요.");
            }

            int cycle = (int)Math.Floor(seconds / LOOP_SECONDS);
            float time = seconds - cycle * LOOP_SECONDS;
            bool begin = cycle != _cycle;
            if (begin)
            {
                _cycle    = cycle;
                _sequence = 0;
            }

            ++_sequence;
            _sampled = seconds;
            if (time >= EXPIRED_START)
            {
                for (int i = 0; i < SIGN_COUNT; ++i)
                {
                    _players[i].Stop();
                }
                return;
            }
            GetDevelopmentCue(time, out WolbaekTalismanPlayer.EPhase phase,
                out float phaseStartedAt, out float progress);
            Vector3 viewer = _camera == null ? Vector3.up : _camera.transform.position;
            for (int i = 0; i < SIGN_COUNT; ++i)
            {
                WolbaekTalismanPlayer player = _players[i];
                WolbaekTalismanPlayer.Snapshot snapshot = new WolbaekTalismanPlayer.Snapshot(
                    (ulong)cycle + 1, _sequence, (EWolbaekTalismanSign)i, _colors[i],
                    player.transform.position, _radius, 0d, time, phase, phaseStartedAt, progress);
                if (begin)
                {
                    _ids[i] = player.Begin(snapshot, time);
                }
                else
                {
                    player.Reconcile(_ids[i], snapshot, time);
                }

                player.Tick(_ids[i], time);
                player.FaceGlyph(_ids[i], viewer);
            }
        }

        /// <summary> 개발용 큐와 각 부적의 표현을 정리한다 </summary>
        public void Stop()
        {
            _running = false;
            if (_players == null)
            {
                return;
            }

            for (int i = 0; i < _players.Length; ++i)
            {
                if (_players[i] != null)
                {
                    _players[i].Stop();
                }
            }
        }

        private void ValidateReferences()
        {
            if ((_players == null) || (_players.Length != SIGN_COUNT))
            {
                throw new InvalidOperationException("부적 프리뷰는 4개의 부적과 개발용 색 참조가 필요합니다.");
            }

            if ((float.IsNaN(_radius)) || (float.IsInfinity(_radius)) || (_radius <= 0f))
            {
                throw new InvalidOperationException("개발용 점유 반경은 유한한 양수여야 합니다.");
            }

            for (int i = 0; i < SIGN_COUNT; ++i)
            {
                if ((_players[i] == null) || (_players[i].Settings == null))
                {
                    throw new InvalidOperationException("개발용 부적 참조가 비어 있습니다: " + i);
                }
            }
        }

        private static void GetDevelopmentCue(float time, out WolbaekTalismanPlayer.EPhase phase,
            out float phaseStartedAt, out float progress)
        {
            // 실제 성공 판정은 서버 스냅샷이다. 이 시간표는 검수 씬에서만 성공 큐를 만든다.
            if (time >= EXPIRED_START)
            {
                phase          = WolbaekTalismanPlayer.EPhase.Expired;
                phaseStartedAt = EXPIRED_START;
                progress       = 0f;
                return;
            }

            if (time >= SUCCESS_START)
            {
                phase          = WolbaekTalismanPlayer.EPhase.Success;
                phaseStartedAt = SUCCESS_START;
                progress       = 1f;
                return;
            }

            if ((time >= RESET_START) && (time < SECOND_HOLD_START))
            {
                phase          = WolbaekTalismanPlayer.EPhase.Reset;
                phaseStartedAt = RESET_START;
                progress       = 0f;
                return;
            }

            phase = WolbaekTalismanPlayer.EPhase.Holding;
            if (time >= SECOND_HOLD_START)
            {
                phaseStartedAt = SECOND_HOLD_START;
                progress = Mathf.Clamp01((time - SECOND_HOLD_START) / (SUCCESS_START - SECOND_HOLD_START));
                return;
            }

            phaseStartedAt = time < FIRST_HOLD_START ? 0f : FIRST_HOLD_START;
            progress = Mathf.Clamp01((time - FIRST_HOLD_START) / (RESET_START - FIRST_HOLD_START)) * FIRST_HOLD_PROGRESS;
        }
    }
}
#endif
