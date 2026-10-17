// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 서버 규칙(하드 2P P02): 가로 N줄을 먼저, 세로 N줄을 뒤에 놓고 줄마다 한 번 즉발 타격한다. 줄은 월드 축 기준이라 방향이 바뀌지 않는다
    //   가로줄은 월드 -X 방향으로 뻗고 번호가 커질수록 월드 Z가 커지며, 세로줄은 월드 +Z 방향으로 뻗고 번호가 커질수록 월드 X가 커진다
    //   줄 k의 타격은 시전 시작 + 첫 타격 시간 + k x 타격 간격이고, 세로 첫 줄은 가로 마지막 줄에서 방향 전환 간격만큼 뒤다
    //   줄의 예고는 타격보다 첫 타격 시간만큼 앞서 시작한다. 이 도구는 서버 알림이 없어서 같은 규칙으로 시각을 직접 계산한다
    /// <summary> 격자 순차 빔의 줄마다 예고와 즉발 타격을 예정 시각에 호출하는 확인용 도구 </summary>
    public sealed class GridBeamCue : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const uint  REVISION   = 1;
        private const float HALF       = 0.5f;
        private const int   AXIS_COUNT = 2;

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PatternMotionPreview _pattern;
        [SerializeField] private WolbaekBeamPlayer _player;
        [SerializeField] private Transform _origin;
        [SerializeField] private string _patternLabel;
        [SerializeField] private float _arenaRadius = 18f;
        [SerializeField] private int _lineCountPerAxis = 12;
        [SerializeField] private float _lineSpacing = 3f;
        [SerializeField] private float _lineWidth = 3f;
        [SerializeField] private float _hitIntervalSeconds = 0.5f;
        [SerializeField] private float _turnIntervalSeconds = 1.5f;
        [SerializeField] private float _hitSeconds = 0.4f;
        [SerializeField] private float _heightOffset;

        private ulong  _castId;
        private uint   _playbackId;
        private double _castStart;
        private float  _firstHitSeconds;
        private int    _nextWarn;
        private int    _nextHit;
        private double _castEnd;

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
            EndCast();
        }

        private void Update()
        {
            if (_playbackId == 0)
            {
                return;
            }

            double now = Time.timeAsDouble;
            int total = _lineCountPerAxis * AXIS_COUNT;

            // 예정 시각이 지난 예고와 타격을 시각 순서대로 처리하고, 같은 시각이면 예고를 먼저 처리한다
            while ((_nextWarn < total) && (WarnTime(_nextWarn) <= now))
            {
                _player.Warn(_playbackId, _nextWarn, REVISION, BuildLine(_nextWarn), WarnTime(_nextWarn), HitTime(_nextWarn),
                    HitTime(_nextWarn) + _hitSeconds, now);
                ++_nextWarn;
            }

            while ((_nextHit < _nextWarn) && (HitTime(_nextHit) <= now))
            {
                _player.ConfirmActive(_playbackId, _nextHit, REVISION, HitTime(_nextHit), HitTime(_nextHit) + _hitSeconds, now);
                ++_nextHit;
            }

            _player.Tick(_playbackId, now);

            if (now > _castEnd)
            {
                EndCast();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnStarted(string label, float strikeSeconds)
        {
            if (label != _patternLabel)
            {
                return;
            }

            EndCast();

            _castStart = Time.timeAsDouble;
            ++_castId;
            _playbackId = _player.BeginCast(_castId, _castStart);

            if (_playbackId == 0)
            {
                return;
            }

            _firstHitSeconds = strikeSeconds;
            _nextWarn = 0;
            _nextHit = 0;
            _castEnd = HitTime((_lineCountPerAxis * AXIS_COUNT) - 1) + _hitSeconds;
        }

        private double HitTime(int line)
        {
            int lastHorizontal = _lineCountPerAxis - 1;

            if (line < _lineCountPerAxis)
            {
                return _castStart + _firstHitSeconds + (line * _hitIntervalSeconds);
            }

            return _castStart + _firstHitSeconds + (lastHorizontal * _hitIntervalSeconds) + _turnIntervalSeconds
                + ((line - _lineCountPerAxis) * _hitIntervalSeconds);
        }

        private double WarnTime(int line)
        {
            return HitTime(line) - _firstHitSeconds;
        }

        // 가로줄은 -X 방향, 세로줄은 +Z 방향을 기준으로 놓고 중심선과 전장 원의 교점을 줄의 양 끝으로 쓴다
        private WolbaekBeamShape BuildLine(int line)
        {
            bool isHorizontal = line < _lineCountPerAxis;
            Vector3 direction = isHorizontal ? Vector3.left : Vector3.forward;
            int index = isHorizontal ? line : line - _lineCountPerAxis;
            Vector3 center = _origin.position;
            BossBeamShape beam = BossBeamGeometry.Place(center, _arenaRadius, direction, index, _lineCountPerAxis, _lineSpacing * HALF, _lineWidth);
            BossBeamGeometry.TryGetCenterLineChord(in beam, out Vector3 start, out Vector3 end);
            start.y += _heightOffset;
            end.y += _heightOffset;
            center.y += _heightOffset;
            return new WolbaekBeamShape(start, end, _lineWidth, center, _arenaRadius);
        }

        private void EndCast()
        {
            if (_playbackId != 0)
            {
                _player.Stop(_playbackId);
                _playbackId = 0;
            }
        }
    }
}
