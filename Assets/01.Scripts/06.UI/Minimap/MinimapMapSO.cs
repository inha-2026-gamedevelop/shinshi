// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary> 맵 하나의 미니맵 그림과 월드 좌표 대응, 처음 보이는 반경. 로컬 표현 전용이며 서버 판정에 쓰지 않는다 </summary>
    [CreateAssetMenu(fileName = "MinimapMap", menuName = "Shinshi/UI/Minimap Map")]
    public sealed class MinimapMapSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private string _mapId = string.Empty;
        [SerializeField] private string _displayName = string.Empty;

        // 위에서 내려다본 실루엣 그림, 위쪽이 월드 +Z이고 오른쪽이 월드 +X다
        [SerializeField] private Sprite _sprite = null;

        // 그림 중심의 월드 X, Z 좌표
        [SerializeField] private Vector2 _worldCenter = Vector2.zero;

        // 그림이 덮는 정사각형 한 변의 절반, 미터
        [SerializeField, Min(1f)] private float _worldHalfExtent = 60f;

        // 내 위치를 중심으로 원형 창이 보여 주는 반경, 미터
        [SerializeField, Min(5f)] private float _viewRadiusMeters = 40f;

        // 보스 아레나처럼 붕괴하면 바깥이 사라지는 맵이 붕괴 뒤에 남기는 중앙 바닥. 반경이 0이면 붕괴를 지도에 반영하지 않는다
        [SerializeField]          private Vector2 _collapseCenter         = Vector2.zero;
        [SerializeField, Min(0f)] private float   _collapsedRadiusMeters = 0f;

        /****************************************
        *              Properties
        ****************************************/

        public string  MapId            => _mapId;
        public string  DisplayName      => _displayName;
        public Sprite  Sprite           => _sprite;
        public Vector2 WorldCenter      => _worldCenter;
        public float   WorldHalfExtent  => _worldHalfExtent;
        public float   ViewRadiusMeters => _viewRadiusMeters;

        public Vector2 CollapseCenter        => _collapseCenter;
        public float   CollapsedRadiusMeters => _collapsedRadiusMeters;
    }
}
