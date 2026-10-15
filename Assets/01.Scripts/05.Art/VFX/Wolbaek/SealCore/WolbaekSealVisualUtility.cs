// System
using System;

// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;
using DG.Tweening.Core;

namespace Shinshi.Art
{
    /// <summary> 독립 봉인핵과 낙하 표현의 유한수 검사, 절대 시각 트윈과 풀 반환용 캐시 </summary>
    internal static class WolbaekSealVisualUtility
    {
        /****************************************
        *                Fields
        ****************************************/
        internal const float MIN_SIZE            = 0.001f;
        internal const float ENVELOPE_SECONDS    = 1f;
        internal const float TAU                 = Mathf.PI * 2f;
        internal const float FULL_TURN           = 360f;
        private const float MAX_VISUAL_VALUE     = 10000f;
        private static readonly int _colorId     = Shader.PropertyToID("_Color");
        private static readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int _baseMapStId = Shader.PropertyToID("_BaseMap_ST");

        /****************************************
        *                Types
        ****************************************/
        internal readonly struct Pose
        {
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public Vector3 Scale { get; }

            public Pose(Transform target)
            {
                Position = target.localPosition;
                Rotation = target.localRotation;
                Scale    = target.localScale;
            }

            public void Restore(Transform target)
            {
                if (target == null)
                {
                    return;
                }
                target.SetLocalPositionAndRotation(Position, Rotation);
                target.localScale = Scale;
            }
        }

        internal sealed class RendererState
        {
            private readonly Renderer _renderer;
            private readonly MaterialPropertyBlock _original;
            private readonly MaterialPropertyBlock _working;
            private readonly int _colorProperty;
            private readonly Vector4 _mapST;
            public Color BaseColor { get; }
            public Renderer Target => _renderer;

            public RendererState(Renderer renderer)
            {
                _renderer = renderer;
                _original = new MaterialPropertyBlock();
                _working  = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(_original);
                renderer.GetPropertyBlock(_working);
                Material material = renderer.sharedMaterial;
                _colorProperty = material.HasProperty(_baseColorId) ? _baseColorId : _colorId;
                BaseColor = _original.HasColor(_colorProperty)
                    ? _original.GetColor(_colorProperty) : material.GetColor(_colorProperty);
                _mapST = _original.HasVector(_baseMapStId) ? _original.GetVector(_baseMapStId)
                    : material.HasProperty(_baseMapStId) ? material.GetVector(_baseMapStId) : new Vector4(1f, 1f, 0f, 0f);
            }

            public void Paint(Color color, float opacity, float flow = 0f)
            {
                if (_renderer == null)
                {
                    return;
                }
                color.a *= opacity;
                _renderer.enabled = color.a > MIN_SIZE;
                _working.SetColor(_colorProperty, color);
                _working.SetVector(_baseMapStId, new Vector4(_mapST.x, _mapST.y, _mapST.z, _mapST.w + flow));
                _renderer.SetPropertyBlock(_working);
            }

            public void Restore()
            {
                if (_renderer == null)
                {
                    return;
                }
                _renderer.SetPropertyBlock(_original.isEmpty ? null : _original);
                _renderer.enabled = false;
            }
        }

        internal sealed class InputOrder
        {
            private ulong _sequence;
            private double _snapshotAt;
            private double _now;
            private bool _hasInput;
            public double LastNow => _now;

            public bool Accepts(ulong sequence, double snapshotAt, double now)
            {
                // Sequence는 외부 전달 창구가 부여하는 단조 증가 값이다. opaque CastId를 순서로 비교하지 않는다
                return (!_hasInput) || (((sequence > _sequence) && (snapshotAt >= _snapshotAt)) && (now >= _now));
            }

            public void Accept(ulong sequence, double snapshotAt, double now)
            {
                _sequence   = sequence;
                _snapshotAt = snapshotAt;
                _now        = now;
                _hasInput   = true;
            }

            public void Tick(double now)
            {
                _now = now;
            }
        }

        internal sealed class UnitHierarchy
        {
            private readonly Transform[] _chain;

