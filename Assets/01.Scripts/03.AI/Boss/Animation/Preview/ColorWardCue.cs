// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 서버 규칙(HP 50% 색 결계): 공동 결계(반경 5m)에 전원이 8초 연속 모이면 빨강과 파랑 결계로 갈라진다
    //   이후 지점 세 곳에서 탐색 7초 뒤 광역 공격 세 번(1.5초 간격, 판정 1.5초 전 예고)을 하고, 마지막 공격과 같은 시각에 다음 지점이 시작된다
    //   같은 색 두 명은 연결선으로 이어지고 3m 이상이면 경고, 4m 초과면 위험으로 표시한다
    // 이 도구는 서버 알림이 없어서 위 시각표를 패턴 시작 기준 초로 계산해 스냅샷을 공급한다. 참가자 위치는 결계 안 가상 두 명이다
    /// <summary> 공동 결계, 색 결계 세 지점, 연결선, 광역 공격을 시각표대로 재생하는 확인용 도구 </summary>
    public sealed class ColorWardCue : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int   SITE_CANDIDATES = 8;
        private const float SITE_RING       = 11f;
        private const float MAX_TRAVEL      = 15f;
        private const float MIN_SEPARATION  = 8f;
        private const float FULL_TURN       = Mathf.PI * 2f;
        private const int   PICK_TRIES      = 64;
        private const int   RED             = 0;
        private const int   BLUE            = 1;
        private const float WARN_DISTANCE   = 3f;
        private const float DANGER_DISTANCE = 4f;
        private const float CLOSE_GAP       = 2f;
        private const float FAR_GAP         = 5.5f;
        private const int   SLOT_COUNT      = 2;
        private const float HALF            = 0.5f;
        private const float STRIKE_LIFETIME = 5f;

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PatternMotionPreview _pattern;
        [SerializeField] private Transform _origin;
        [SerializeField] private string[] _patternLabels;
        [SerializeField] private GameObject _gatherPrefab;
        [SerializeField] private float _gatherScale = 0.8f;
        [SerializeField] private float _gatherFadeSeconds = 2.5f;
        [SerializeField] private WolbaekWardPlayer[] _redWards;
        [SerializeField] private WolbaekWardPlayer[] _blueWards;
        [SerializeField] private WolbaekLinkPlayer _redLink;
        [SerializeField] private WolbaekLinkPlayer _blueLink;
        [SerializeField] private GameObject _strikePrefab;
        [SerializeField] private float _strikeScale = 6.9f;
        [SerializeField] private float _strikeHeight = 0.1f;
        [SerializeField] private float _gatherSeconds = 8f;
        [SerializeField] private float _wardRadius = 3f;
        [SerializeField] private int _stageCount = 3;
        [SerializeField] private float _searchSeconds = 7f;
        [SerializeField] private int _strikeCount = 3;
        [SerializeField] private float _strikeInterval = 1.5f;
        [SerializeField] private Color _redColor = new Color(1f, 0.25f, 0.3f, 1f);
        [SerializeField] private Color _blueColor = new Color(0.3f, 0.55f, 1f, 1f);

        private ulong   _castId;
        private double  _castStart;
        private bool    _isCasting;
        private ulong   _sequence;
        private int     _stage = -1;
        private int     _nextStrike;
        private Vector3 _redCenter;
        private Vector3 _blueCenter;
        private GameObject _gatherInstance;
        private uint[]  _redPlayback = new uint[SLOT_COUNT];
        private uint[]  _bluePlayback = new uint[SLOT_COUNT];
        private double[] _wardStart = new double[SLOT_COUNT];
        private uint    _redLinkPlayback;
        private uint    _blueLinkPlayback;
        private double  _stageStart;
        private double  _gatherEnd;
        private bool    _gatherEnded;
        private Vector3 _previousRed;
        private Vector3 _previousBlue;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _pattern.Started += OnStarted;
        }

        private void OnDisable()
        {
            _pattern.Started -= OnStarted;
            StopAll();
        }

        private void Update()
        {
            if (!_isCasting)
            {
                return;
            }

            double now = Time.timeAsDouble;
            double elapsed = now - _castStart;
            double stageLength = _searchSeconds + ((_strikeCount - 1) * (double)_strikeInterval);
            double end = _gatherSeconds + (_stageCount * stageLength);

            // 마지막 광역 공격 판정과 같은 시각에 성공하므로 결계와 연결선도 그때 정리한다
            if (elapsed >= end)
            {
                StopAll();
                return;
            }

            if (!_gatherEnded)
            {
                if (elapsed >= _gatherSeconds)
                {
                    EndGather(now);
                }
                else
                {
                    return;
                }
            }

            // 지점의 마지막 판정과 같은 시각에 다음 지점이 시작된다
            int wantedStage = Mathf.Min(_stageCount - 1, (int)((elapsed - _gatherSeconds) / stageLength));
            while (_stage < wantedStage)
            {
                BeginStage(_stage + 1, now);
            }

            TickStrikes(elapsed, stageLength);
            TickWards(now);
            TickLinks(now);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnStarted(string label, float strikeSeconds)
        {
            StopAll();

            if (Array.IndexOf(_patternLabels, label) < 0)
            {
                return;
            }

            double now = Time.timeAsDouble;
            ++_castId;
            _castStart = now;
            _sequence = 1;
            _stage = -1;
            _nextStrike = 0;
            _gatherEnded = false;
            _previousRed = _origin.position;
            _previousBlue = _origin.position;
            SpawnGather();
            _isCasting = true;
        }

        // 돔 효과는 수명이 짧아 집결 동안 이어지도록 모든 파티클을 반복으로 바꾼다
        private void SpawnGather()
        {
            _gatherInstance = Instantiate(_gatherPrefab, _origin.position, Quaternion.identity);
            _gatherInstance.transform.localScale = _gatherPrefab.transform.localScale * _gatherScale;

            foreach (ParticleSystem system in _gatherInstance.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                main.loop = true;
            }
        }

        private void FadeGather()
        {
            if (_gatherInstance == null)
            {
                return;
            }

            foreach (ParticleSystem system in _gatherInstance.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }

            Destroy(_gatherInstance, _gatherFadeSeconds);
            _gatherInstance = null;
        }

        private void EndGather(double now)
        {
            _gatherEnded = true;
            // 공동 결계는 색 결계가 나타나는 순간 새 파티클을 멈추고 지운다
            FadeGather();
        }

        private void BeginStage(int index, double now)
        {
            // 이전 지점 결계는 사라지는 중이므로 쓰던 슬롯을 번갈아 쓴다
            int slot = index % SLOT_COUNT;
            ExpireWards(1 - slot, now);
            PickSites();

            _stage = index;
            _stageStart = _castStart + _gatherSeconds + (index * (_searchSeconds + ((_strikeCount - 1) * (double)_strikeInterval)));
            _nextStrike = 0;
            _wardStart[slot] = now;
            ++_sequence;

            _redPlayback[slot] = _redWards[slot].Begin(WardSnapshot(RED, index, now, WolbaekWardPlayer.EPhase.Active), now);
            _bluePlayback[slot] = _blueWards[slot].Begin(WardSnapshot(BLUE, index, now, WolbaekWardPlayer.EPhase.Active), now);
            _redLinkPlayback = _redLink.Begin(LinkSnapshot(RED, now, true), now);
            _blueLinkPlayback = _blueLink.Begin(LinkSnapshot(BLUE, now, true), now);
            _previousRed = _redCenter;
            _previousBlue = _blueCenter;
        }

        private WolbaekWardPlayer.Snapshot WardSnapshot(int color, int index, double now, WolbaekWardPlayer.EPhase phase)
        {
            bool red = color == RED;
            return new WolbaekWardPlayer.Snapshot(_castId, _sequence, (uint)index,
                red ? EWolbaekWardSign.Diamond : EWolbaekWardSign.Moon, red ? _redColor : _blueColor,
                red ? _redCenter : _blueCenter, _wardRadius, _wardStart[index % SLOT_COUNT], now, phase,
                (phase == WolbaekWardPlayer.EPhase.Expired) ? now : _wardStart[index % SLOT_COUNT]);
        }

        // 지점 후보 여덟 곳 중 이동 거리와 두 색 최소 거리 제약을 만족하는 쌍을 고른다
        private void PickSites()
        {
            Vector3 center = _origin.position;
            for (int i = 0; i < PICK_TRIES; ++i)
            {
                Vector3 red = Candidate(center, UnityEngine.Random.Range(0, SITE_CANDIDATES));
                Vector3 blue = Candidate(center, UnityEngine.Random.Range(0, SITE_CANDIDATES));

                if ((Vector3.Distance(red, _previousRed) <= MAX_TRAVEL) && (Vector3.Distance(blue, _previousBlue) <= MAX_TRAVEL)
                    && (Vector3.Distance(red, blue) >= MIN_SEPARATION) && ((_stage < 0) || (red != _redCenter)))
                {
                    _redCenter = red;
                    _blueCenter = blue;
                    return;
                }
            }
        }

        private static Vector3 Candidate(Vector3 center, int index)
        {
            float angle = (index / (float)SITE_CANDIDATES) * FULL_TURN;
            return center + new Vector3(Mathf.Cos(angle) * SITE_RING, 0f, Mathf.Sin(angle) * SITE_RING);
        }

        private void ExpireWards(int slot, double now)
        {
            if (_redPlayback[slot] == 0)
            {
                return;
            }

            // 필드에는 항상 빨강과 파랑 결계가 하나씩만 있어야 하므로 이전 지점은 감쇠 없이 즉시 끈다
            _redWards[slot].Stop();
            _blueWards[slot].Stop();
            _redPlayback[slot] = 0;
            _bluePlayback[slot] = 0;
        }

        // 광역 공격 판정 시각마다 월백 중심에 공격 효과를 낸다
        private void TickStrikes(double elapsed, double stageLength)
        {
            if (_stage < 0)
            {
                return;
            }

            double stageElapsed = elapsed - (_stageStart - _castStart);
            while ((_nextStrike < _strikeCount) && (stageElapsed >= _searchSeconds + (_nextStrike * (double)_strikeInterval)))
            {
                SpawnStrike();
                ++_nextStrike;
            }
        }

        private void SpawnStrike()
        {
            if (_strikePrefab == null)
            {
                return;
            }

            Vector3 position = _origin.position + (Vector3.up * _strikeHeight);
            GameObject instance = Instantiate(_strikePrefab, position, Quaternion.identity);
            instance.transform.localScale = _strikePrefab.transform.localScale * _strikeScale;
            Destroy(instance, STRIKE_LIFETIME);
        }

        private void TickWards(double now)
        {
            for (int slot = 0; slot < SLOT_COUNT; ++slot)
            {
                if (_redPlayback[slot] != 0)
                {
                    _redWards[slot].Tick(_redPlayback[slot], now);
                    _blueWards[slot].Tick(_bluePlayback[slot], now);
                }
            }
        }

        // 같은 색 두 명이 결계 안에 서 있고, 두 번째 지점에서는 빨강 쌍이 서서히 벌어지게 해 경고와 위험 표시를 보여 준다
        private void GetFeet(int color, double now, out Vector3 first, out Vector3 second, out WolbaekLinkPlayer.ESeverity severity, out float warning)
        {
            Vector3 center = (color == RED) ? _redCenter : _blueCenter;
            float gap = CLOSE_GAP;

            if ((_stage == 1) && (color == RED))
            {
                float t = Mathf.Clamp01((float)((now - _stageStart) / _searchSeconds));
                gap = Mathf.Lerp(CLOSE_GAP, FAR_GAP, t);
            }

            first = center - (Vector3.right * (gap * HALF));
            second = center + (Vector3.right * (gap * HALF));
            severity = WolbaekLinkPlayer.ESeverity.Normal;
            warning = 0f;

            if (gap > DANGER_DISTANCE)
            {
                severity = WolbaekLinkPlayer.ESeverity.Danger;
                warning = 1f;
            }
            else if (gap >= WARN_DISTANCE)
            {
                severity = WolbaekLinkPlayer.ESeverity.Warning;
                warning = Mathf.Clamp01((gap - WARN_DISTANCE) / (DANGER_DISTANCE - WARN_DISTANCE));
            }
        }

        private WolbaekLinkPlayer.Snapshot LinkSnapshot(int color, double now, bool visible)
        {
            GetFeet(color, now, out Vector3 first, out Vector3 second, out WolbaekLinkPlayer.ESeverity severity, out float warning);
            bool red = color == RED;
            return new WolbaekLinkPlayer.Snapshot(_castId, _sequence, red ? EWolbaekWardSign.Diamond : EWolbaekWardSign.Moon,
                red ? _redColor : _blueColor, first, second, _castStart, now, WolbaekLinkPlayer.EPhase.Active, _stageStart,
                severity, warning, visible);
        }

        private void TickLinks(double now)
        {
            ++_sequence;
            _redLink.Reconcile(_redLinkPlayback, LinkSnapshot(RED, now, true), now);
            _blueLink.Reconcile(_blueLinkPlayback, LinkSnapshot(BLUE, now, true), now);
            _redLink.Tick(_redLinkPlayback, now);
            _blueLink.Tick(_blueLinkPlayback, now);
        }

        private void StopAll()
        {
            _isCasting = false;

            if (_gatherInstance != null)
            {
                Destroy(_gatherInstance);
                _gatherInstance = null;
            }

            _redLink.Stop();
            _blueLink.Stop();

            for (int i = 0; i < SLOT_COUNT; ++i)
            {
                _redWards[i].Stop();
                _blueWards[i].Stop();
                _redPlayback[i] = 0;
                _bluePlayback[i] = 0;
            }
        }
    }
}
