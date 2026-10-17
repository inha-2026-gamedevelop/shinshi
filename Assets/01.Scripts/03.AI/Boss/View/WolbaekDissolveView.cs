// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 디졸브 컴포넌트(INab Studio Interactive Dissolve)는 외부 에셋(99.ThirdParty)이라 이 어셈블리가 직접 참조할 수 없다
    // 그래서 형식을 모르는 MonoBehaviour로 받고 SendMessage로 공개 메서드를 부른다(시험 씬 DeathSequencePreview와 같은 방식)
    // 컨트롤러가 비어 있으면 단계 시간만 흐르고 화면은 바뀌지 않는다
    /// <summary> 처치 모션이 끝나고 정지한 뒤 디졸브를 시작하는 표현, 월백 정식 프리팹에 컨트롤러를 연결해 쓴다 </summary>
    public sealed class WolbaekDissolveView : ClientView
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string PLAY_EFFECT_METHOD = "PlayEffect";
        private const string SET_MASK_METHOD    = "UpdateMaskTransform";
        private const string MASK_POSITION      = "_PositionVector";
        private const string MASK_SCALE         = "_ScaleVector";
        private const string MASK_UP            = "_UpVector";
        private const float MASK_START_HEIGHT   = 12f;
        private const float MASK_END_HEIGHT     = -0.5f;
        private const float CURVE_STEP          = 0.02f;
        // DeathSequencePreview가 연결한 첫 InteractiveEffect의 3초 effectCurve를 0.02 간격으로 읽은 값이다.
        private static readonly float[] G_MaskCurve =
        {
            0f, 0.416f, 0.6241191f, 0.62505674f, 0.6268942f, 0.62959176f, 0.63311005f, 0.63740945f,
            0.6424504f, 0.64819336f, 0.6545988f, 0.66162705f, 0.66923875f, 0.6773942f, 0.686054f,
            0.69517845f, 0.70472807f, 0.7146633f, 0.7249446f, 0.7355324f, 0.7463872f, 0.75746936f,
            0.7687394f, 0.78015774f, 0.79168487f, 0.8032812f, 0.81490713f, 0.82652324f, 0.8380899f,
            0.84956753f, 0.8609167f, 0.8720977f, 0.88307106f, 0.89379734f, 0.9042368f, 0.91435f,
            0.92409736f, 0.9334393f, 0.9423364f, 0.9507489f, 0.9586374f, 0.9659624f, 0.9726842f,
            0.97876334f, 0.98416024f, 0.98883533f, 0.9927491f, 0.99586207f, 0.99813455f, 0.99952704f, 1f
        };

        /****************************************
        *                Fields
        ****************************************/

        [Tooltip("디졸브 컨트롤러와 불꽃 컨트롤러 전부, 처치 때 PlayEffect를 한꺼번에 부른다")]
        [SerializeField] private MonoBehaviour[] _controllers;

        [Tooltip("시험 씬에서 복사한 Burn 재질. INab 컨트롤러를 연결할 수 없을 때 마스크 평면으로 소멸을 그린다")]
        [SerializeField] private Material[] _burnMaterials;

        private readonly WolbaekDeathTimeline _timeline = new WolbaekDeathTimeline();
        private readonly List<Material> _runtimeBurn = new List<Material>();
        private Renderer[] _renderers;
        private Material[][] _originalMaterials;
        private Material[][] _dissolveMaterials;
        private bool _usingBurn;

        /****************************************
        *              Properties
        ****************************************/

        public WolbaekDeathTimeline.EStage Stage => _timeline.Stage;

        public bool HasControllers => (_controllers != null) && (_controllers.Length > 0);

        protected override void OnViewAwake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void OnDestroy()
        {
            RestoreMaterials();
            for (int i = 0; i < _runtimeBurn.Count; ++i)
            {
                if (_runtimeBurn[i] != null)
                {
                    Destroy(_runtimeBurn[i]);
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 처치 연출을 시작한다. isLate가 참이면 재접속으로 이미 쓰러진 보스라 정지부터, 처치 뒤 지난 시간을 알면 그 지점부터 시작한다 </summary>
        public void Begin(bool isLate, float elapsedSinceDefeat = WolbaekDeathTimeline.UNKNOWN_ELAPSED)
        {
            InstallBurnMaterials();
            SetMask(0f);
            _timeline.Begin(isLate, elapsedSinceDefeat);
        }

        /// <summary> 처치 연출을 끊고 소멸 진행을 되돌린다, 보스가 다시 일어서거나 전투를 다시 시작할 때 쓴다 </summary>
        public void ResetDeath()
        {
            _timeline.Reset();
            SetMask(0f);
            RestoreMaterials();
        }

        /// <summary> 매 프레임 부른다. isFallPlaying은 처치 클립 상태가 재생 중인가다 </summary>
        public void Tick(float deltaTime, bool isFallPlaying)
        {
            if (_timeline.Tick(deltaTime, isFallPlaying))
            {
                PlayEffect();
            }

            if (_usingBurn && (_timeline.Stage == WolbaekDeathTimeline.EStage.Dissolving
                || _timeline.Stage == WolbaekDeathTimeline.EStage.Dissolved))
            {
                SetMask(SampleMaskCurve(_timeline.DissolveProgress));
            }
        }

        private void PlayEffect()
        {
            for (int i = 0; (_controllers != null) && (i < _controllers.Length); ++i)
            {
                if (_controllers[i] != null)
                {
                    _controllers[i].SendMessage(PLAY_EFFECT_METHOD, SendMessageOptions.DontRequireReceiver);
                }
            }
        }

        private void SetMask(float lerp)
        {
            for (int i = 0; (_controllers != null) && (i < _controllers.Length); ++i)
            {
                if (_controllers[i] != null)
                {
                    _controllers[i].SendMessage(SET_MASK_METHOD, lerp, SendMessageOptions.DontRequireReceiver);
                }
            }

            if (!_usingBurn)
            {
                return;
            }

            // 시험 씬의 Plane 마스크가 모델 위 12m에서 아래 0.5m까지 내려오는 계약이다.
            Vector3 position = transform.TransformPoint(0f, Mathf.Lerp(MASK_START_HEIGHT, MASK_END_HEIGHT, lerp), 0f);
            Vector3 scale = transform.lossyScale;
            Vector3 up = transform.up;
            for (int i = 0; i < _runtimeBurn.Count; ++i)
            {
                Material material = _runtimeBurn[i];
                material.SetVector(MASK_POSITION, position);
                material.SetVector(MASK_SCALE, scale);
                material.SetVector(MASK_UP, up);
            }
        }

        private static float SampleMaskCurve(float progress)
        {
            float sample = Mathf.Clamp01(progress) / CURVE_STEP;
            int index = Mathf.Min((int)sample, G_MaskCurve.Length - 2);
            return Mathf.Lerp(G_MaskCurve[index], G_MaskCurve[index + 1], sample - index);
        }

        private void InstallBurnMaterials()
        {
            if (_usingBurn || (_renderers == null) || (_burnMaterials == null) || (_burnMaterials.Length == 0))
            {
                return;
            }

            _originalMaterials = new Material[_renderers.Length][];
            _dissolveMaterials = new Material[_renderers.Length][];
            for (int i = 0; i < _renderers.Length; ++i)
            {
                Renderer renderer = _renderers[i];
                Material[] originals = renderer.sharedMaterials;
                Material[] replacements = (Material[])originals.Clone();
                bool changed = false;
                for (int slot = 0; slot < originals.Length; ++slot)
                {
                    Material source = originals[slot];
                    Material burn = FindBurn(source);
                    if (burn == null)
                    {
                        continue;
                    }

                    Material instance = new Material(burn);
                    _runtimeBurn.Add(instance);
                    replacements[slot] = instance;
                    changed = true;
                }

                if (changed)
                {
                    _originalMaterials[i] = originals;
                    _dissolveMaterials[i] = replacements;
                    renderer.sharedMaterials = replacements;
                }
            }

            _usingBurn = _runtimeBurn.Count > 0;
        }

        private Material FindBurn(Material source)
        {
            if (source == null)
            {
                return null;
            }

            string sourceName = source.name.Replace("_URP", string.Empty).Replace(" (Instance)", string.Empty);
            for (int i = 0; i < _burnMaterials.Length; ++i)
            {
                Material candidate = _burnMaterials[i];
                if ((candidate != null) && (candidate.name == sourceName + "_Burn"))
                {
                    return candidate;
                }
            }

            return null;
        }

        private void RestoreMaterials()
        {
            if (!_usingBurn)
            {
                return;
            }

            for (int i = 0; i < _renderers.Length; ++i)
            {
                if (_originalMaterials[i] != null)
                {
                    _renderers[i].sharedMaterials = _originalMaterials[i];
                }
            }

            for (int i = 0; i < _runtimeBurn.Count; ++i)
            {
                Destroy(_runtimeBurn[i]);
            }
            _runtimeBurn.Clear();
            _usingBurn = false;
        }
    }
}
