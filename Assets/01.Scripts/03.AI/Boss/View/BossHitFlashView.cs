// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 월백 몸체는 Standard 계열이라 _Color, 무철 몸체는 URP Lit이라 _BaseColor를 쓴다. 렌더러마다 있는 쪽을 골라 같은 컴포넌트로 둘 다 처리한다
    // 두 모델 모두 _EMISSION 키워드가 꺼져 있고 기본 색이 이미 흰색이라, 발광이나 흰색 섞기 대신 텍스처에 곱해지는 색을 밝게 키워 포화시킨다
    // 원본 재질은 건드리지 않고 프로퍼티 블록만 얹었다가 지운다
    // 스킨드 메시 렌더러만 대상이라 월백 프리팹에 섞인 워프 프록시와 파티클 VFX는 하얘지지 않는다
    /// <summary> 피격 때 보스 몸체를 짧게 흰색으로 번쩍이게 하는 표현, 보스 외형 프리팹 루트에 붙여 쓴다 </summary>
    public sealed class BossHitFlashView : ClientView
    {
        /****************************************
        *              Constants
        ****************************************/

        private static readonly int G_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int G_ColorId     = Shader.PropertyToID("_Color");

        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("번쩍임이 유지되는 시간(초), 연속 피격은 시간만 연장한다")]
        [SerializeField, Min(0.01f)] private float _flashSeconds = 0.15f;

        [Tooltip("번쩍임이 시작되는 순간의 세기 비율, 1이면 밝기 배율을 전부 적용한다")]
        [SerializeField, Range(0f, 1f)] private float _peak = 1f;

        [Tooltip("번쩍일 때 텍스처에 곱하는 밝기 배율, 클수록 어두운 부위도 하얗게 포화된다")]
        [SerializeField, Min(1f)] private float _gain = 8f;

        private MaterialPropertyBlock _block;
        private WolbaekDissolveView _dissolve;
        private SkinnedMeshRenderer[] _renderers;
        private int[]   _propertyIds;
        private Color[] _originals;
        private float   _flashUntil;
        private float   _lastHealth;
        private bool    _hasHealth;
        private bool    _applied;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsFlashing => Time.time < _flashUntil;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _block       = new MaterialPropertyBlock();
            _dissolve    = GetComponentInParent<WolbaekDissolveView>();
            _renderers   = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            _propertyIds = new int[_renderers.Length];
            _originals   = new Color[_renderers.Length];
        }

        // 보스 외형이 실행 중에 Standard 재질을 URP Lit으로 바꾸므로 프로퍼티와 원래 색은 번쩍이기 직전의 재질에서 읽는다
        private void CaptureOriginals()
        {
            for (int i = 0; i < _renderers.Length; ++i)
            {
                Material material = _renderers[i].sharedMaterial;
                if (material == null)
                {
                    _propertyIds[i] = -1;
                    continue;
                }

                int id = material.HasProperty(G_BaseColorId) ? G_BaseColorId
                       : material.HasProperty(G_ColorId)     ? G_ColorId
                       : -1;
                _propertyIds[i] = id;
                _originals[i]   = (id >= 0) ? material.GetColor(id) : Color.white;
            }
        }

        private void Update()
        {
            if (_applied && !IsFlashing)
            {
                Clear();
                return;
            }

            if (IsFlashing)
            {
                Apply();
            }
        }

        private void OnDisable()
        {
            Clear();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 보스 체력을 매 프레임 알려 주면 줄어든 순간 번쩍인다, 첫 값은 기준으로만 삼는다 </summary>
        public void Observe(float health)
        {
            if (_hasHealth && (health < _lastHealth))
            {
                Play();
            }

            _lastHealth = health;
            _hasHealth  = true;
        }

        /// <summary> 번쩍임을 시작한다, 이미 번쩍이는 중이면 끝 시각만 늦춘다 </summary>
        public void Play()
        {
            // 디졸브가 재질을 통째로 바꾸는 동안에는 번쩍임을 얹지 않는다
            if ((_dissolve != null) && (_dissolve.Stage >= WolbaekDeathTimeline.EStage.Dissolving))
            {
                return;
            }

            if (!_applied)
            {
                CaptureOriginals();
            }

            _flashUntil = Time.time + _flashSeconds;
        }

        // 시작에서 바로 최대로 치솟고 끝으로 갈수록 원래 색으로 돌아온다
        private void Apply()
        {
            float remain = Mathf.Clamp01((_flashUntil - Time.time) / _flashSeconds);
            float blend  = remain * _peak;

            for (int i = 0; i < _renderers.Length; ++i)
            {
                if (_propertyIds[i] < 0)
                {
                    continue;
                }

                _renderers[i].GetPropertyBlock(_block);
                // 기본 색이 이미 흰색이라 흰색으로 섞어서는 변하지 않는다. 텍스처에 곱해지는 색을 밝게 키워 포화시킨다
                Color original = _originals[i];
                Color boosted  = new Color(original.r * _gain, original.g * _gain, original.b * _gain, original.a);
                _block.SetColor(_propertyIds[i], Color.Lerp(original, boosted, blend));
                _renderers[i].SetPropertyBlock(_block);
            }

            _applied = true;
        }

        private void Clear()
        {
            if (!_applied)
            {
                return;
            }

            for (int i = 0; i < _renderers.Length; ++i)
            {
                if (_propertyIds[i] >= 0)
                {
                    _renderers[i].SetPropertyBlock(null);
                }
            }

            _applied = false;
        }
    }
}
