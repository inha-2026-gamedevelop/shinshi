// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Art
{
    /// <summary> P09 프리팹을 만들어 외형을 적용하고 애니메이터를 연결한다 </summary>
    public static class PlayerCharacterLookAssembler
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ALIVE_PARAMETER = "Alive";

        private static readonly int _aliveHash = Animator.StringToHash(ALIVE_PARAMETER);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 성공하면 parent 아래에 활성화된 결과를 돌려준다. 실패하면 만든 것을 지우고 거짓과 오류를 돌려준다 </summary>
        public static bool TryAssemble(PlayerCharacterLookCatalogSO catalog, CharacterP09Look look, Transform parent,
            out GameObject result, out string error)
        {
            result = null;
            if ((catalog == null) || (catalog.Prefab == null) || (parent == null))
            {
                error = "카탈로그 프리팹과 부모 Transform이 필요하다";
                return false;
            }

            // 비활성 임시 부모 아래에서 적용을 끝내고 옮긴다. 활성 계층에 바로 만들면 기본 부위가 한 프레임 보인다
            var scratch = new GameObject(nameof(PlayerCharacterLookAssembler) + "_Scratch");
            scratch.SetActive(false);
            GameObject instance = Object.Instantiate(catalog.Prefab, scratch.transform);
            instance.name = catalog.Prefab.name;

            if (!PlayerCharacterLookApplier.TryApply(instance, catalog, look, out error))
            {
                Object.Destroy(scratch);
                return false;
            }

            Animator animator = instance.GetComponent<Animator>();
            if (animator == null)
            {
                error = "P09 프리팹 루트에 Animator가 없다";
                Object.Destroy(scratch);
                return false;
            }

            animator.runtimeAnimatorController = catalog.Controller;
            animator.applyRootMotion           = false;

            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            Object.Destroy(scratch);

            // 컨트롤러 기본값도 true지만, 바인딩 전 한 프레임이라도 Die로 들어가지 않게 활성화 직후 명시한다
            if (animator.isActiveAndEnabled && (animator.runtimeAnimatorController != null))
            {
                animator.SetBool(_aliveHash, true);
            }

            result = instance;
            error  = string.Empty;
            return true;
        }
    }
}
