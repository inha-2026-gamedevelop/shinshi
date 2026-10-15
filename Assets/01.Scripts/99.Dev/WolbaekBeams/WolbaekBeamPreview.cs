#if UNITY_EDITOR || DEVELOPMENT_BUILD
// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Dev
{
    /// <summary> 로컬 예시 배치·회전·압축 격자 시계는 개발 씬에만 있다 </summary>
    public sealed class WolbaekBeamPreview : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private WolbaekBeamPlayer[] _players;

        private readonly uint[] _ids           = new uint[4];
        private readonly bool[] _gridWarned    = new bool[24];
        private readonly bool[] _gridConfirmed = new bool[24];
        private bool   _manual;
        private bool   _running;
        private double _started;
        private int _cycle     = -1;
        private int _starStage = -2;
        private bool _confirmed;

        /****************************************
        *              Unity Event
        ****************************************/

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

        /****************************************
        *                Methods
        ****************************************/

        public void Begin(bool manual)
        {
            Stop();
            _manual  = manual;
            _running = true;
            _started = Time.unscaledTimeAsDouble;
            _cycle   = -1;
            Sample(0f);
        }

        public void Sample(float seconds)
        {
            if (!_running)
            {
                return;
            }

            int cycle = (int)(seconds / 8f);
            float time = seconds - cycle * 8f;
            if (cycle != _cycle)
            {
                _cycle     = cycle;
                _confirmed = false;
                _starStage = -2;
                for (int i = 0; i < 4; ++i)
                {
                    _ids[i] = _players[i].BeginCast((ulong)cycle + 1, 0d);
                }

                for (int i = 0; i < 24; ++i)
                {
                    _gridWarned[i]    = false;
                    _gridConfirmed[i] = false;
                }

                _players[0].Warn(_ids[0], 0, 1,
                    new WolbaekBeamShape(new Vector3(-4.5f, 0f, -3.5f), new Vector3(-4.5f, 0f, 2.5f), 3f),
                    0d, 1.2d, 2.6d, 0d);
                for (int i = 0; i < 2; ++i)
                {
                    float x = 2.75f + i * 1.5f;
                    _players[1].Warn(_ids[1], i, 1,
                        new WolbaekBeamShape(new Vector3(x, 0f, -3.5f), new Vector3(x, 0f, 2.5f), 0.9f,
                            new Vector3(3.5f, 0f, -0.5f), 3.2f), 0d, 1.2d, 7.8d, 0d);
                }

                StarWarnings(0f, 1, 0d, 1.2d, 0d);
                StarWarnings(30f, 1, 0d, 4.2d, 0d, 10);
            }

            if ((!_confirmed) && (time >= 1.2f))
            {
                _players[0].ConfirmActive(_ids[0], 0, 1, 1.2d, 2.6d, time);
                for (int i = 0; i < 2; ++i)
                {
                    _players[1].ConfirmActive(_ids[1], i, 1, 1.2d, 7.8d, time);
                }

                for (int i = 0; i < 5; ++i)
                {
                    _players[2].ConfirmActive(_ids[2], i, 1, 1.2d, 4.1d, time);
                }

                _confirmed = true;
            }

            if ((time >= 1.2f) && (time < 4.1f))
            {
                for (int i = 0; i < 5; ++i)
                {
                    _players[2].UpdateGeometry(_ids[2], i, 1, StarShape(i, (time - 1.2f) * 20f), time);
                }
            }

            if (time >= 4.2f)
            {
                int stage = Mathf.Min(2, (int)((time - 4.2f) / 0.8f));
                if (_starStage != stage)
                {
                    for (int i = 0; i < 5; ++i)
                    {
                        _players[2].StopLine(_ids[2], 10 + i, 1);
                    }

                    double hit = 4.2d + stage * 0.8d;
                    StarWarnings(stage * 30f, (uint)stage + 2, hit, hit, time);
                    for (int i = 0; i < 5; ++i)
                    {
                        _players[2].ConfirmActive(_ids[2], i, (uint)stage + 2, hit, hit + 0.35d, time);
                    }

                    _starStage = stage;
                }
            }

            for (int line = 0; line < 24; ++line)
            {
                double hit = line < 12 ? 1.2d + line * 0.18d : 3.88d + (line - 12) * 0.18d;
                if ((!_gridWarned[line]) && (time >= hit - 1d))
                {
                    _gridWarned[line] = _players[3].Warn(_ids[3], line, 1,
                        GridShape(line), hit - 1d, hit, hit + 0.12d, time);
                }

                if ((_gridWarned[line]) && (!_gridConfirmed[line]) && (time >= hit))
                {
                    _gridConfirmed[line] = _players[3].ConfirmActive(_ids[3], line, 1, hit, hit + 0.12d, time);
                }
            }

            for (int i = 0; i < 4; ++i)
            {
                _players[i].Tick(_ids[i], time);
            }
        }

        private void StarWarnings(float rotation, uint revision, double start, double active, double now, int first = 0)
        {
            for (int i = 0; i < 5; ++i)
            {
                _players[2].Warn(_ids[2], first + i, revision,
                    StarShape(i, rotation), start, active, active + 1d, now);
            }
        }

        private static WolbaekBeamShape StarShape(int line, float rotation)
        {
            Vector3 center = new Vector3(-4f, 0f, 6.5f);
            float a = (line * 72f + rotation) * Mathf.Deg2Rad,
                b = (((line + 2) % 5) * 72f + rotation) * Mathf.Deg2Rad;
            return new WolbaekBeamShape(center + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 2.5f,
                center + new Vector3(Mathf.Sin(b), 0f, Mathf.Cos(b)) * 2.5f, 0.17f);
        }

        private static WolbaekBeamShape GridShape(int line)
        {
            Vector3 center = new Vector3(4f, 0f, 6.5f);
            float lateral = ((line % 12) - 5.5f) * 0.48f;
            float half = Mathf.Sqrt(3.2f * 3.2f - lateral * lateral);
            Vector3 along = line < 12 ? Vector3.left : Vector3.forward;
            Vector3 right = line < 12 ? Vector3.forward : Vector3.right;
            Vector3 middle = center + right * lateral;
            return new WolbaekBeamShape(middle - along * half, middle + along * half, 0.22f, center, 3.2f);
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
