#if UNITY_EDITOR || DEVELOPMENT_BUILD
// System
using System;

// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Dev
{
    /// <summary> 외부 확정 위치와 연결 상태를 개발용 시간표로 공급한다. 실제 서버 판정을 수행하지 않는다 </summary>
    public sealed class WolbaekWardPreview : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int SIGN_COUNT             = 2;
        private const int PLAYER_COUNT           = 4;
        private const float LOOP_SECONDS         = 20f;
        private const float ACTIVE_START         = 2f;
        private const float WARNING_START        = 4f;
        private const float DANGER_START         = 6f;
        private const float HIDDEN_START         = 8f;
        private const float RECOVER_START        = 10f;
        private const float NEW_STAGE_START      = 12f;
        private const float NEW_STAGE_ACTIVE     = 14f;
        private const float EXPIRED_START        = 17f;
        private const float NORMAL_DISTANCE      = 2.4f;
        private const float WARNING_DISTANCE     = 3.6f;
        private const float DANGER_DISTANCE      = 4.8f;
        private const float MOVE_SECONDS         = 0.7f;
        private const float HALF                 = 0.5f;
        private const float PAIR_ROTATION        = 0.38f;
        private const float PAIR_SWAY            = 0.08f;
        private const float PAIR_SWAY_FREQUENCY  = 0.65f;
        private const float SHIFT_DISTANCE       = 3f;
        private const float WARNING_AMOUNT       = 0.65f;
        private const double CLOCK_GAP           = 0.001d;

        [Header("외부 상태를 대신하는 개발용 큐")]
        [SerializeField] private WolbaekWardPlayer[] _wards = null;
        [SerializeField] private WolbaekLinkPlayer[] _links = null;
        [SerializeField] private Transform[] _players       = null;
        [SerializeField] private Camera _camera             = null;
        [SerializeField, Min(0.001f)] private float _radius  = 3f;

        private readonly uint[] _wardIds        = new uint[SIGN_COUNT];
        private readonly uint[] _linkIds        = new uint[SIGN_COUNT];
        private readonly Vector3[] _centers     = new Vector3[SIGN_COUNT];
        private readonly Color[] _wardColors   = new Color[SIGN_COUNT];
        private readonly Color[] _linkColors   = new Color[SIGN_COUNT];
        private readonly EWolbaekWardSign[] _wardSigns = new EWolbaekWardSign[SIGN_COUNT];
        private readonly EWolbaekWardSign[] _linkSigns = new EWolbaekWardSign[SIGN_COUNT];
        private bool _manual          = false;
        private bool _running         = false;
        private bool _centersCached   = false;
        private double _started       = 0d;
        private double _clockOrigin   = 0d;
        private double _lastClock     = 0d;
        private float _sampled        = -1f;
        private int _cycle            = -1;
        private uint _stage           = 0;
        private ulong _castId         = 0;
        private ulong _sequence       = 0;

        /****************************************
        *              Properties
        ****************************************/

        public WolbaekWardPlayer[] Wards => _wards;
        public WolbaekLinkPlayer[] Links => _links;
        public Transform[] Players => _players;
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
            if (_camera == null)
            {
                // 수동으로 만든 검수 씬의 카메라 참조도 시작 시 한 번만 붙잡는다
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

        /// <summary> 수동 캡처는 Begin(true) 후 Sample에 시작 이후 초를 넣는다 </summary>
        public void Begin(bool manual)
        {
            ValidateReferences();
            Stop();
            if (!_centersCached)
            {
                for (int i = 0; i < SIGN_COUNT; ++i)
                {
                    _centers[i] = _wards[i].transform.position;
                }
                _centersCached = true;
            }
            _manual      = manual;
            _running     = true;
            _started     = Time.unscaledTimeAsDouble;
            _clockOrigin = _lastClock + CLOCK_GAP;
            _sampled     = -1f;
            _cycle       = -1;
            _stage       = 0;
            _sequence    = 0;
            for (int i = 0; i < SIGN_COUNT; ++i)
            {
                _wardColors[i] = _wards[i].Settings.DefaultColor;
                _linkColors[i] = _links[i].Settings.DefaultColor;
                _wardSigns[i] = _wards[i].Settings.DefaultSign;
                _linkSigns[i] = _links[i].Settings.DefaultSign;
            }
            Sample(0f);
        }

        /// <summary> 초는 단조 증가해야 한다. 다시 처음을 캡처할 때는 Begin으로 개발 큐를 초기화한다 </summary>
        public void Sample(float seconds)
        {
            if (!_running)
            {
                return;
            }
            if ((float.IsNaN(seconds)) || (float.IsInfinity(seconds)) || (seconds < 0f)
                || (seconds / LOOP_SECONDS >= int.MaxValue))
            {
                throw new ArgumentOutOfRangeException(nameof(seconds), "검수 시각은 유한한 0 이상의 초여야 합니다.");
            }
            if (seconds < _sampled)
            {
                throw new InvalidOperationException("이전 시각 캡처 전에 Begin으로 개발용 큐를 초기화하세요.");
            }
            int cycle = (int)Math.Floor(seconds / LOOP_SECONDS);
            float time = seconds - cycle * LOOP_SECONDS;
            double cycleStart = _clockOrigin + cycle * (double)LOOP_SECONDS;
            double now = _clockOrigin + seconds;
            bool newCycle = cycle != _cycle;
            uint stage = time >= NEW_STAGE_START ? 1u : 0u;
            bool newStage = newCycle || (stage != _stage);
            if (newCycle)
            {
                _cycle = cycle;
                ++_castId;
                if (_castId == 0)
                {
                    ++_castId;
                }
                _sequence = 0;
            }
            ++_sequence;
            _sampled   = seconds;
            _lastClock = now;
            _stage     = stage;
            GetWardCue(time, out WolbaekWardPlayer.EPhase phase, out float phaseStart);
            GetLinkCue(time, out WolbaekLinkPlayer.ESeverity severity, out float warning, out bool visible);
            float distance = DevelopmentDistance(time);
            for (int i = 0; i < SIGN_COUNT; ++i)
            {
                // 서버 사건을 대신하는 이 개발 큐에서만 위치를 바꾼다. 이전 시각에는 새 위치를 그리지 않는다
                Vector3 center = _centers[i] + (stage == 0 ? Vector3.zero
                    : Vector3.forward * (i == 0 ? SHIFT_DISTANCE : -SHIFT_DISTANCE));
                MovePlayers(i, center, distance, time, visible);
                double appearedAt = cycleStart + (stage == 0 ? 0d : NEW_STAGE_START);
                WolbaekWardPlayer.Snapshot ward = new WolbaekWardPlayer.Snapshot(
                    _castId, _sequence, stage, _wardSigns[i], _wardColors[i], center, _radius,
                    appearedAt, now, phase, cycleStart + phaseStart);
                if (newStage)
                {
                    _wardIds[i] = _wards[i].Begin(ward, now);
                    if (_wardIds[i] == 0)
                    {
                        throw new InvalidOperationException("개발 결계 시작 입력이 거부되었습니다: " + i);
                    }
                }
                else if (_wards[i].IsRunning)
                {
                    if (!_wards[i].Reconcile(_wardIds[i], ward, now))
                    {
                        throw new InvalidOperationException("개발 결계 상태 입력이 거부되었습니다: " + i);
                    }
                }
                _wards[i].Tick(_wardIds[i], now);

                WolbaekLinkPlayer.EPhase linkPhase = time >= EXPIRED_START
                    ? WolbaekLinkPlayer.EPhase.Expired : WolbaekLinkPlayer.EPhase.Active;
                WolbaekLinkPlayer.Snapshot link = new WolbaekLinkPlayer.Snapshot(
                    _castId, _sequence, _linkSigns[i], _linkColors[i], _players[i * SIGN_COUNT].position,
                    _players[i * SIGN_COUNT + 1].position, cycleStart, now, linkPhase,
                    cycleStart + (time >= EXPIRED_START ? EXPIRED_START : 0f), severity, warning, visible);
                if (newCycle)
                {
                    _linkIds[i] = _links[i].Begin(link, now);
                    if (_linkIds[i] == 0)
                    {
                        throw new InvalidOperationException("개발 연결선 시작 입력이 거부되었습니다: " + i);
                    }
                }
                else if (_links[i].IsRunning)
                {
                    if (!_links[i].Reconcile(_linkIds[i], link, now))
                    {
                        throw new InvalidOperationException("개발 연결선 상태 입력이 거부되었습니다: " + i);
                    }
                }
                _links[i].Tick(_linkIds[i], now);
            }
        }

        /// <summary> 개발용 반복과 표현을 정리하고 다음 Begin에서 같은 검수 조건을 복원한다 </summary>
        public void Stop()
        {
            _running = false;
            if (_wards != null)
            {
                foreach (WolbaekWardPlayer ward in _wards)
                {
                    if (ward != null)
                    {
                        ward.Stop();
                    }
                }
            }
            if (_links != null)
            {
                foreach (WolbaekLinkPlayer link in _links)
                {
                    if (link != null)
                    {
                        link.Stop();
                    }
                }
            }
            if (_players != null)
            {
                foreach (Transform player in _players)
                {
                    if (player != null)
                    {
                        player.gameObject.SetActive(true);
                    }
                }
            }
        }

        private void MovePlayers(int pair, Vector3 center, float distance, float time, bool visible)
        {
            float angle = (pair == 0 ? PAIR_ROTATION : -PAIR_ROTATION)
                + Mathf.Sin(time * PAIR_SWAY_FREQUENCY) * PAIR_SWAY;
            Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            for (int i = 0; i < SIGN_COUNT; ++i)
            {
                Transform player = _players[pair * SIGN_COUNT + i];
                player.position = center + direction * distance * (i == 0 ? -HALF : HALF);
                // 참가자 한쪽의 비활성 상태를 보여 주며 연결선 가시성은 별도 외부 입력으로 전달한다
                player.gameObject.SetActive(visible || (i == 0) || (time < ACTIVE_START));
            }
        }

        private static float DevelopmentDistance(float time)
        {
            if ((time >= DANGER_START) && (time < RECOVER_START))
            {
                return DOVirtual.EasedValue(WARNING_DISTANCE, DANGER_DISTANCE,
                    Mathf.Clamp01((time - DANGER_START) / MOVE_SECONDS), Ease.OutSine);
            }
            if ((time >= WARNING_START) && (time < DANGER_START))
            {
                return DOVirtual.EasedValue(NORMAL_DISTANCE, WARNING_DISTANCE,
                    Mathf.Clamp01((time - WARNING_START) / MOVE_SECONDS), Ease.OutSine);
            }
            if ((time >= RECOVER_START) && (time < NEW_STAGE_START))
            {
                return DOVirtual.EasedValue(DANGER_DISTANCE, NORMAL_DISTANCE,
                    Mathf.Clamp01((time - RECOVER_START) / MOVE_SECONDS), Ease.OutSine);
            }
            return NORMAL_DISTANCE;
        }

        private static void GetWardCue(float time, out WolbaekWardPlayer.EPhase phase, out float phaseStart)
        {
            if (time >= EXPIRED_START)
            {
                phase = WolbaekWardPlayer.EPhase.Expired;
                phaseStart = EXPIRED_START;
            }
            else if (time >= NEW_STAGE_ACTIVE)
            {
                phase = WolbaekWardPlayer.EPhase.Active;
                phaseStart = NEW_STAGE_ACTIVE;
            }
            else if (time >= NEW_STAGE_START)
            {
                phase = WolbaekWardPlayer.EPhase.Mark;
                phaseStart = NEW_STAGE_START;
            }
            else if (time >= ACTIVE_START)
            {
                phase = WolbaekWardPlayer.EPhase.Active;
                phaseStart = ACTIVE_START;
            }
            else
            {
                phase = WolbaekWardPlayer.EPhase.Mark;
                phaseStart = 0f;
            }
        }

        private static void GetLinkCue(float time, out WolbaekLinkPlayer.ESeverity severity,
            out float warning, out bool visible)
        {
            visible = (time >= ACTIVE_START) && ((time < HIDDEN_START) || (time >= RECOVER_START));
            if ((time >= DANGER_START) && (time < RECOVER_START))
            {
                severity = WolbaekLinkPlayer.ESeverity.Danger;
                warning = 1f;
            }
            else if ((time >= WARNING_START) && (time < DANGER_START))
            {
                severity = WolbaekLinkPlayer.ESeverity.Warning;
                warning = WARNING_AMOUNT;
            }
            else
            {
                severity = WolbaekLinkPlayer.ESeverity.Normal;
                warning = 0f;
            }
        }

        private void ValidateReferences()
        {
            if ((_wards == null) || (_wards.Length != SIGN_COUNT) || (_links == null)
                || (_links.Length != SIGN_COUNT) || (_players == null) || (_players.Length != PLAYER_COUNT))
            {
                throw new InvalidOperationException("결계 프리뷰는 결계 2개, 연결선 2개, 발밑 앵커 4개가 필요합니다.");
            }
            if ((float.IsNaN(_radius)) || (float.IsInfinity(_radius)) || (_radius <= 0f))
            {
                throw new InvalidOperationException("개발용 반경은 유한한 양수여야 합니다.");
            }
            for (int i = 0; i < SIGN_COUNT; ++i)
            {
                if ((_wards[i] == null) || (_wards[i].Settings == null) || (_links[i] == null)
                    || (_links[i].Settings == null))
                {
                    throw new InvalidOperationException("개발용 결계 또는 연결선 참조가 비어 있습니다: " + i);
                }
            }
            for (int i = 0; i < PLAYER_COUNT; ++i)
            {
                if (_players[i] == null)
                {
                    throw new InvalidOperationException("개발용 발밑 앵커가 비어 있습니다: " + i);
                }
            }
        }
    }
}
#endif
