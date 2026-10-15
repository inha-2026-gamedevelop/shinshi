// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 서버 규칙(HP 75% 사방 부적진): 전장 중심에서 동, 북, 서, 남 고정 지점에 구조물을 두고 색은 네 가지를 무작위로 섞어 배치한다
    //   색은 빨강, 파랑, 노랑, 초록 순서(서버 값 0부터 3)이고 아트 문양은 꽃, 달, 별, 마름모에 대응한다
    //   참가자 전원이 각자 구조물 안에 동시에 머무는 공용 유지 상태가 5초 이어지면 성공하고, 한 명이라도 벗어나면 진행도가 0으로 초기화된다
    // 이 도구는 서버 알림이 없어서 등장, 유지 시작, 초기화, 재유지, 성공 시각을 패턴 시작 기준 초로 직접 정해 스냅샷을 공급한다
    /// <summary> 사방 부적진의 등장, 공용 유지 진행, 초기화, 성공을 시각표대로 재생하는 확인용 도구 </summary>
    public sealed class TalismanFormationCue : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int   SERVER_COLOR_COUNT = 4;
        private const float QUARTER_TURN       = 90f;

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PatternMotionPreview _pattern;
        [SerializeField] private Transform _origin;
        [SerializeField] private WolbaekTalismanPlayer[] _players;
        [SerializeField] private string[] _patternLabels;
        [SerializeField] private int _structureCount = 4;
        [SerializeField] private float _distance = 10f;
        [SerializeField] private float _radius = 3f;
        [SerializeField] private float _firstHoldSeconds = 3f;
        [SerializeField] private float _resetSeconds = 5f;
        [SerializeField] private float _secondHoldSeconds = 6.5f;
        [SerializeField] private float _holdDuration = 5f;
        [SerializeField] private float _endSeconds = 20f;
        [SerializeField] private Color[] _serverColors =
        {
            new Color(1f, 0.25f, 0.3f, 1f),
            new Color(0.3f, 0.55f, 1f, 1f),
            new Color(1f, 0.85f, 0.25f, 1f),
            new Color(0.3f, 0.9f, 0.5f, 1f)
        };

        private ulong  _castId;
        private double _castStart;
        private bool   _isCasting;
        private bool   _didSucceed;
        private ulong  _sequence;
        private uint[] _playbackIds;
        private int[]  _colorOrder;
        private int[]  _signOrder;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _playbackIds = new uint[SERVER_COLOR_COUNT];
            _colorOrder = new int[SERVER_COLOR_COUNT];
            _signOrder = new int[SERVER_COLOR_COUNT];
        }

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

            if (elapsed >= _endSeconds)
            {
                StopAll();
                return;
            }

            Reconcile(elapsed, now);

            for (int i = 0; i < _structureCount; ++i)
            {
                _players[_signOrder[i]].Tick(_playbackIds[i], now);
                _players[_signOrder[i]].FaceGlyph(_playbackIds[i], Camera.main.transform.position);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnStarted(string label, float strikeSeconds)
        {
            // 다른 패턴이 시작되면 진행 중이던 부적진을 끝낸다
            StopAll();

            if (Array.IndexOf(_patternLabels, label) < 0)
            {
                return;
            }

            double now = Time.timeAsDouble;
            ++_castId;
            _castStart = now;
            _sequence = 1;
            _didSucceed = false;
            ShuffleColors();

            for (int i = 0; i < _structureCount; ++i)
            {
                _playbackIds[i] = _players[_signOrder[i]].Begin(BuildSnapshot(i, WolbaekTalismanPlayer.EPhase.Holding, now, now, now, 0f), now);
            }

            _isCasting = true;
        }

        // 시각표에 맞춰 유지 진행, 초기화, 재유지, 성공 스냅샷을 순서대로 공급한다
        private void Reconcile(double elapsed, double now)
        {
            if (_didSucceed)
            {
                return;
            }

            double holdStart;
            WolbaekTalismanPlayer.EPhase phase;
            float progress;

            if (elapsed < _firstHoldSeconds)
            {
                holdStart = _castStart;
                phase = WolbaekTalismanPlayer.EPhase.Holding;
                progress = 0f;
            }
            else if (elapsed < _resetSeconds)
            {
                holdStart = _castStart + _firstHoldSeconds;
                phase = WolbaekTalismanPlayer.EPhase.Holding;
                progress = Mathf.Clamp01((float)((now - holdStart) / _holdDuration));
            }
            else if (elapsed < _secondHoldSeconds)
            {
                holdStart = _castStart + _resetSeconds;
                phase = WolbaekTalismanPlayer.EPhase.Reset;
                progress = 0f;
            }
            else if (elapsed < _secondHoldSeconds + _holdDuration)
            {
                holdStart = _castStart + _secondHoldSeconds;
                phase = WolbaekTalismanPlayer.EPhase.Holding;
                progress = Mathf.Clamp01((float)((now - holdStart) / _holdDuration));
            }
            else
            {
                _didSucceed = true;
                holdStart = _castStart + _secondHoldSeconds + _holdDuration;
                phase = WolbaekTalismanPlayer.EPhase.Success;
                progress = 1f;
            }

            ++_sequence;

            for (int i = 0; i < _structureCount; ++i)
            {
                _players[_signOrder[i]].Reconcile(_playbackIds[i], BuildSnapshot(i, phase, holdStart, now, _castStart, progress), now);
            }
        }

        private WolbaekTalismanPlayer.Snapshot BuildSnapshot(int structure, WolbaekTalismanPlayer.EPhase phase,
            double phaseStartedAt, double now, double appearedAt, float progress)
        {
            // 동, 북, 서, 남 순서로 +X에서 시작해 반시계 방향으로 90도씩 놓는다
            float angle = structure * QUARTER_TURN * Mathf.Deg2Rad;
            Vector3 center = _origin.position + (new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _distance);

            // 서버 색 순서 0부터 3(빨강, 파랑, 노랑, 초록)을 아트 문양 꽃, 달, 별, 마름모로 보낸다
            EWolbaekTalismanSign sign = ToSign(_colorOrder[structure]);
            return new WolbaekTalismanPlayer.Snapshot(_castId, _sequence, sign, _serverColors[_colorOrder[structure]],
                center, _radius, appearedAt, now, phase, phaseStartedAt, progress);
        }

        private static EWolbaekTalismanSign ToSign(int serverColor)
        {
            switch (serverColor)
            {
                case 0:
                    return EWolbaekTalismanSign.Flower;

                case 1:
                    return EWolbaekTalismanSign.Moon;

                case 2:
                    return EWolbaekTalismanSign.Star;

                default:
                    return EWolbaekTalismanSign.Diamond;
            }
        }

        // 서버 색 네 가지를 방향 네 곳에 무작위로 섞고, 문양별 프리팹 인스턴스 번호를 같이 정한다
        private void ShuffleColors()
        {
            for (int i = 0; i < SERVER_COLOR_COUNT; ++i)
            {
                _colorOrder[i] = i;
            }

            for (int i = SERVER_COLOR_COUNT - 1; i > 0; --i)
            {
                int swap = UnityEngine.Random.Range(0, i + 1);
                int temp = _colorOrder[i];
                _colorOrder[i] = _colorOrder[swap];
                _colorOrder[swap] = temp;
            }

            for (int i = 0; i < SERVER_COLOR_COUNT; ++i)
            {
                _signOrder[i] = (int)ToSign(_colorOrder[i]);
            }
        }

        private void StopAll()
        {
            _isCasting = false;

            for (int i = 0; i < _players.Length; ++i)
            {
                _players[i].Stop();
            }
        }
    }
}
