// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using DG.Tweening;
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 채움 게이지 끝에 퍼센트 글자를 붙여 둔다, 값이 오를 때마다 채움이 끊어 오르며 번쩍이고 불꽃이 튄다 </summary>
    public sealed class UiGaugeFollowLabel : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  PERCENT_SCALE  = 100f;
        private const float  ANCHOR_MIDDLE  = 0.5f;
        private const float  PIVOT_LEFT     = 0f;
        private const float  PIVOT_RIGHT    = 1f;
        private const string PERCENT_FORMAT = "{0:0}%";

        // 한 번 끊어 오르는 칸에서 오르는 데 쓰는 시간 비율, 나머지는 멈춘다
        private const float HOP_RISE_RATIO   = 0.35f;
        private const float SWEEP_RISE_RATIO = 0.6f;
        private const float EASE_POWER       = 2f;
        private const int   NONE             = -1;

        private const float SPARK_GRAVITY   = 220f;
        private const float SPARK_SPEED_MIN = 40f;
        private const float SPARK_SPEED_MAX = 140f;
        private const float SPARK_LIFE_MIN  = 0.3f;
        private const float SPARK_LIFE_MAX  = 0.55f;
        private const float SPARK_SIZE_MIN  = 2f;
        private const float SPARK_SIZE_MAX  = 4.5f;
        private const float EDGE_STRETCH    = 0.9f;
        private const float LABEL_POP       = 0.3f;
        private const float SPARK_JITTER_Y  = 3f;
        private const float SPARK_ANGLE_MIN = -20f;
        private const float SPARK_ANGLE_MAX = 110f;
        private const float MIN_FADE_TIME   = 0.01f;

        [Header("표시")]
        [SerializeField] private Image         _fill      = null;
        [Tooltip("채움 Image의 자식으로 둔다, 부모 너비를 막대 길이로 삼아 비율 위치에 붙는다")]
        [SerializeField] private RectTransform _label     = null;
        [SerializeField] private TMP_Text      _labelText = null;

        [Header("배치")]
        [Tooltip("채움 끝과 글자 사이 간격(px)")]
        [SerializeField] private float _gap        = 6f;
        [Tooltip("이 비율부터 글자를 채움 안쪽으로 넣는다, 막대 밖으로 넘치지 않게 한다")]
        [SerializeField] private float _insideFrom = 0.85f;

        [Header("오르는 모양 (촵촵 촤자자작)")]
        [Tooltip("많이 오를 때 걸리는 시간")]
        [SerializeField] private float _surgeDuration = 0.8f;
        [Tooltip("이 값 이상 오를 때만 여러 번 끊어 오른다, 미만은 한 번 촵 하고 오른다")]
        [SerializeField] private float _surgeMinDelta = 0.04f;
        [Tooltip("조금 오를 때 한 번 촵 하고 오르는 시간")]
        [SerializeField] private float _smallDuration = 0.2f;
        [Tooltip("앞부분에서 끊어 오르는 횟수(촵촵)")]
        [SerializeField] private int   _hopCount      = 3;
        [Tooltip("전체 시간 중 촵촵 구간 비율")]
        [Range(0.1f, 0.7f)]
        [SerializeField] private float _hopTimeRatio  = 0.4f;
        [Tooltip("촵촵 구간에서 오르는 양의 비율")]
        [Range(0.05f, 0.7f)]
        [SerializeField] private float _hopHeightRatio = 0.3f;
        [Tooltip("남은 구간을 잘게 끊어 몰아 오르는 횟수(촤자자작)")]
        [SerializeField] private int   _sweepSteps    = 6;

        [Header("번쩍임 (비워 두면 그 효과만 쓰지 않는다)")]
        [Tooltip("채움 Image의 자식. 채움 끝에 붙어 번쩍이는 가는 띠")]
        [SerializeField] private Image _edgeGlow = null;
        [Tooltip("채움 뒤에 깔리는 같은 설정의 채움 Image. 목표 위치까지 밝게 미리 깔렸다가 사라진다")]
        [SerializeField] private Image _gainBar  = null;
        [Tooltip("끊어 오를 때마다 채움이 하얗게 번쩍이는 정도")]
        [Range(0f, 1f)]
        [SerializeField] private float _flashStrength    = 0.75f;
        [Tooltip("끊어 오를 때마다 틀이 위아래로 부푸는 정도")]
        [SerializeField] private float _framePunch       = 0.28f;
        [SerializeField] private float _tickFadeDuration = 0.22f;
        [SerializeField] private float _gainFadeDuration = 0.5f;
        [Tooltip("목표까지 미리 깔리는 밝은 띠의 처음 진하기")]
        [Range(0f, 1f)]
        [SerializeField] private float _gainStartAlpha   = 0.4f;

        [Header("불꽃")]
        [Tooltip("끊어 오를 때마다 채움 끝에서 튀는 불꽃 개수 상한, 0이면 쓰지 않는다")]
        [SerializeField] private int   _sparkCount     = 14;
        [SerializeField] private int   _sparksPerTick  = 3;
        [SerializeField] private Color _sparkColor     = new Color(1f, 0.92f, 0.62f, 1f);

        private Tweener      _tween     = null;
        private EaseFunction _surgeEase = null;
        private float        _ratio     = 0f;
        private int          _percent   = -1;

        private Color         _fillBaseColor = Color.white;
        private RectTransform _frame         = null;
        private bool          _isSurge       = false;
        private int           _stepId        = 0;
        private int           _firedStepId   = NONE;

        private float _tickLevel = 0f; // 틱마다 1로 올라 0으로 줄어든다. 번쩍임, 틀 부풂, 끝 띠가 함께 쓴다
        private float _gainLevel = 0f;

        private RectTransform[] _sparkRects = null;
        private Image[]         _sparkImages = null;
        private Vector2[]       _sparkVelocity = null;
        private float[]         _sparkAge = null;
        private float[]         _sparkLife = null;
        private int             _sparkNext = 0;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if ((_fill == null) || (_label == null) || (_labelText == null))
            {
                Debug.LogError("[UiGaugeFollowLabel] 채움, 글자 참조가 비어 있다", this);
                enabled = false;
                return;
            }

            _fillBaseColor = _fill.color;
            _frame         = _fill.transform.parent as RectTransform;

            Apply(_fill.fillAmount);
            SetAlpha(_gainBar, 0f);
            SetAlpha(_edgeGlow, 0f);
            CreateSparks();

            // 처치마다 다시 부르므로 한 번 만들어 값만 바꿔 재사용한다
            _surgeEase = SurgeEase;
            _tween = DOTween.To(GetRatio, Apply, _ratio, _smallDuration)
                .SetEase(Ease.OutCubic)
                .SetAutoKill(false)
                .Pause()
                .SetLink(gameObject);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            UpdateTick(dt);
            UpdateGain(dt);
            UpdateSparks(dt);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 0~1 비율로 채움을 바꾼다, 현재 표시값에서 목표까지 따라 오른다 </summary>
        public void SetRatio(float ratio)
        {
            if (_tween == null)
            {
                return;
            }

            float target = Mathf.Clamp01(ratio);
            float delta  = target - _ratio;

            if (delta <= 0f)
            {
                _isSurge = false;
                _tween.SetEase(Ease.OutCubic);
                _tween.ChangeValues(_ratio, target, _smallDuration);
                _tween.Restart();
                return;
            }

            // 많이 오를 때는 여러 번 끊어 오르고, 처치마다 조금씩 오를 때는 한 번 촵 하고 오른다
            _isSurge     = (delta >= _surgeMinDelta);
            _firedStepId = NONE;
            _stepId      = 0;

            if (_isSurge)
            {
                _tween.SetEase(_surgeEase);
            }
            else
            {
                _tween.SetEase(Ease.OutQuad);
            }

            _tween.ChangeValues(_ratio, target, _isSurge ? _surgeDuration : _smallDuration);

            if (_gainBar != null)
            {
                _gainBar.fillAmount = target;
                SetAlpha(_gainBar, _gainStartAlpha);
                _gainLevel = 1f;
            }

            _tween.Restart();

            // 끊어 오르는 쪽은 Apply가 칸이 바뀔 때마다 부른다, 한 번 촵 하는 쪽은 여기서 한 번 부른다
            if (!_isSurge)
            {
                Tick();
            }
        }

        /// <summary> 연출 없이 바로 바꾼다, 재접속 복구처럼 지난 값을 보여 줄 때 쓴다 </summary>
        public void SetRatioImmediate(float ratio)
        {
            if (_tween != null)
            {
                _tween.Pause();
            }

            _isSurge   = false;
            _tickLevel = 0f;
            _gainLevel = 0f;
            SetAlpha(_gainBar, 0f);
            Apply(ratio);
        }

        /// <summary> 채움 색을 바꾼다, 번쩍임이 되돌아올 기준색도 함께 바뀐다 </summary>
        public void SetFillColor(Color color)
        {
            if (_fill == null)
            {
                return;
            }

            _fillBaseColor = color;
            _fill.color    = color;
        }

        private float GetRatio() { return _ratio; }

        // 한 칸 오를 때마다 번쩍이고, 틀이 부풀고, 끝에서 불꽃이 튄다
        private void Tick()
        {
            _tickLevel = 1f;
            SpawnSparks();
        }

        private void UpdateTick(float dt)
        {
            if (_tickLevel <= 0f)
            {
                return;
            }

            _tickLevel = Mathf.Max(0f, _tickLevel - (dt / Mathf.Max(MIN_FADE_TIME, _tickFadeDuration)));

            // 채움 색을 하얗게 밝혔다가 돌린다
            _fill.color = Color.Lerp(_fillBaseColor, Color.white, _tickLevel * _flashStrength);

            if (_frame != null)
            {
                _frame.localScale = new Vector3(1f, 1f + (_tickLevel * _framePunch), 1f);
            }

            _label.localScale = Vector3.one * (1f + (_tickLevel * LABEL_POP));

            if (_edgeGlow != null)
            {
                SetAlpha(_edgeGlow, _tickLevel);
                _edgeGlow.rectTransform.localScale = new Vector3(1f + (_tickLevel * EDGE_STRETCH), 1f + (_tickLevel * EDGE_STRETCH * HOP_RISE_RATIO), 1f);
            }
        }

        // 오르는 동안은 목표까지 미리 깔린 밝은 띠를 유지하고, 다 오르면 서서히 지운다
        private void UpdateGain(float dt)
        {
            if ((_gainBar == null) || (_gainLevel <= 0f) || (_tween == null) || _tween.IsPlaying())
            {
                return;
            }

            _gainLevel = Mathf.Max(0f, _gainLevel - (dt / Mathf.Max(MIN_FADE_TIME, _gainFadeDuration)));
            SetAlpha(_gainBar, _gainLevel * _gainStartAlpha);
        }

        private void CreateSparks()
        {
            if (_sparkCount <= 0)
            {
                return;
            }

            _sparkRects    = new RectTransform[_sparkCount];
            _sparkImages   = new Image[_sparkCount];
            _sparkVelocity = new Vector2[_sparkCount];
            _sparkAge      = new float[_sparkCount];
            _sparkLife     = new float[_sparkCount];

            for (int i = 0; i < _sparkCount; ++i)
            {
                GameObject go = new GameObject("Spark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.layer = _fill.gameObject.layer;
                go.transform.SetParent(_fill.transform, false);

                Image image = go.GetComponent<Image>();
                image.raycastTarget = false;
                image.color         = new Color(_sparkColor.r, _sparkColor.g, _sparkColor.b, 0f);
                go.SetActive(false);

                _sparkRects[i]  = (RectTransform)go.transform;
                _sparkImages[i] = image;
                _sparkLife[i]   = 1f;
                _sparkAge[i]    = 1f;
            }
        }

        private void SpawnSparks()
        {
            if (_sparkRects == null)
            {
                return;
            }

            Vector2 anchor = new Vector2(_ratio, ANCHOR_MIDDLE);

            for (int n = 0; n < _sparksPerTick; ++n)
            {
                int i = _sparkNext;
                _sparkNext = (_sparkNext + 1) % _sparkRects.Length;

                RectTransform rect = _sparkRects[i];
                float size = Random.Range(SPARK_SIZE_MIN, SPARK_SIZE_MAX);
                rect.anchorMin        = anchor;
                rect.anchorMax        = anchor;
                rect.sizeDelta        = new Vector2(size, size);
                rect.anchoredPosition = new Vector2(0f, Random.Range(-SPARK_JITTER_Y, SPARK_JITTER_Y));

                float angle = Random.Range(SPARK_ANGLE_MIN, SPARK_ANGLE_MAX) * Mathf.Deg2Rad;
                float speed = Random.Range(SPARK_SPEED_MIN, SPARK_SPEED_MAX);
                _sparkVelocity[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
                _sparkAge[i]      = 0f;
                _sparkLife[i]     = Random.Range(SPARK_LIFE_MIN, SPARK_LIFE_MAX);

                _sparkImages[i].color = _sparkColor;
                rect.gameObject.SetActive(true);
            }
        }

        private void UpdateSparks(float dt)
        {
            if (_sparkRects == null)
            {
                return;
            }

            for (int i = 0; i < _sparkRects.Length; ++i)
            {
                if (_sparkAge[i] >= _sparkLife[i])
                {
                    continue;
                }

                _sparkAge[i] += dt;

                if (_sparkAge[i] >= _sparkLife[i])
                {
                    _sparkRects[i].gameObject.SetActive(false);
                    continue;
                }

                _sparkVelocity[i].y -= SPARK_GRAVITY * dt;
                _sparkRects[i].anchoredPosition += _sparkVelocity[i] * dt;

                float life = 1f - (_sparkAge[i] / _sparkLife[i]);
                Color color = _sparkColor;
                color.a = life;
                _sparkImages[i].color = color;
            }
        }

        private static void SetAlpha(Image image, float alpha)
        {
            if (image == null)
            {
                return;
            }

            Color color = image.color;
            color.a     = alpha;
            image.color = color;
        }

        /// <summary> 앞은 몇 번 끊어 짧게 오르고(촵촵), 뒤는 잘게 끊어 몰아 오른다(촤자자작). 시작 0, 끝 1 </summary>
        private float SurgeEase(float time, float duration, float overshoot, float period)
        {
            float t       = (duration > 0f) ? Mathf.Clamp01(time / duration) : 1f;
            int   hops    = Mathf.Max(1, _hopCount);
            float hopTime = _hopTimeRatio;

            if (t < hopTime)
            {
                float span  = hopTime / hops;
                int   index = Mathf.Min(hops - 1, (int)(t / span));
                float local = (t - (index * span)) / span;
                float rise  = Mathf.Clamp01(local / HOP_RISE_RATIO);
                rise = 1f - Mathf.Pow(1f - rise, EASE_POWER);

                _stepId = index;
                return ((index + rise) / hops) * _hopHeightRatio;
            }

            int   steps = Mathf.Max(1, _sweepSteps);
            float sweep = ((t - hopTime) / (1f - hopTime)) * steps;
            int   step  = Mathf.Min(steps - 1, (int)sweep);
            float rise2 = Mathf.Clamp01((sweep - step) / SWEEP_RISE_RATIO);
            float value = (step + rise2) / steps;

            // 몰아 오르다 끝에서 속도가 줄어 자연스럽게 멈춘다
            value = 1f - Mathf.Pow(1f - value, EASE_POWER);

            _stepId = hops + step;
            return _hopHeightRatio + ((1f - _hopHeightRatio) * value);
        }

        private void Apply(float ratio)
        {
            if ((_fill == null) || (_label == null) || (_labelText == null))
            {
                return;
            }

            _ratio           = Mathf.Clamp01(ratio);
            _fill.fillAmount = _ratio;

            // 끊어 오르는 칸이 바뀌면 한 번 번쩍인다
            if (_isSurge && (_stepId != _firedStepId))
            {
                _firedStepId = _stepId;
                Tick();
            }

            bool    isInside = _ratio >= _insideFrom;
            Vector2 anchor   = new Vector2(_ratio, ANCHOR_MIDDLE);

            if (_edgeGlow != null)
            {
                RectTransform edge = _edgeGlow.rectTransform;
                edge.anchorMin        = anchor;
                edge.anchorMax        = anchor;
                edge.anchoredPosition = Vector2.zero;
            }

            _label.anchorMin        = anchor;
            _label.anchorMax        = anchor;
            _label.pivot            = new Vector2(isInside ? PIVOT_RIGHT : PIVOT_LEFT, ANCHOR_MIDDLE);
            _label.anchoredPosition = new Vector2(isInside ? -_gap : _gap, 0f);
            _labelText.alignment    = isInside ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;

            // 트윈이 매 프레임 부르므로 정수 퍼센트가 바뀔 때만 글자를 다시 만든다
            int percent = Mathf.RoundToInt(_ratio * PERCENT_SCALE);
            if (percent == _percent)
            {
                return;
            }

            _percent = percent;
            _labelText.SetText(PERCENT_FORMAT, percent);
        }
    }
}
