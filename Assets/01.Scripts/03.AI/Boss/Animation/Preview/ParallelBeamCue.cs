// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 서버 규칙(이지 2P P02): 전장 중심에서 기준 방향의 좌우 같은 거리에 폭이 같은 평행 빔 여러 줄을 예고 시작 순간의 방향으로 고정한다
    // 판정 시각에 모든 줄을 함께 확정하고 유지 시간이 지나면 함께 끝난다. 주기 피해는 서버 소유라 이 도구는 표현만 한다
    /// <summary> 패턴 시작에 평행 빔 예고를 보이고 판정 시각에 확정해 유지 시간 동안 보여 주는 확인용 도구 </summary>
    public sealed class ParallelBeamCue : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const uint REVISION = 1;

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PatternMotionPreview _pattern;
        [SerializeField] private WolbaekBeamPlayer _player;
        [SerializeField] private Transform _origin;
        [SerializeField] private string _patternLabel;
        [SerializeField] private float _arenaRadius = 18f;
        [SerializeField] private int _beamCount = 2;
        [SerializeField] private float _lateralOffset = 5f;
        [SerializeField] private float _beamWidth = 2f;
        [SerializeField] private float _heightOffset;
        [SerializeField] private float _activeSeconds = 4f;

        private ulong  _castId;
        private uint   _playbackId;
        private double _castEnd;

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

            double expectedActive = now + strikeSeconds;
            _castEnd = expectedActive + _activeSeconds;

            for (int i = 0; i < _beamCount; ++i)
            {
                _player.Warn(_playbackId, i, REVISION, BuildBeam(i), now, expectedActive, _castEnd, now);
            }
        }

        private void OnStruck(string label, int shotIndex)
        {
            if ((label != _patternLabel) || (_playbackId == 0))
            {
                return;
            }

            double now = Time.timeAsDouble;
            _castEnd = now + _activeSeconds;

            for (int i = 0; i < _beamCount; ++i)
            {
                _player.ConfirmActive(_playbackId, i, REVISION, now, _castEnd, now);
            }
        }

        // 선 중심은 전장 원과의 교점이고 폭 띠와 원의 교집합은 표현 쪽이 전장 중심과 반경으로 다듬는다
        private WolbaekBeamShape BuildBeam(int index)
        {
            Vector3 center = _origin.position;
            BossBeamShape beam = BossBeamGeometry.Place(center, _arenaRadius, _origin.forward, index, _beamCount, _lateralOffset, _beamWidth);
            BossBeamGeometry.TryGetCenterLineChord(in beam, out Vector3 start, out Vector3 end);
            start.y += _heightOffset;
            end.y += _heightOffset;
            center.y += _heightOffset;
            return new WolbaekBeamShape(start, end, _beamWidth, center, _arenaRadius);
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
