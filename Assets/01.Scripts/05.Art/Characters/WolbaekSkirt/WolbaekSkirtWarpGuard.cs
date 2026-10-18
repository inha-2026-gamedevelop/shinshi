// Unity
using UnityEngine;
using UnityEngine.Animations.Rigging;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    // 워프로 위치가 한 번에 바뀌면 Damped Transform이 워프 전 위치를 기억한 채 천천히 따라와 치마가 끌려온다
    // 워프 프레임 동안 Rig 가중치를 0으로 두면 그 기억이 새 위치로 따라가므로, 가중치를 되돌려도 끌려오지 않는다
    // 워프 연출은 몸 렌더러를 끄는데, 컬링 모드가 렌더러 기준이면 애니메이터와 Rig가 갱신을 멈춰 가중치 0 프레임이 헛돈다
    // 그래서 항상 애니메이션하도록 고정한다
    /// <summary> 워프 순간에 치마 Rig를 잠깐 꺼서 치마가 옛 위치에서 끌려오지 않게 하는 표현 전용 컴포넌트 </summary>
    public sealed class WolbaekSkirtWarpGuard : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int HOLD_FRAMES = 2;

        [SerializeField] private Rig _rig;

        private float _normalWeight;
        private int   _releaseFrame;
        private bool  _isHolding;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _normalWeight = _rig.weight;
            GetComponent<Animator>().cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        private void Update()
        {
            if (_isHolding && (Time.frameCount >= _releaseFrame))
            {
                Release();
            }
        }

        private void OnDisable()
        {
            if (_isHolding)
            {
                Release();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 보스가 순간 이동하는 같은 프레임에 불러야 한다. 애니메이터 평가보다 먼저 가중치가 0이 되어야 하기 때문이다 </summary>
        public void OnTeleported()
        {
            if (!_isHolding)
            {
                _normalWeight = _rig.weight;
            }

            _isHolding = true;
            _releaseFrame = Time.frameCount + HOLD_FRAMES;
            _rig.weight = 0f;
        }

        private void Release()
        {
            _isHolding = false;
            _rig.weight = _normalWeight;
        }
    }
}
