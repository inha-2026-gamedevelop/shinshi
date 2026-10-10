// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버가 시전 시작에 고정하는 격자 배치다. 줄은 전장 중심을 기준으로 대칭이고 줄 사이에 틈이 없다
    // 가로줄은 월드 -X 방향으로 뻗는 띠이고 BossBeamGeometry.RightOf(-X)가 +Z이므로, 줄 번호가 커질수록 월드 Z가 커진다(아래에서 위)
    // 세로줄은 월드 +Z 방향으로 뻗는 띠이고 RightOf(+Z)가 +X이므로, 줄 번호가 커질수록 월드 X가 커진다(왼쪽에서 오른쪽)
    // 화면의 위아래와 월드 Z의 대응은 카메라 방위가 정하므로 이 구조체는 월드 축까지만 약속한다
    // 판정은 줄마다 BossBeamGeometry.Contains 하나이고 전장 원 밖은 거기서 잘린다
    /// <summary> 격자 순차 빔의 전장 기준 배치, 가로 N줄과 세로 N줄의 기하를 만든다 </summary>
    public readonly struct BossGridBeamLayout
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float HALF          = 0.5f;
        private const float COVER_EPSILON = 0.001f; // m, 줄이 전장을 덮는지 비교할 때 부동소수 오차 흡수

        /****************************************
        *                Fields
        ****************************************/

        private readonly Vector3 _arenaCenter;
        private readonly float   _arenaRadius;
        private readonly int     _lineCount;
        private readonly float   _lineWidth;
        private readonly float   _lineSpacing;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 전장 중심, 보스 위치가 아니라 전장 정보에서 온 값이다 </summary>
        public Vector3 ArenaCenter => _arenaCenter;

        /// <summary> 전장 반경(m) </summary>
        public float ArenaRadius => _arenaRadius;

        /// <summary> 한 방향의 줄 수 </summary>
        public int LineCount => _lineCount;

        /// <summary> 줄 하나의 띠 폭(m) </summary>
        public float LineWidth => _lineWidth;

        /// <summary> 이웃한 줄의 중심선 사이 거리(m) </summary>
        public float LineSpacing => _lineSpacing;

        // 첫 줄과 끝 줄의 중심선 사이는 (N - 1) x 간격이고 바깥 가장자리는 각각 폭의 절반만큼 더 나간다. 폭이 간격과 다르면 N x 간격 / 2와 달라진다
        /// <summary> 줄 전체가 덮는 폭의 절반(m), 전장 중심에서 첫 줄과 끝 줄의 바깥 가장자리까지 거리다 </summary>
        public float HalfExtent => ((((_lineCount - 1) * _lineSpacing) + _lineWidth) * HALF);

        /// <summary> 이웃한 줄 사이에 틈이 없는가, 폭이 중심 간격 이상이면 가장자리가 맞닿거나 겹친다 </summary>
        public bool IsGapFree => _lineWidth >= (_lineSpacing - COVER_EPSILON);

        /// <summary> 줄 전체가 전장 지름을 모두 덮는가 </summary>
        public bool CoversArena => (HalfExtent + COVER_EPSILON) >= _arenaRadius;

        /****************************************
        *                Methods
        ****************************************/

        public BossGridBeamLayout(Vector3 arenaCenter, float arenaRadius, int lineCount, float lineWidth, float lineSpacing)
        {
            _arenaCenter = arenaCenter;
            _arenaRadius = arenaRadius;
            _lineCount   = lineCount;
            _lineWidth   = lineWidth;
            _lineSpacing = lineSpacing;
        }

        /// <summary> 방향과 줄 번호로 줄 하나의 기하를 만든다, 번호는 0부터이고 발사 순서와 같다 </summary>
        public BossBeamShape GetLine(EBossGridBeamAxis axis, int index)
        {
            Vector3 direction = (axis == EBossGridBeamAxis.Horizontal) ? Vector3.left : Vector3.forward;
            return BossBeamGeometry.Place(_arenaCenter, _arenaRadius, direction, index, _lineCount, _lineSpacing * HALF, _lineWidth);
        }
    }
}
