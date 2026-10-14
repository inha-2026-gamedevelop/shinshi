#if UNITY_EDITOR || DEVELOPMENT_BUILD
// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Dev
{
    public sealed class WolbaekWarpPreview : MonoBehaviour
    {
        private const float LOOP_SECONDS = 4f;
        private const float WARNING_SECONDS = 1.5f;
        private const float LANDING_X = 7f;
        private const float ORIGIN_X = -5f;
        private const float LANDING_RADIUS = 5f;
        private const int STATE_COUNT = 3;
        private static readonly Vector3 G_Size = new Vector3(0.98f, 1.92f, 0.5f);
        [SerializeField] private WolbaekWarpPlayer[] _players;
        [SerializeField] private GameObject _landingBody;
        [SerializeField] private GameObject _reformBody;
        private readonly uint[] _ids = new uint[STATE_COUNT];
        private bool _manual;
        private bool _running;
        private bool _confirmed;
        private double _started;
        private int _cycle = -1;

        private void Start()
        {
            Begin(false);
        }

        private void Update()
        {
            if ((_running) && (!_manual))
            {
                Sample((float)(Time.unscaledTimeAsDouble - _started));
            }
        }

        private void OnDisable()
        {
            Stop();
        }

        public void Begin(bool manual)
        {
            Stop();
            _manual = manual;
            _running = true;
            _started = Time.unscaledTimeAsDouble;
            _cycle = -1;
            Sample(0f);
        }

        public void Sample(float seconds)
        {
            if (!_running)
            {
                return;
            }
            int cycle = (int)(seconds / LOOP_SECONDS);
            float time = seconds - cycle * LOOP_SECONDS;
            if (cycle != _cycle)
            {
                _cycle = cycle;
                _confirmed = false;
                Bounds origin = new Bounds(new Vector3(ORIGIN_X, G_Size.y * 0.5f, 0f), G_Size);
                Bounds destination = new Bounds(new Vector3(0f, G_Size.y * 0.5f, 0f), G_Size);
                _ids[0] = _players[0].PlayBody((ulong)cycle + 1, origin, 0d, 0d, true);
                _ids[1] = _players[1].PlayBody((ulong)cycle + 1, destination, 0d, 0d, true);
                _ids[2] = _players[2].PlayLandedWarning((ulong)cycle + 1, Vector3.right * LANDING_X, LANDING_RADIUS, 0d, WARNING_SECONDS, 0d);
                _landingBody.SetActive(true);
                _reformBody.SetActive(false);
            }
            if ((!_confirmed) && (time >= WARNING_SECONDS))
            {
                _players[2].ConfirmStrike(_ids[2], WARNING_SECONDS, time);
                _confirmed = true;
            }
            for (int i = 0; i < _players.Length; ++i)
            {
                _players[i].Tick(_ids[i], time);
            }
            _reformBody.SetActive(time >= _players[1].Settings.BodySeconds);
        }

        public void Stop()
        {
            _running = false;
            if (_players != null)
            {
                for (int i = 0; i < _players.Length; ++i)
                {
                    if (_players[i] != null)
                    {
                        _players[i].Stop();
                    }
                }
            }
        }
    }
}
#endif
