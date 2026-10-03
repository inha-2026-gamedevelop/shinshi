// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 모든 얼굴 LOD에 같은 눈 깜박임과 표정 모프 값을 적용하는 클라이언트 표현 </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFemaleCharacterBlinkView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MAX_BLEND_SHAPE_WEIGHT = 100f;
        private const int   INFINITE_LOOPS         = -1;

        [SerializeField] private SkinnedMeshRenderer[] _renderers             = Array.Empty<SkinnedMeshRenderer>();
        [SerializeField] private string                _blinkBlendShape       = "Blink";
        [SerializeField] private string[]              _expressionBlendShapes = Array.Empty<string>();
        [SerializeField] private bool                  _automaticBlink        = true;
        [SerializeField] private Vector2               _blinkInterval         = new Vector2(2.5f, 4.5f);
        [SerializeField] private float                 _closeSeconds          = 0.065f;
        [SerializeField] private float                 _holdSeconds           = 0.025f;
        [SerializeField] private float                 _openSeconds           = 0.1f;

        private BlendShapeBinding[] _blinkBindings       = Array.Empty<BlendShapeBinding>();
        private ExpressionBinding[] _expressionBindings = Array.Empty<ExpressionBinding>();
        private Sequence            _blinkSequence;
        private float               _blinkWeight;
        private bool                _isBound;
        private string              _validationError = string.Empty;

        private struct BlendShapeBinding
        {
            public SkinnedMeshRenderer Renderer;
            public Mesh Mesh;
            public int Index;
            public float InitialWeight;
        }

        private struct ExpressionBinding
        {
            public string Name;
            public BlendShapeBinding[] Shapes;
        }

        /****************************************
        *              Properties
        ****************************************/

        public bool IsBound           => _isBound;
        public string ValidationError => _validationError;
        public int BlinkRendererCount => _blinkBindings.Length;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            TryBind(out _validationError);
        }

        private void OnEnable()
        {
            if ((_isBound) && (_automaticBlink) && (!IsServerBuild) && (Application.isPlaying))
            {
                StartAutomaticBlink();
            }
        }

        private void OnDisable()
        {
            StopSequence();
            RestoreBindings(_blinkBindings);
            for (int i = 0; i < _expressionBindings.Length; ++i)
            {
                RestoreBindings(_expressionBindings[i].Shapes);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 모든 얼굴 LOD의 같은 이름 모프를 연결하고 기존 연결의 원래 값을 복원한다 </summary>
        public bool Configure(SkinnedMeshRenderer[] renderers, string blinkBlendShape, string[] expressionBlendShapes)
        {
            StopSequence();
            RestoreBindings(_blinkBindings);
            for (int i = 0; i < _expressionBindings.Length; ++i)
            {
                RestoreBindings(_expressionBindings[i].Shapes);
            }

            _renderers             = renderers;
            _blinkBlendShape       = blinkBlendShape;
            _expressionBlendShapes = expressionBlendShapes;
            bool result = TryBind(out _validationError);
            if ((result) && (_automaticBlink) && (isActiveAndEnabled) && (Application.isPlaying))
            {
                StartAutomaticBlink();
            }

            return result;
        }

        /// <summary> 필수 눈 깜박임 모프가 모든 지정 LOD에 있는지 검사하고 인덱스를 캐시한다 </summary>
        public bool TryBind(out string error)
        {
            StopSequence();
            RestoreBindings(_blinkBindings);
            for (int i = 0; i < _expressionBindings.Length; ++i)
            {
                RestoreBindings(_expressionBindings[i].Shapes);
            }

            _isBound = false;
            _blinkBindings = Array.Empty<BlendShapeBinding>();
            _expressionBindings = Array.Empty<ExpressionBinding>();
            if ((IsServerBuild) || (_renderers == null) || (_renderers.Length == 0)
                || (string.IsNullOrWhiteSpace(_blinkBlendShape)) || (!HasValidTiming()))
            {
                error = "클라이언트 얼굴 렌더러와 모프 이름, 유효한 눈 깜박임 시간이 필요하다";
                _validationError = error;
                return false;
            }

            if (!TryBuildBindings(_blinkBlendShape, out BlendShapeBinding[] blinkBindings, out error))
            {
                _validationError = error;
                return false;
            }

            int expressionCount = (_expressionBlendShapes == null) ? 0 : _expressionBlendShapes.Length;
            var expressions = new ExpressionBinding[expressionCount];
            var names = new HashSet<string>(StringComparer.Ordinal) { _blinkBlendShape };
            for (int i = 0; i < expressionCount; ++i)
            {
                string name = _expressionBlendShapes[i];
                if ((string.IsNullOrWhiteSpace(name)) || (!names.Add(name)))
                {
                    error = "표정 이름은 비어 있거나 눈 깜박임과 중복될 수 없다";
                    _validationError = error;
                    return false;
                }

                if (!TryBuildBindings(name, out BlendShapeBinding[] shapes, out error))
                {
                    _validationError = error;
                    return false;
                }

                expressions[i] = new ExpressionBinding { Name = name, Shapes = shapes };
            }

            _blinkBindings      = blinkBindings;
            _expressionBindings = expressions;
            _blinkWeight        = 0f;
            _isBound            = true;
            error = string.Empty;
            _validationError = error;
            return true;
        }

        /// <summary> 정규화된 0부터 1 값으로 연결한 표정을 모든 LOD에 적용한다 </summary>
        public bool TrySetExpression(string name, float normalizedWeight)
        {
            if ((!_isBound) || (IsServerBuild) || (!SecondaryMotionMath.IsFinite(normalizedWeight)))
            {
                return false;
            }

            for (int i = 0; i < _expressionBindings.Length; ++i)
            {
                if (string.Equals(_expressionBindings[i].Name, name, StringComparison.Ordinal))
                {
                    return ApplyBindings(_expressionBindings[i].Shapes, Mathf.Clamp01(normalizedWeight));
                }
            }

            return false;
        }

        /// <summary> 수동 검토나 얼굴 포즈에 사용할 눈 감김 값을 모든 LOD에 적용한다 </summary>
        public bool TrySetBlink(float normalizedWeight)
        {
            if ((!_isBound) || (IsServerBuild) || (!SecondaryMotionMath.IsFinite(normalizedWeight)))
            {
                return false;
            }

            if (_blinkSequence != null)
            {
                _blinkSequence.Pause();
            }

            _blinkWeight = Mathf.Clamp01(normalizedWeight);
            return ApplyBindings(_blinkBindings, _blinkWeight);
        }

        /// <summary> 캐시한 시퀀스를 재생해 지금 한 번 감고 자동 반복을 이어 간다 </summary>
        public bool BlinkNow()
        {
            if ((!_isBound) || (IsServerBuild) || (!isActiveAndEnabled) || (!Application.isPlaying))
            {
                return false;
            }

            EnsureSequence();
            _blinkSequence.Restart(includeDelay: false);
            return true;
        }

        private bool TryBuildBindings(string name, out BlendShapeBinding[] bindings, out string error)
        {
            bindings = new BlendShapeBinding[_renderers.Length];
            var uniqueRenderers = new HashSet<SkinnedMeshRenderer>();
            for (int i = 0; i < _renderers.Length; ++i)
            {
                SkinnedMeshRenderer renderer = _renderers[i];
                if ((renderer == null) || (renderer.sharedMesh == null) || (!uniqueRenderers.Add(renderer)))
                {
                    error = "얼굴 LOD 렌더러가 없거나 중복됐다";
                    return false;
                }

                Mesh mesh = renderer.sharedMesh;
                int index = mesh.GetBlendShapeIndex(name);
                if (index < 0)
                {
                    error = renderer.name + "에 표정 모프가 없다: " + name;
                    return false;
                }

                bindings[i] = new BlendShapeBinding
                {
                    Renderer = renderer,
                    Mesh = mesh,
                    Index = index,
                    InitialWeight = renderer.GetBlendShapeWeight(index)
                };
            }

            error = string.Empty;
            return true;
        }

        private bool HasValidTiming()
        {
            return (SecondaryMotionMath.IsFinite(_blinkInterval.x)) && (SecondaryMotionMath.IsFinite(_blinkInterval.y))
                && (_blinkInterval.x > 0f) && (_blinkInterval.y >= _blinkInterval.x)
                && (SecondaryMotionMath.IsFinite(_closeSeconds)) && (_closeSeconds > 0f)
                && (SecondaryMotionMath.IsFinite(_holdSeconds)) && (_holdSeconds >= 0f)
                && (SecondaryMotionMath.IsFinite(_openSeconds)) && (_openSeconds > 0f);
        }

        private void StartAutomaticBlink()
        {
            _blinkWeight = 0f;
            ApplyBindings(_blinkBindings, _blinkWeight);
            EnsureSequence();
            _blinkSequence.Restart();
        }

        private void EnsureSequence()
        {
            if (_blinkSequence != null)
            {
                return;
            }

            // 반복 모프 연출은 캐시한 DOTween 시퀀스를 쓰며 비활성화할 때 명시적으로 해제한다
            float interval = UnityEngine.Random.Range(_blinkInterval.x, _blinkInterval.y);
            _blinkSequence = DOTween.Sequence();
            _blinkSequence.SetAutoKill(false).Pause();
            _blinkSequence.Append(DOTween.To(ReadBlinkWeight, ApplyBlinkWeight, 1f, _closeSeconds).SetEase(Ease.OutQuad));
            _blinkSequence.AppendInterval(_holdSeconds);
            _blinkSequence.Append(DOTween.To(ReadClosedBlinkWeight, ApplyBlinkWeight, 0f, _openSeconds).SetEase(Ease.InOutSine));
            _blinkSequence.AppendInterval(interval);
            _blinkSequence.SetDelay(interval).SetLoops(INFINITE_LOOPS);
        }

        private float ReadBlinkWeight() { return _blinkWeight; }
        private float ReadClosedBlinkWeight() { return 1f; }

        private void ApplyBlinkWeight(float weight)
        {
            _blinkWeight = weight;
            ApplyBindings(_blinkBindings, weight);
        }

        private static bool ApplyBindings(BlendShapeBinding[] bindings, float normalizedWeight)
        {
            for (int i = 0; i < bindings.Length; ++i)
            {
                if ((bindings[i].Renderer == null) || (bindings[i].Renderer.sharedMesh != bindings[i].Mesh))
                {
                    return false;
                }
            }

            for (int i = 0; i < bindings.Length; ++i)
            {
                bindings[i].Renderer.SetBlendShapeWeight(bindings[i].Index, normalizedWeight * MAX_BLEND_SHAPE_WEIGHT);
            }

            return true;
        }

        private static void RestoreBindings(BlendShapeBinding[] bindings)
        {
            for (int i = 0; i < bindings.Length; ++i)
            {
                if ((bindings[i].Renderer != null) && (bindings[i].Renderer.sharedMesh == bindings[i].Mesh))
                {
                    bindings[i].Renderer.SetBlendShapeWeight(bindings[i].Index, bindings[i].InitialWeight);
                }
            }
        }

        private void StopSequence()
        {
            if (_blinkSequence != null)
            {
                _blinkSequence.Kill();
                _blinkSequence = null;
            }
        }
    }
}
