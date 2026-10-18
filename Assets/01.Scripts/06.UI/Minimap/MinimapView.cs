// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 원형 미니맵 창, 내 위치를 중심으로 지도 그림을 밀고 표시를 찍는다. 값은 바인더가 넣는다 </summary>
    public sealed class MinimapView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("지도")]
        [SerializeField] private RectTransform _viewport = null;
        [SerializeField] private RectTransform _map      = null;
        [SerializeField] private Image         _mapImage = null;

        [Header("표시")]
        [SerializeField] private RectTransform _selfMarker = null;
        [SerializeField] private Image[]       _markers    = null;
        [SerializeField] private TMP_Text      _nameLabel  = null;

        // 창 가장자리에서 표시가 안쪽으로 물러나는 거리
        [SerializeField] private float _markerEdgeInset = 10f;

        [Header("표시 색")]
        [SerializeField] private Color _partyColor = new Color(0.45f, 0.85f, 1f, 1f);
        [SerializeField] private Color _bossColor  = new Color(1f, 0.35f, 0.3f, 1f);
        [SerializeField] private Color _npcColor   = new Color(1f, 0.85f, 0.4f, 1f);

        private MinimapMapSO _mapData      = null;
        private Vector2      _lastCenter   = Vector2.zero;
        private float        _appliedScale = -1f;
        private bool         _hasCenter    = false;

        /****************************************
        *              Properties
        ****************************************/

        public int MarkerCapacity => (_markers != null) ? _markers.Length : 0;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 지도 그림과 이름을 바꾼다, null이면 그림을 숨긴다 </summary>
        public void SetMap(MinimapMapSO map)
        {
            _mapData       = map;
            _appliedScale  = -1f;
            _hasCenter     = false;

            if (_mapImage != null)
            {
                _mapImage.sprite  = (map != null) ? map.Sprite : null;
                _mapImage.enabled = (map != null) && (map.Sprite != null);
            }

            if (_nameLabel != null)
            {
                _nameLabel.text = (map != null) ? map.DisplayName : string.Empty;
            }

            HideMarkersFrom(0);
        }

        /// <summary> 창 가운데에 올 월드 X, Z 위치, 지도 그림이 반대로 움직인다 </summary>
        public void SetCenter(Vector2 centerXz)
        {
            if ((_mapData == null) || (_viewport == null) || (_map == null))
            {
                return;
            }

            float scale = MinimapRules.UiPerMeter(_viewport.rect.width, _mapData.ViewRadiusMeters);
            if (!Mathf.Approximately(scale, _appliedScale))
            {
                _appliedScale = scale;
                float side = MinimapRules.MapSide(_mapData.WorldHalfExtent, scale);
                _map.sizeDelta = new Vector2(side, side);
            }

            _lastCenter = centerXz;
            _hasCenter  = true;
            _map.anchoredPosition = MinimapRules.MapPosition(centerXz, _mapData.WorldCenter, scale);
        }

        /// <summary> 내 표시를 켜고 끈다, 내 위치를 모를 때는 끈다 </summary>
        public void SetSelfVisible(bool visible)
        {
            if ((_selfMarker != null) && (_selfMarker.gameObject.activeSelf != visible))
            {
                _selfMarker.gameObject.SetActive(visible);
            }
        }

        public void SetSelfYaw(float yawDegrees)
        {
            if (_selfMarker == null)
            {
                return;
            }

            _selfMarker.localRotation = Quaternion.Euler(0f, 0f, MinimapRules.YawToMarkerRotation(yawDegrees));
        }

        /// <summary> 표시 한 개를 찍는다, 칸이 모자라면 거짓이다. 창 밖이면 가장자리로 당긴다 </summary>
        public bool SetMarker(int index, Vector2 worldXz, EMinimapMarker kind)
        {
            if ((_markers == null) || (index < 0) || (index >= _markers.Length) || (_markers[index] == null))
            {
                return false;
            }

            if ((!_hasCenter) || (_viewport == null))
            {
                return false;
            }

            Image marker = _markers[index];
            float scale  = MinimapRules.UiPerMeter(_viewport.rect.width, _mapData.ViewRadiusMeters);
            Vector2 position = MinimapRules.WorldToViewport(worldXz, _lastCenter, scale);
            float radius = MinimapRules.MarkerRadius(_viewport.rect.width, _markerEdgeInset);
            position = MinimapRules.ClampToCircle(position, radius, out _);

            marker.rectTransform.anchoredPosition = position;
            marker.color = ColorOf(kind);
            if (!marker.gameObject.activeSelf)
            {
                marker.gameObject.SetActive(true);
            }

            return true;
        }

        /// <summary> 앞에서부터 쓰지 않는 표시를 숨긴다 </summary>
        public void HideMarkersFrom(int first)
        {
            if (_markers == null)
            {
                return;
            }

            for (int i = Mathf.Max(0, first); i < _markers.Length; ++i)
            {
                if ((_markers[i] != null) && (_markers[i].gameObject.activeSelf))
                {
                    _markers[i].gameObject.SetActive(false);
                }
            }
        }

        private Color ColorOf(EMinimapMarker kind)
        {
            switch (kind)
            {
                case EMinimapMarker.Boss:
                    return _bossColor;
                case EMinimapMarker.Npc:
                    return _npcColor;
                default:
                    return _partyColor;
            }
        }
    }
}
