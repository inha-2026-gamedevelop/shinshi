// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary>커서를 올리면 불가 커서를 띄우는 대상. 자신과 자식의 충돌체를 등록한다</summary>
    public sealed class UiCursorBlockedTarget : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        // 커서가 매 프레임 충돌체마다 GetComponent를 부르지 않도록 켜진 대상의 충돌체를 한곳에 모아 둔다
        private static readonly HashSet<Collider> G_Colliders = new HashSet<Collider>();

        private Collider[] _colliders = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        private void OnEnable()
        {
            for (int i = 0; i < _colliders.Length; ++i)
            {
                G_Colliders.Add(_colliders[i]);
            }
        }

        private void OnDisable()
        {
            for (int i = 0; i < _colliders.Length; ++i)
            {
                G_Colliders.Remove(_colliders[i]);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public static bool Contains(Collider collider) { return G_Colliders.Contains(collider); }
    }
}
