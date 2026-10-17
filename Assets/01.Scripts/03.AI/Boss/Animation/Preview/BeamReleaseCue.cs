// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.AI
{
    // 서버 규칙(1P P06): 예고가 시작되면 직사각형 바닥 예고를 보이고 판정 시각에 한 번 확정한다. 예고 중 조준 추적은 이 도구에서 다루지 않는다
    // 확정 뒤 파면이 지속하는 시간은 서버 규칙이 아니라 표현값이라 직접 정한다
    /// <summary> 패턴 시작에 직사각 빔 예고를 보이고 판정 시각에 확정해 단발 파면을 재생하는 확인용 도구 </summary>
    public sealed class BeamReleaseCue : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int  LINE_ID  = 1;
        private const uint REVISION = 1;

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PatternMotionPreview _pattern;
        [SerializeField] private WolbaekBeamPlayer _player;
        [SerializeField] private Transform _origin;
        [SerializeField] private string _patternLabel;
        [SerializeField] private float _length = 14f;
        [SerializeField] private float _width = 4f;
        [SerializeField] private float _activeSeconds = 1.4f;

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

            Vector3 start = _origin.position;
            Vector3 forward = _origin.forward;
            forward.y = 0f;
            WolbaekBeamShape shape = new WolbaekBeamShape(start, start + (forward.normalized * _length), _width);

            double expectedActive = now + strikeSeconds;
            _castEnd = expectedActive + _activeSeconds;
            _player.Warn(_playbackId, LINE_ID, REVISION, shape, now, expectedActive, _castEnd, now);
        }

        private void OnStruck(string label, int shotIndex)
        {
            if ((label != _patternLabel) || (_playbackId == 0))
            {
                return;
            }

            double now = Time.timeAsDouble;
            _castEnd = now + _activeSeconds;
            _player.ConfirmActive(_playbackId, LINE_ID, REVISION, now, _castEnd, now);
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
