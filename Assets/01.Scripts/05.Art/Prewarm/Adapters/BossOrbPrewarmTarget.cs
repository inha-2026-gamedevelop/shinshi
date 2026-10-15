// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    // 보스 여우불은 런타임에 기본 구체를 만들고 직렬화된 재질만 갈아 끼운다. 구체 메시는 내장이라 재질 셰이더가 데울 대상이다
    /// <summary> 지정한 재질마다 구체를 하나씩 만들어 그린다, 반월 연쇄 여우불 재질을 넣는다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossOrbPrewarmTarget : MonoBehaviour, IPrewarmTarget
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  ORB_SIZE    = 0.6f;
        private const float  ORB_SPACING = 1f;
        private const string ORB_NAME    = "BossOrb Prewarm";

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Material[] _materials;

        private readonly List<GameObject> _orbs = new List<GameObject>();

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

            if ((_materials == null) || (_materials.Length == 0))
            {
                Debug.LogWarning("[Prewarm] 여우불 재질이 비어 있다", this);
                return;
            }

            for (int i = 0; i < _materials.Length; ++i)
            {
                GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                orb.name = ORB_NAME;
                if (orb.TryGetComponent(out Collider collider))
                {
                    Destroy(collider);
                }

                if ((_materials[i] != null) && (orb.TryGetComponent(out Renderer renderer)))
                {
                    renderer.sharedMaterial = _materials[i];
                }

                orb.transform.SetParent(transform, false);
                orb.transform.localPosition = Vector3.right * (ORB_SPACING * i);
                orb.transform.localScale    = Vector3.one * ORB_SIZE;
                _orbs.Add(orb);
            }
        }

        public void TickPrewarm(float elapsedSeconds)
        {
        }

        public void EndPrewarm()
        {
            for (int i = 0; i < _orbs.Count; ++i)
            {
                if (_orbs[i] != null)
                {
                    Destroy(_orbs[i]);
                }
            }

            _orbs.Clear();
        }
    }
}
