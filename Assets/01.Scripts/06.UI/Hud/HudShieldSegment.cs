// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Shinshi.UI
{
    // 씬과 프리팹을 고치지 않으려고 보호막이 처음 생길 때 체력 채움 칸 바로 다음 형제로 만든다.
    // 채움 칸의 그림과 세로 배치를 그대로 따라 막대 끝 모양이 같고 색만 흰색이다. 플레이어 HUD와 파티 칸이 같이 쓴다
    /// <summary> 체력 채움 끝에 이어 붙이는 흰 보호막 구간 </summary>
    public static class HudShieldSegment
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SEGMENT_NAME   = "ShieldFill";
        private const float  HIDE_EPSILON   = 0.001f; // 막대 비율, 이보다 짧으면 구간을 끈다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 보호막 구간을 start부터 end 비율까지 그린다, 처음 보일 때 만들고 길이가 없으면 끈다 </summary>
        public static void Apply(ref RectTransform segment, RectTransform fill, float start, float end)
        {
            if (!(end > start + HIDE_EPSILON))
            {
                if ((segment != null) && (segment.gameObject.activeSelf))
                {
                    segment.gameObject.SetActive(false);
                }

                return;
            }

            if (segment == null)
            {
                segment = Create(fill);
                if (segment == null)
                {
                    return;
                }
            }

            Vector2 anchorMin = segment.anchorMin;
            anchorMin.x       = start;
            segment.anchorMin = anchorMin;
            Vector2 anchorMax = segment.anchorMax;
            anchorMax.x       = end;
            segment.anchorMax = anchorMax;
            if (!segment.gameObject.activeSelf)
            {
                segment.gameObject.SetActive(true);
            }
        }

        private static RectTransform Create(RectTransform fill)
        {
            if ((fill == null) || (!(fill.parent is RectTransform track)))
            {
                return null;
            }

            var child = new GameObject(SEGMENT_NAME, typeof(RectTransform));
            child.hideFlags = HideFlags.DontSave;
            child.layer     = fill.gameObject.layer;
            var rect = (RectTransform)child.transform;
            rect.SetParent(track, false);

            // 채움 칸 바로 위에 그려 피해 잔상에 가리지 않고, 뒤에 오는 수치 글자는 덮지 않는다
            rect.SetSiblingIndex(fill.GetSiblingIndex() + 1);
            rect.anchorMin = new Vector2(0f, fill.anchorMin.y);
            rect.anchorMax = new Vector2(0f, fill.anchorMax.y);
            rect.pivot     = fill.pivot;
            rect.offsetMin = new Vector2(0f, fill.offsetMin.y);
            rect.offsetMax = new Vector2(0f, fill.offsetMax.y);

            var image = child.AddComponent<Image>();
            image.raycastTarget = false;
            image.color         = Color.white;
            if (fill.TryGetComponent(out Image source))
            {
                image.sprite                  = source.sprite;
                image.type                    = source.type;
                image.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
            }

            return rect;
        }
    }
}
