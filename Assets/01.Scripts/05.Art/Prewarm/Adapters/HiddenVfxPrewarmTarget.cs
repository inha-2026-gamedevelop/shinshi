// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    // 무철, 월백 패턴 이펙트는 재생기 스크립트가 Awake나 Stop에서 렌더러를 숨겨 그대로 꺼내면 그려지지 않는다.
    // 비활성 부모 아래에 만들어 Awake가 돌기 전에 스크립트를 모두 지우고, 남은 렌더러와 파티클만 켜서 재질 셰이더를 한 번 그린다.
    // 스크립트가 재생 중에 만드는 메시나 MaterialPropertyBlock 값은 덮지 못한다
    /// <summary> 스크립트가 숨기는 이펙트 프리팹을 스크립트 없이 펼쳐 그린다 </summary>
    [DisallowMultipleComponent]
    public sealed class HiddenVfxPrewarmTarget : MonoBehaviour, IPrewarmTarget
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  SLOT_SPACING    = 2f;
        private const int    SLOTS_PER_ROW   = 6;
        private const int    MAX_PASSES      = 4;
        private const float  PARTICLE_SAMPLE = 0.05f;
        private const string HOLDER_NAME     = "HiddenVfx Prewarm";

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private GameObject[] _prefabs;

        private readonly List<GameObject> _holders = new List<GameObject>();

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnDestroy()
        {
            EndPrewarm();
        }

        /****************************************
        *                Methods
        ****************************************/

        public void BeginPrewarm()
        {
            EndPrewarm();

            if ((_prefabs == null) || (_prefabs.Length == 0))
            {
                Debug.LogWarning("[Prewarm] 숨김 이펙트 프리팹이 비어 있다", this);
                return;
            }

            for (int i = 0; i < _prefabs.Length; ++i)
            {
                if (_prefabs[i] != null)
                {
                    Spawn(_prefabs[i], i);
                }
            }
        }

        public void TickPrewarm(float elapsedSeconds)
        {
        }

        public void EndPrewarm()
        {
            for (int i = 0; i < _holders.Count; ++i)
            {
                if (_holders[i] != null)
                {
                    Destroy(_holders[i]);
                }
            }

            _holders.Clear();
        }

        private void Spawn(GameObject prefab, int index)
        {
            // 부모를 먼저 꺼 두어야 Instantiate 직후 재생기의 Awake가 돌지 않는다
            GameObject holder = new GameObject(HOLDER_NAME + " " + prefab.name);
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            holder.transform.localPosition = new Vector3((index % SLOTS_PER_ROW) * SLOT_SPACING, 0f, (index / SLOTS_PER_ROW) * SLOT_SPACING);
            _holders.Add(holder);

            GameObject instance = Instantiate(prefab, holder.transform, false);
            StripBehaviours(instance);
            ShowAll(instance);

            holder.SetActive(true);

            ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>();
            for (int i = 0; i < particles.Length; ++i)
            {
                particles[i].Simulate(PARTICLE_SAMPLE, false, true);
                particles[i].Play(false);
            }

            Animator[] animators = instance.GetComponentsInChildren<Animator>();
            for (int i = 0; i < animators.Length; ++i)
            {
                animators[i].Update(0f);
            }
        }

        // RequireComponent로 묶인 스크립트는 의존하는 쪽이 먼저 지워져야 하므로 남은 것이 없을 때까지 몇 번 돈다
        private static void StripBehaviours(GameObject instance)
        {
            for (int pass = 0; pass < MAX_PASSES; ++pass)
            {
                MonoBehaviour[] behaviours = instance.GetComponentsInChildren<MonoBehaviour>(true);
                if (behaviours.Length == 0)
                {
                    return;
                }

                for (int i = behaviours.Length - 1; i >= 0; --i)
                {
                    if (behaviours[i] != null)
                    {
                        DestroyImmediate(behaviours[i]);
                    }
                }
            }
        }

        private static void ShowAll(GameObject instance)
        {
            Transform[] nodes = instance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < nodes.Length; ++i)
            {
                nodes[i].gameObject.SetActive(true);
            }

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
                renderers[i].enabled = true;
            }

            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; ++i)
            {
                colliders[i].enabled = false;
            }

            // 워밍은 표현 전용이라 소리를 내지 않는다
            AudioSource[] sources = instance.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < sources.Length; ++i)
            {
                sources[i].enabled = false;
            }
        }
    }
}
