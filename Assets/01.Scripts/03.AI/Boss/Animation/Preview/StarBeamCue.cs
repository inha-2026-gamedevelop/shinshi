// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 서버 규칙
    //   회전형(이지 2P P01, 단계 수 0): 예고 동안 별의 방향을 고정하고 판정 시각에 5선을 확정한다
    //     확정 뒤에는 서버가 정한 방향 부호로 초당 정해진 각도만큼 별 전체가 전장 중심을 축으로 돈다. 회전각은 확정 시각부터 흐른 시간으로 직접 계산한다
    //   단계형(하드 2P P01, 단계 수 1 이상): 같은 별을 단계 각도만큼 돌려 가며 단계마다 즉발 타격한다, 단계 k의 예정 시각은 첫 판정 + k x 간격이다
    //     선 슬롯은 두 묶음을 번갈아 쓴다. 지금 단계는 확정으로, 다음 단계는 예고(희미한 ghost)로 보이고 끝난 묶음은 다다음 단계의 예고로 다시 쓴다
    /// <summary> 패턴 시작에 별 5선 예고를 보이고 판정 시각에 확정해 회전시키거나 단계별로 보여 주는 확인용 도구 </summary>
    public sealed class StarBeamCue : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const uint REVISION_BASE = 1;
        private const int  GROUP_COUNT   = 2;

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PatternMotionPreview _pattern;
        [SerializeField] private WolbaekBeamPlayer _player;
        [SerializeField] private Transform _origin;
        [SerializeField] private string _patternLabel;
        [SerializeField] private float _ringRadius = 14f;
        [SerializeField] private int _vertexCount = 5;
        [SerializeField] private int _connectStep = 2;
        [SerializeField] private float _beamWidth = 1.5f;
        [SerializeField] private float _heightOffset = 1f;
        [SerializeField] private float _degreesPerSecond = 20f;
        [SerializeField] private bool _rotatesRight = true;
        [SerializeField] private float _activeSeconds = 6f;
        [SerializeField] private int _stageCount;
        [SerializeField] private float _stageDegrees = 30f;
        [SerializeField] private float _stageIntervalSeconds = 0.8f;

        private ulong         _castId;
        private uint          _playbackId;
        private BossStarShape _shape;
        private double        _castStart;
        private float         _strikeSeconds;
        private double        _activeStart;
        private double        _castEnd;
        private bool          _isActive;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _pattern.Started += OnStarted;
            _pattern.Struck += OnStruck;
        }

        private void OnDisable()
        {
            _pattern.Started -= OnStarted;
            _pattern.Struck -= OnStruck;
            EndCast();
        }

        private void Update()
        {
            if (_playbackId == 0)
            {
                return;
            }

            double now = Time.timeAsDouble;

            if ((_isActive) && (_stageCount == 0))
            {
                RotateBeams(now);
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

            double now = Time.timeAsDouble;
            ++_castId;
            _playbackId = _player.BeginCast(_castId, now);

            if (_playbackId == 0)
            {
                return;
            }

            _castStart = now;
            _strikeSeconds = strikeSeconds;
            _shape = BossStarGeometry.Place(_origin.position, _ringRadius, _origin.forward, _ringRadius,
                _vertexCount, _connectStep, _beamWidth, _degreesPerSecond, _rotatesRight);

            if (_stageCount == 0)
            {
                WarnRotating(now);
                return;
            }

            _castEnd = now + strikeSeconds + ((_stageCount - 1) * _stageIntervalSeconds) + _activeSeconds;
            WarnStage(0, now);

            if (_stageCount > 1)
            {
                WarnStage(1, now);
            }
        }

        private void OnStruck(string label, int shotIndex)
        {
            if ((label != _patternLabel) || (_playbackId == 0))
            {
                return;
            }

            double now = Time.timeAsDouble;

            if (_stageCount == 0)
            {
                ConfirmRotating(now);
                return;
            }

            ConfirmStage(shotIndex, now);

            // 방금 끝난 묶음이 쓰던 슬롯을 두 단계 뒤의 예고로 다시 쓴다
            if ((shotIndex >= 1) && ((shotIndex + 1) < _stageCount))
            {
                WarnStage(shotIndex + 1, now);
            }
        }

        private void WarnRotating(double now)
        {
            double expectedActive = now + _strikeSeconds;
            _castEnd = expectedActive + _activeSeconds;

            for (int i = 0; i < _shape.BeamCount; ++i)
            {
                _player.Warn(_playbackId, i, REVISION_BASE, BuildBeam(i, 0f), now, expectedActive, _castEnd, now);
            }
        }

        private void ConfirmRotating(double now)
        {
            _activeStart = now;
            _castEnd = now + _activeSeconds;
            _isActive = true;

            for (int i = 0; i < _shape.BeamCount; ++i)
            {
                _player.ConfirmActive(_playbackId, i, REVISION_BASE, now, _castEnd, now);
            }
        }

        private void RotateBeams(double now)
        {
            float rotation = BossStarGeometry.RotationAt(in _shape, now - _activeStart, _activeSeconds);

            for (int i = 0; i < _shape.BeamCount; ++i)
            {
                _player.UpdateGeometry(_playbackId, i, REVISION_BASE, BuildBeam(i, rotation), now);
            }
        }

        private void WarnStage(int stage, double now)
        {
            double expectedActive = _castStart + _strikeSeconds + (stage * _stageIntervalSeconds);
            float rotation = stage * _stageDegrees;

            for (int i = 0; i < _shape.BeamCount; ++i)
            {
                _player.Warn(_playbackId, StageLineId(stage, i), StageRevision(stage), BuildBeam(i, rotation),
                    _castStart, expectedActive, expectedActive + _activeSeconds, now);
            }
        }

        private void ConfirmStage(int stage, double now)
        {
            for (int i = 0; i < _shape.BeamCount; ++i)
            {
                _player.ConfirmActive(_playbackId, StageLineId(stage, i), StageRevision(stage), now, now + _activeSeconds, now);
            }
        }

        private int StageLineId(int stage, int beamIndex)
        {
            return ((stage % GROUP_COUNT) * _shape.BeamCount) + beamIndex;
        }

        private static uint StageRevision(int stage)
        {
            return REVISION_BASE + (uint)(stage / GROUP_COUNT);
        }

        private WolbaekBeamShape BuildBeam(int index, float rotation)
        {
            BossStarGeometry.GetBeam(in _shape, index, rotation, out Vector3 start, out Vector3 end);
            start.y += _heightOffset;
            end.y += _heightOffset;
            return new WolbaekBeamShape(start, end, _beamWidth);
        }

        private void EndCast()
        {
            if (_playbackId != 0)
            {
                _player.Stop(_playbackId);
                _playbackId = 0;
            }

            _isActive = false;
        }
    }
}
