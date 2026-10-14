// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;

namespace Shinshi.Art
{
    /// <summary> 고정 폭 경계와 구간별 내부 흐름을 분리한다. 원형 clipping은 표현 정점에만 적용한다 </summary>
    public sealed class WolbaekBeamLineView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int CORE = 0, VEIL = 1, GROUND = 2, PETALS = 3;
        private const float TAU = Mathf.PI * 2f;
        private const float SIDE_EDGE_THRESHOLD = 0.49f;
        private const float CAP_EDGE_THRESHOLD  = 0.01f;
        private const float CAP_END_THRESHOLD   = 0.99f;
        private const float SOURCE_EDGE_WIDTH   = 0.003f;
        private const float WORLD_EDGE_WIDTH    = 0.005f;
        private const float APPEAR_LIFE_SHARE   = 0.2f;
        private const float FADE_LIFE_SHARE     = 0.3f;

        private static readonly int G_Color = Shader.PropertyToID("_Color");
        private static readonly int G_Map   = Shader.PropertyToID("_BaseMap_ST");

        [SerializeField] private Renderer[]  _renderers;
        [SerializeField] private Transform[] _bones;
        [SerializeField] private Vector3[]   _rest;
        [SerializeField] private Vector4[]   _data;
        [SerializeField] private bool        _releaseStyle;

