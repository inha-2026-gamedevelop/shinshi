// System
using System.Globalization;

// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;
using TMPro;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 서버가 보낸 고정 월드 좌표에 숫자를 투영하는 소유자 풀, 표시 중인 슬롯만 갱신한다 </summary>
    public sealed class DamageNumberPool : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string HUD_RESOURCE = "PF_PlayerStatusHud";
        private const int POOL_CAPACITY = 64;
        private const int SORTING_ORDER = 50;
        private const float LABEL_WIDTH = 240f;
        private const float LABEL_HEIGHT = 64f;
        private const float MAX_DIRECT_FORMAT_AMOUNT = 1000000000f;
        private const float OUTLINE_WIDTH = 0.35f;
        // 외곽선이 글자 안쪽까지 먹으므로 글자 자체를 먼저 굵게 부풀린다
        private const float FACE_DILATE = 0.35f;
        // 이 셰이더를 쓰는 재질이 프로젝트에 없으면 빌드가 셰이더를 빼서 외곽선이 사라진다. Hud/Resources/M_TextOutlineShaderRef.mat이 포함을 맡으니 지우지 않는다
        private const string OUTLINE_SHADER = "TextMeshPro/Distance Field";
        private const string OUTLINE_KEYWORD = "OUTLINE_ON";

        private static int _baseTweenersCapacity;
        private static int _baseSequencesCapacity;
        private static int _reservedPoolCount;

        [SerializeField] private float _lifetimeSeconds = 0.8f;
        [SerializeField] private float _risePixels = 64f;
        [SerializeField] private float _worldHeight = 1.5f;
        [SerializeField] private float _normalFontSize = 28f;
        [SerializeField] private float _criticalFontSize = 36f;
        [SerializeField] private Color _normalColor = Color.white;
        [SerializeField] private Color _criticalColor = new Color(1f, 0.75f, 0.15f, 1f);

        private Slot[] _slots;
        private Material _outlineMaterial;
        private Camera _camera;
        private RectTransform _canvasRect;
        private int _activeCount;
        private bool _hasTweenReservation;

        private sealed class Slot
        {
            public RectTransform Anchor;
            public RectTransform LabelRect;
            public TMP_Text Label;
            public CanvasGroup Group;
            public Sequence Tween;
            public Vector3 WorldPosition;
            public bool Active;
        }

        /****************************************
        *              Properties
        ****************************************/

        public int ActiveCount => _activeCount;
        public int Capacity => POOL_CAPACITY;
        public bool IsInitialized => _slots != null;
        public bool HasCamera => _camera != null;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake() => enabled = false;

        private void OnDisable() => Clear();

        private void OnDestroy()
        {
            if (_outlineMaterial != null)
            {
                Destroy(_outlineMaterial);
                _outlineMaterial = null;
            }
            if (_slots == null)
            {
                return;
            }
            for (int i = 0; i < _slots.Length; ++i)
            {
                _slots[i].Tween?.Kill();
            }
            if (_hasTweenReservation)
            {
                --_reservedPoolCount;
                _hasTweenReservation = false;
            }
        }

        // 카메라가 움직이는 동안 화면 좌표는 매 프레임 바뀐다. 이동·페이드는 재사용 DOTween이 처리한다
        private void LateUpdate()
        {
            if (_camera == null)
            {
                Clear();
                return;
            }
            for (int i = 0; i < _slots.Length; ++i)
            {
                Slot slot = _slots[i];
                if (!slot.Active)
                {
                    continue;
                }
                if ((!slot.Tween.IsPlaying()) || (!TryProject(slot.WorldPosition, out Vector2 point)))
                {
                    Release(slot);
                    continue;
                }
                slot.Anchor.anchoredPosition = point;
            }
            if (_activeCount == 0)
            {
                enabled = false;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Initialize(Camera camera)
        {
            if ((IsServerBuild) || (_slots != null))
            {
                return;
            }
            _camera = camera;
            GameObject prefab = Resources.Load<GameObject>(HUD_RESOURCE);
            TMP_Text style = (prefab != null) ? prefab.GetComponentInChildren<TMP_Text>(true) : null;
            TMP_FontAsset font = (style != null) ? style.font : TMP_Settings.defaultFontAsset;
            if (font == null)
            {
                Debug.LogError("[Combat] 기존 HUD TMP 폰트가 없어 피해 숫자 풀을 만들 수 없다");
                return;
            }
            _outlineMaterial = CreateOutlineMaterial((style != null) ? style.fontSharedMaterial : font.material);
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SORTING_ORDER;
            _canvasRect = (RectTransform)transform;
            ReserveTweenCapacity();
            _slots = new Slot[POOL_CAPACITY];
            for (int i = 0; i < _slots.Length; ++i)
            {
                _slots[i] = CreateSlot(i, font, style);
            }
        }

        public bool Show(in DamageFeedback feedback)
        {
            if ((IsServerBuild) || (_slots == null) || (_camera == null) || (!DamageFeedbackRules.IsValid(in feedback))
                || (_activeCount >= POOL_CAPACITY))
            {
                return false;
            }
            Vector3 world = feedback.WorldPosition + (Vector3.up * _worldHeight);
            if (!TryProject(world, out Vector2 point))
            {
                return false;
            }
            for (int i = 0; i < _slots.Length; ++i)
            {
                Slot slot = _slots[i];
                if (slot.Active)
                {
                    continue;
                }
                slot.Active = true;
                slot.WorldPosition = world;
                slot.Anchor.anchoredPosition = point;
                slot.LabelRect.anchoredPosition = Vector2.zero;
                slot.Group.alpha = 1f;
                slot.Label.color = feedback.IsCritical ? _criticalColor : _normalColor;
                slot.Label.fontSize = feedback.IsCritical ? _criticalFontSize : _normalFontSize;
                // TMP 숫자 포맷은 문자열 할당을 피한다. 치명타는 서버 표식의 색·크기·느낌표로 구분한다
                SetAmount(slot.Label, feedback.Amount, feedback.IsCritical);
                slot.Anchor.gameObject.SetActive(true);
                ++_activeCount;
                slot.Tween.Restart();
                enabled = true;
                return true;
            }
            return false;
        }

        public void BindCamera(Camera camera)
        {
            Clear();
            _camera = camera;
        }

        public bool IsBoundToCamera(Camera camera) => (camera != null) && (_camera == camera);

        public void Clear()
        {
            if (_slots != null)
            {
                for (int i = 0; i < _slots.Length; ++i)
                {
                    Release(_slots[i]);
                }
            }
            _activeCount = 0;
            enabled = false;
        }

        private static void SetAmount(TMP_Text label, float amount, bool critical)
        {
            if (amount > MAX_DIRECT_FORMAT_AMOUNT)
            {
                // TMP의 float 포맷은 decimal/long으로 변환한다. 극단적인 유한 값만 안전한 축약 문자열로 대신한다
                string value = amount.ToString("G4", CultureInfo.InvariantCulture);
                label.SetText(critical ? value + "!" : value);
                return;
            }
            label.SetText(critical ? "{0}!" : "{0}", Mathf.Max(1f, Mathf.Round(amount)));
        }

        private void ReserveTweenCapacity()
        {
            if (_baseTweenersCapacity == 0)
            {
                _baseTweenersCapacity = DOTween.tweenersCapacity;
                _baseSequencesCapacity = DOTween.sequencesCapacity;
            }
            ++_reservedPoolCount;
            _hasTweenReservation = true;
            // 설치 DLL의 기본 sequence 용량은 50이다. 기존 여유에 풀의 64 sequence·128 child를 더한다.
            // 동시 풀이 정리되어도 용량은 줄이지 않으며, 재바인딩마다 예약을 누적하지 않는다
            int sequences = Mathf.Max(DOTween.sequencesCapacity,
                _baseSequencesCapacity + (_reservedPoolCount * POOL_CAPACITY) + 1);
            int tweeners = Mathf.Max(DOTween.tweenersCapacity,
                _baseTweenersCapacity + (_reservedPoolCount * POOL_CAPACITY * 2) + 1);
            tweeners = Mathf.Max(tweeners, sequences);
            if ((tweeners != DOTween.tweenersCapacity) || (sequences != DOTween.sequencesCapacity))
            {
                DOTween.SetTweensCapacity(tweeners, sequences);
            }
        }

        // 밝은 바닥 위에서도 읽히도록 검은 외곽선을 두른다. 모바일용 거리장 셰이더는 외곽선 값을 줘도 그리지 못해 일반 거리장 셰이더 사본을 쓴다.
        // 셰이더를 찾지 못하면 null을 돌려 원래 재질을 그대로 쓰고, 풀의 모든 슬롯이 사본 하나를 같이 쓴다
        private static Material CreateOutlineMaterial(Material source)
        {
            Shader shader = Shader.Find(OUTLINE_SHADER);
            if ((source == null) || (shader == null))
            {
                return null;
            }
            var material = new Material(source) { hideFlags = HideFlags.DontSave };
            material.shader = shader;
            material.EnableKeyword(OUTLINE_KEYWORD);
            material.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, OUTLINE_WIDTH);
            material.SetFloat(ShaderUtilities.ID_FaceDilate, FACE_DILATE);
            return material;
        }

        private Slot CreateSlot(int index, TMP_FontAsset font, TMP_Text style)
        {
            var slot = new Slot();
            slot.Anchor = CreateRect("Damage" + index, _canvasRect);
            slot.Group = slot.Anchor.gameObject.AddComponent<CanvasGroup>();
            slot.Group.blocksRaycasts = false;
            slot.Group.interactable = false;
            slot.LabelRect = CreateRect("Value", slot.Anchor);
            slot.Label = slot.LabelRect.gameObject.AddComponent<TextMeshProUGUI>();
            slot.Label.font = font;
            if (_outlineMaterial != null)
            {
                slot.Label.fontSharedMaterial = _outlineMaterial;
            }
            else if (style != null)
            {
                slot.Label.fontSharedMaterial = style.fontSharedMaterial;
            }
            slot.Label.alignment = TextAlignmentOptions.Center;
            slot.Label.raycastTarget = false;
            slot.Label.textWrappingMode = TextWrappingModes.NoWrap;
            slot.Tween = DOTween.Sequence().SetAutoKill(false).SetUpdate(true).Pause();
            // 현재 런타임 asmdef에서 UI 모듈 확장을 참조하지 않아 코어 DOTween으로 같은 표현을 만든다
            slot.Tween.Append(DOTween.To(() => slot.LabelRect.anchoredPosition.y,
                value => slot.LabelRect.anchoredPosition = new Vector2(0f, value), _risePixels, _lifetimeSeconds).SetEase(Ease.OutQuad));
            slot.Tween.Join(DOTween.To(() => slot.Group.alpha, value => slot.Group.alpha = value,
                0f, _lifetimeSeconds).SetEase(Ease.InQuad));
            slot.Tween.ForceInit();
            slot.Anchor.gameObject.SetActive(false);
            return slot;
        }

        private static RectTransform CreateRect(string objectName, Transform parent)
        {
            var rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.hideFlags = HideFlags.DontSave;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(LABEL_WIDTH, LABEL_HEIGHT);
            return rect;
        }

        private bool TryProject(Vector3 world, out Vector2 point)
        {
            point = Vector2.zero;
            if ((_camera == null) || (!DamageFeedbackRules.IsFinite(world))
                || (!DamageFeedbackRules.IsVisibleProjection(_camera.WorldToViewportPoint(world))))
            {
                return false;
            }
            Vector3 screen = _camera.WorldToScreenPoint(world);
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out point);
        }

        private void Release(Slot slot)
        {
            if (!slot.Active)
            {
                return;
            }
            slot.Active = false;
            --_activeCount;
            slot.Tween.Rewind();
            slot.Tween.Pause();
            slot.Anchor.gameObject.SetActive(false);
        }
    }
}
