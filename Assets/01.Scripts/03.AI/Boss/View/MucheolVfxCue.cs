// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 프리뷰의 EffectCue 하나에서 서버 사건으로 바꿀 수 없는 값(원본, 크기, 조준 기준 각도)만 옮겼다.
    // 위치와 시각은 서버 사건의 원점, 방향, 시각에서 읽는다
    /// <summary> 무철 패턴 VFX 한 개의 원본과 배치 </summary>
    public readonly struct MucheolVfxCue
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly EMucheolVfxTemplate _template;
        private readonly Vector3             _scale;
        private readonly float               _yawDegrees;

        /****************************************
        *              Properties
        ****************************************/

        public EMucheolVfxTemplate Template => _template;

        /// <summary> 원본 프리팹 루트에 곱하는 크기 </summary>
        public Vector3 Scale => _scale;

        /// <summary> 도, 서버 조준 방향에서 더하는 각도, 순차 타격은 서버 단계 각도와 같다 </summary>
        public float YawDegrees => _yawDegrees;

        /****************************************
        *                Methods
        ****************************************/

        public MucheolVfxCue(EMucheolVfxTemplate template, Vector3 scale, float yawDegrees = 0f)
        {
            _template   = template;
            _scale      = scale;
            _yawDegrees = yawDegrees;
        }
    }
}