        private MaterialPropertyBlock _properties;
        private Mesh[]                _owned;
        private Vector3[][]           _original;
        private Vector3[][]           _clipped;
        private Vector3 _petalScale = Vector3.one;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsVisible { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        public void Configure(Renderer[] renderers, Transform[] bones, Vector3[] rest, Vector4[] data)
        {
            _renderers = renderers;
            _bones     = bones;
            _rest      = rest;
            _data      = data;
        }

        public void Initialize()
        {
            if (_properties != null)
            {
                return;
            }

            _properties = new MaterialPropertyBlock();
            _owned      = new Mesh[_renderers.Length];
            _original   = new Vector3[_renderers.Length][];
            _clipped    = new Vector3[_renderers.Length][];
            Hide();
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnDestroy()
        {
            if (_owned == null)
            {
                return;
            }

            for (int i = 0; i < _owned.Length; ++i)
            {
                if (_owned[i] != null)
                {
                    Destroy(_owned[i]);
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Hide()
        {
            IsVisible = false;
            if (_renderers != null)
            {
                for (int i = 0; i < _renderers.Length; ++i)
                {
                    _renderers[i].enabled = false;
                }
            }
        }

        public void SetGeometry(WolbaekBeamShape shape, float height)
        {
            Vector3 delta = shape.End - shape.Start;
            delta.y = 0f;
            float length = delta.magnitude;
            float coreScale = _releaseStyle ? 1f : Mathf.Clamp(0.075f / (shape.Width * 0.11f), 0.7f, 3.5f);
            _petalScale = new Vector3(Mathf.Clamp(0.04f / (shape.Width * 0.052f), 0.05f, 3.5f), 1f,
                Mathf.Clamp(0.055f / (length * 0.038f), 0.05f, 3.5f));
            _renderers[CORE].transform.localScale = new Vector3(coreScale, 1f, 1f);
            transform.SetPositionAndRotation(shape.Start, Quaternion.LookRotation(delta, Vector3.up));
            transform.localScale = new Vector3(shape.Width, height, length);
            Vector3 center = Quaternion.Inverse(transform.rotation) * (shape.ArenaCenter - shape.Start);
            for (int role = 0; role < _renderers.Length; ++role)
            {
                if ((shape.ArenaRadius <= 0f) && (_owned[role] == null) && (role != GROUND))
                {
                    continue;
                }

                if (_owned[role] == null)
                {
                    Mesh source = role == GROUND ? _renderers[role].GetComponent<MeshFilter>().sharedMesh
                        : ((SkinnedMeshRenderer)_renderers[role]).sharedMesh;
                    _original[role] = source.vertices;
                    _clipped[role]  = new Vector3[_original[role].Length];
                    _owned[role]    = Instantiate(source);
                    if (role == GROUND)
                    {
                        _renderers[role].GetComponent<MeshFilter>().sharedMesh = _owned[role];
                    }
                    else
                    {
                        ((SkinnedMeshRenderer)_renderers[role]).sharedMesh = _owned[role];
                    }
                }

                for (int vertex = 0; vertex < _original[role].Length; ++vertex)
                {
                    Vector3 p = _original[role][vertex];
                    if (role == GROUND)
                    {
                        if (Mathf.Abs(p.x) > SIDE_EDGE_THRESHOLD)
                        {
                            float edge = p.x < 0f ? -0.5f : 0.5f;
                            p.x = edge + (p.x - edge) / SOURCE_EDGE_WIDTH * WORLD_EDGE_WIDTH / shape.Width;
                        }

                        if ((p.z < CAP_EDGE_THRESHOLD) || (p.z > CAP_END_THRESHOLD))
                        {
                            float cap = p.z < 0.5f ? 0f : 1f;
                            p.z = cap + (p.z - cap) / SOURCE_EDGE_WIDTH * WORLD_EDGE_WIDTH / length;
                        }
                    }

                    if (shape.ArenaRadius > 0f)
                    {
                        float lateral = p.x * shape.Width * (role == CORE ? coreScale : 1f) - center.x;
                        float half = Mathf.Sqrt(Mathf.Max(0f, shape.ArenaRadius * shape.ArenaRadius - lateral * lateral));
                        float lower = Mathf.Clamp(center.z - half, 0f, length);
                        float upper = Mathf.Clamp(center.z + half, lower, length);
                        p.z = (lower + p.z * (upper - lower)) / length;
                    }

                    _clipped[role][vertex] = p;
                }

                _owned[role].SetVertices(_clipped[role]);
                _owned[role].RecalculateBounds();
            }
        }

        public void ShowWarning(float alpha)
        {
            Hide();
            IsVisible = true;
            _renderers[GROUND].enabled = true;
            ApplyProperties(_renderers[GROUND], new Color(0.75f, 0.77f, 1f, alpha), 0f);
        }

        public void Sample(float age, float lifetime, bool release, float appear, float fade,
            float cycle, float veil, float core)
        {
            IsVisible = true;
            float alpha = DOVirtual.EasedValue(0f, 1f,
                Mathf.Clamp01(age / Mathf.Min(appear, lifetime * APPEAR_LIFE_SHARE)), Ease.OutSine)
                * DOVirtual.EasedValue(0f, 1f,
                    Mathf.Clamp01((lifetime - age) / Mathf.Min(fade, lifetime * FADE_LIFE_SHARE)), Ease.OutSine);
            for (int i = 0; i < _renderers.Length; ++i)
            {
                _renderers[i].enabled = true;
                float strength = i == CORE ? core * (release ? 0.4f : 1f)
                    : i == VEIL ? veil : i == GROUND ? (release ? 0.7f : 0.09f) : 1f;
                ApplyProperties(_renderers[i], new Color(0.86f, 0.94f, 1f, alpha * strength),
                    i < GROUND ? age * 0.22f : 0f);
            }

            float crest = Mathf.Lerp(0.18f, 0.82f, Mathf.Clamp01(age / Mathf.Max(0.001f, lifetime)));
            for (int i = 0; i < _bones.Length; ++i)
            {
                Vector3 rest = _rest[i];
                Vector4 data = _data[i];
                float phase = age * TAU / cycle * (1f + Mathf.Sin(data.y) * 0.18f)
                    - rest.z * (5f + Mathf.Sin(data.y) * 2f) + data.y - rest.y * 3f + rest.x * 2f;
                Vector3 offset = new Vector3((Mathf.Sin(phase) + Mathf.Sin(phase * 2.73f + data.y) * 0.3f) * 0.018f,
                    (Mathf.Sin(phase * 1.47f) + Mathf.Sin(phase * 0.61f + data.y) * 0.4f) * 0.12f * data.w, 0f);
                offset.y = Mathf.Max(offset.y, 0.06f - rest.y);
                float envelope = ((release) && (data.z < 2f))
                    ? Mathf.Exp(-(rest.z - crest) * (rest.z - crest) * 35f) : 1f;
                if ((release) && (data.z == VEIL))
                {
                    offset.y += envelope * (0.25f + data.w * 0.18f);
                    envelope = 0.45f + envelope * 0.55f;
                }

                if ((release) && (data.z == CORE))
                {
                    offset.z = crest - 0.5f;
                    envelope = 1f;
                }

                if (data.z == 2f)
                {
                    float turn = age * (0.35f + data.w * 0.3f);
                    offset.y += Mathf.Sin(turn + data.y) * 0.1f;
                    _bones[i].localRotation = Quaternion.Euler(turn * 90f, turn * 63f, Mathf.Sin(phase) * 35f);
                    envelope = 0.3f + 0.7f * Mathf.Abs(Mathf.Sin(phase * 0.47f));
                }
                else
                {
                    _bones[i].localRotation = Quaternion.identity;
                }

                _bones[i].localPosition = rest + offset;
                _bones[i].localScale    = (data.z == 2f ? _petalScale : Vector3.one) * envelope;
            }
        }

        private void ApplyProperties(Renderer renderer, Color color, float flow)
        {
            _properties.Clear();
            _properties.SetColor(G_Color, color);
            _properties.SetVector(G_Map, new Vector4(1f, 1f, 0f, flow));
            renderer.SetPropertyBlock(_properties);
        }
    }
}
