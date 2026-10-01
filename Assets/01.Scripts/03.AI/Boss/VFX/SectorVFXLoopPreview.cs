// System
using System.Collections;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 부채꼴 연출을 각도와 방식을 바꿔 가며 반복 재생한다, 눈으로 확인하는 용도 </summary>
    public sealed class SectorVFXLoopPreview : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float DEFAULT_INTERVAL = 0.8f;

        [SerializeField] private SectorVFXController _target;
        [SerializeField, Min(0f)] private float _interval = DEFAULT_INTERVAL;
        [SerializeField] private float _telegraphDuration = 1f;
        [SerializeField] private float _radius = 6f;
        [SerializeField] private float _innerRadius = 0f;
        [SerializeField] private float[] _angles = { 30f, 60f, 90f, 120f, 160f, 180f };
        [SerializeField] private ESectorExplosionMode[] _modes =
        {
            ESectorExplosionMode.AngularSweep,
            ESectorExplosionMode.RadialSweep,
            ESectorExplosionMode.Simultaneous
        };
        [SerializeField] private ESectorSweepDirection[] _directions =
        {
            ESectorSweepDirection.LeftToRight,
            ESectorSweepDirection.RightToLeft
        };

        private WaitUntil _waitFinish;
        private WaitForSeconds _waitInterval;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            if (_target == null)
            {
                Debug.LogError("[SectorVFX] 미리보기 대상이 지정되지 않았다", this);
                return;
            }

            _waitFinish = new WaitUntil(() => !_target.IsPlaying);
            _waitInterval = new WaitForSeconds(_interval);
            StartCoroutine(CoLoop());
        }

        /****************************************
        *                Methods
        ****************************************/

        // 각도는 매번, 방식은 각도가 한 바퀴 돌 때마다, 방향은 방식이 한 바퀴 돌 때마다 바뀐다
        private IEnumerator CoLoop()
        {
            int step = 0;
            while (true)
            {
                int modeStep = step / _angles.Length;
                int directionStep = modeStep / _modes.Length;

                SectorShape shape = SectorShape.Default;
                shape.Angle = _angles[step % _angles.Length];
                shape.Radius = _radius;
                shape.InnerRadius = _innerRadius;

                SectorBehaviour behaviour = SectorBehaviour.Default;
                behaviour.Mode = _modes[modeStep % _modes.Length];
                behaviour.Direction = _directions[directionStep % _directions.Length];
                behaviour.TelegraphDuration = _telegraphDuration;

                Debug.Log("[SectorVFX] angle " + shape.Angle + ", " + behaviour.Mode + ", " + behaviour.Direction);
                _target.Play(shape, behaviour);

                yield return null;
                yield return _waitFinish;
                yield return _waitInterval;
                ++step;
            }
        }
    }
}