            public UnitHierarchy(Transform root)
            {
                int count = 0;
                for (Transform item = root; item != null; item = item.parent)
                {
                    ++count;
                }
                _chain = new Transform[count];
                Transform current = root;
                for (int i = 0; i < count; ++i)
                {
                    _chain[i] = current;
                    current = current.parent;
                }
            }

            public bool IsUnchanged()
            {
                for (int i = 0; i < _chain.Length; ++i)
                {
                    Transform item = _chain[i];
                    Transform parent = i + 1 < _chain.Length ? _chain[i + 1] : null;
                    if (((item == null) || (!item.localScale.Equals(Vector3.one))) || (item.parent != parent))
                    {
                        return false;
                    }
                }
                return true;
            }

            public static bool IsUnit(Transform root)
            {
                for (Transform item = root; item != null; item = item.parent)
                {
                    if (!item.localScale.Equals(Vector3.one))
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        /****************************************
        *                Methods
        ****************************************/
        internal static Tweener Envelope(GameObject owner, DOGetter<float> getter, DOSetter<float> setter, Ease ease)
        {
            // 자동 시계와 완료 콜백을 끄고 외부 절대 시각에서 캐시한 트윈을 샘플링한다
            return DOTween.To(getter, setter, ENVELOPE_SECONDS, ENVELOPE_SECONDS)
                .SetEase(ease).SetUpdate(UpdateType.Manual).SetAutoKill(false)
                .SetLink(owner, LinkBehaviour.KillOnDestroy).Pause();
        }

        internal static bool Finite(double value) => (!double.IsNaN(value)) && (!double.IsInfinity(value));
        internal static bool Positive(float value) => ((Finite(value)) && (value >= MIN_SIZE)) && (value <= MAX_VISUAL_VALUE);
        internal static bool NonNegative(float value) => ((Finite(value)) && (value >= 0f)) && (value <= MAX_VISUAL_VALUE);
        internal static bool Unit(float value) => (NonNegative(value)) && (value <= 1f);
        internal static bool Valid(Vector3 value) => ((Finite(value.x)) && (Finite(value.y))) && (Finite(value.z));
        internal static bool Valid(Color value) => (((NonNegative(value.r)) && (NonNegative(value.g)))
            && (NonNegative(value.b))) && (Unit(value.a));

        internal static bool ValidPose(Transform target)
        {
            if (target == null)
            {
                return false;
            }
            Vector3 position = target.localPosition;
            Quaternion rotation = target.localRotation;
            Vector3 scale = target.localScale;
            return ((((((((((Valid(position)) && (Math.Abs(position.x) <= MAX_VISUAL_VALUE))
                && (Math.Abs(position.y) <= MAX_VISUAL_VALUE)) && (Math.Abs(position.z) <= MAX_VISUAL_VALUE))
                && (Positive(scale.x))) && (Positive(scale.y))) && (Positive(scale.z)))
                && (Finite(rotation.x))) && (Finite(rotation.y))) && (Finite(rotation.z))) && (Finite(rotation.w));
        }

        internal static bool ValidRenderer(Renderer renderer, Transform parent, bool validatePose = true)
        {
            if (((renderer == null) || (renderer.transform.parent != parent))
                || ((validatePose) && (!ValidPose(renderer.transform))))
            {
                return false;
            }
            Material material = renderer.sharedMaterial;
            return (material != null) && ((material.HasProperty(_colorId)) || (material.HasProperty(_baseColorId)));
        }

        internal static void Hide(Renderer renderer)
        {
            if (renderer != null)
            {
                renderer.enabled = false;
            }
        }

        internal static void RestorePose(Pose pose, Renderer renderer)
        {
            if (renderer != null)
            {
                pose.Restore(renderer.transform);
            }
        }

        internal static float Normalized(double age, double seconds)
            => (float)Math.Max(0d, Math.Min(ENVELOPE_SECONDS, age / seconds));
        internal static float Phase(double age, float cycle) => (float)(age % cycle / cycle) * TAU;
        internal static float Turns(double age, float cycle, float turns)
        {
            if (turns <= 0f)
            {
                return 0f;
            }
            double period = cycle / (double)turns;
            return (float)(age % period / period) * FULL_TURN;
        }
    }
}
