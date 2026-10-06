// System
using System;
using System.Collections.Generic;
using System.Linq;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Art
{
    /// <summary> 의상 직업 프리팹과 다른 직업의 헤어를 합쳐 커스터마이즈 외형 하나를 조립한다 </summary>
    public static class PlayerFemaleLookAssembler
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string HAIR_RENDERER_MARKER = "_Hair_";

        // 보조본 체인/캡슐이 헤어 소속인지 판단하는 본 이름 규칙. 실제 제작 본은 "aux:HairFrontLeft1" 형태를 쓴다
        private const string HAIR_BONE_MARKER = "Hair";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 성공하면 활성화된 결과 루트를 돌려준다, 실패하면 만든 것을 모두 지우고 거짓과 오류를 돌려준다.
        /// 실패해도 기본 헤어로 조용히 바꾸지 않는다 - 호출자가 직접 대체 여부를 정한다 </summary>
        public static bool TryAssemble(PlayerFemaleLookCatalogSO catalog, CharacterLook look, Transform parent,
            RuntimeAnimatorController controller, out GameObject result, out string error)
        {
            result = null;
            if ((catalog == null) || (parent == null))
            {
                error = "카탈로그와 부모 Transform이 필요하다";
                return false;
            }

            if ((!catalog.TryGetFace(look.FaceId, out PlayerFemaleLookCatalogSO.LookEntry face)) || (face.ClassPrefab == null)
                || (!catalog.TryGetHair(look.HairId, out PlayerFemaleLookCatalogSO.LookEntry hair)) || (hair.ClassPrefab == null)
                || (!catalog.TryGetOutfit(look.OutfitId, out PlayerFemaleLookCatalogSO.LookEntry outfit)) || (outfit.ClassPrefab == null))
            {
                error = "등록되지 않은 face/hair/outfit ID다";
                return false;
            }

            // 비활성 임시 부모 아래에서 전부 조립한 뒤 마지막에 옮긴다. 활성 계층에 바로 Instantiate하면
            // Awake/OnEnable이 헤어 교체 중간 상태(아직 보조본을 다시 연결하지 않은 상태)를 평가해 버린다
            GameObject scratchParent = new GameObject("PlayerFemaleLookAssembler_Scratch");
            scratchParent.SetActive(false);

            GameObject outfitInstance = null;
            GameObject hairDonor = null;
            try
            {
                outfitInstance = UnityEngine.Object.Instantiate(outfit.ClassPrefab, scratchParent.transform);
                outfitInstance.name = $"PlayerFemaleLook_{look.HairId}_{look.OutfitId}";

                // 같은 프리팹에서 온 헤어면 의상이 이미 그 헤어를 입고 있으므로 다시 붙이지 않는다
                if (hair.ClassPrefab != outfit.ClassPrefab)
                {
                    hairDonor = UnityEngine.Object.Instantiate(hair.ClassPrefab, scratchParent.transform);
                    if (!TrySpliceHair(outfitInstance, hairDonor, out error))
                    {
                        Cleanup(outfitInstance, hairDonor, scratchParent);
                        result = null;
                        return false;
                    }
                }

                Animator animator = outfitInstance.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    error = "조립 결과에 Animator가 없다";
                    Cleanup(outfitInstance, hairDonor, scratchParent);
                    result = null;
                    return false;
                }

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                PlayerFemaleCharacterSecondaryMotionBinding secondary =
                    outfitInstance.GetComponentInChildren<PlayerFemaleCharacterSecondaryMotionBinding>(true);
                error = string.Empty;
                if ((secondary == null) || (!secondary.TryBind(out error)))
                {
                    if (secondary == null)
                    {
                        error = "조립 결과에 보조본 바인딩이 없다";
                    }

                    Cleanup(outfitInstance, hairDonor, scratchParent);
                    result = null;
                    return false;
                }

                // 조립된 시각 인스턴스는 원본 PF의 서버 물리/전투 판정을 바꾸지 않는다. 원본 Collider가 0개여도
                // 조립 결과 전체를 공통으로 비활성화해 두어, 헤어 교체로 새로 생긴 Collider도 같은 보장을 받는다
                Collider[] colliders = outfitInstance.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Length; ++i)
                {
                    colliders[i].enabled = false;
                }

                if (hairDonor != null)
                {
                    DestroyInstance(hairDonor);
                    hairDonor = null;
                }

                outfitInstance.transform.SetParent(parent, false);
                outfitInstance.SetActive(true);
                DestroyInstance(scratchParent);
                scratchParent = null;

                result = outfitInstance;
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = $"조립 중 예외가 났다: {exception.Message}";
                Cleanup(outfitInstance, hairDonor, scratchParent);
                result = null;
                return false;
            }
        }

        private static void Cleanup(GameObject outfitInstance, GameObject hairDonor, GameObject scratchParent)
        {
            DestroyInstance(outfitInstance);
            DestroyInstance(hairDonor);
            DestroyInstance(scratchParent);
        }

        // 플레이 모드에서는 다음 프레임까지 살아남는 Destroy를, 에디터 테스트처럼 플레이 중이 아닐 때는
        // 그 자리에서 지우는 DestroyImmediate를 쓴다. 여기서 지우는 대상은 항상 이 메서드가 만든 인스턴스이고
        // 영속 에셋(프리팹 원본)은 절대 넘기지 않는다
        private static void DestroyInstance(UnityEngine.Object instance)
        {
            if (instance == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(instance);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        // donor의 _Hair_ 렌더러(LOD 0~2, outfit과 같은 개수여야 한다)를 outfit 스켈레톤의 같은 이름 뼈에 다시 연결한다.
        // outfit에 없는 뼈(헤어 전용 보조본)만 donor의 local TRS로 이식해 보존한다.
        // LODGroup의 각 단계 배열에서 옛 렌더러를 가리키던 자리를 새 렌더러로 그대로 바꿔 LOD별 렌더러 수를 지킨다
        private static bool TrySpliceHair(GameObject outfitInstance, GameObject hairDonor, out string error)
        {
            Transform outfitRoot = FindSkeletonRoot(outfitInstance.transform);
            Transform donorRoot  = FindSkeletonRoot(hairDonor.transform);
            if ((outfitRoot == null) || (donorRoot == null))
            {
                error = "의상과 헤어 양쪽에 스켈레톤 루트(Animator)가 필요하다";
                return false;
            }

            Dictionary<string, Transform> outfitBones = new Dictionary<string, Transform>(StringComparer.Ordinal);
            CollectBonesByName(outfitRoot, outfitBones);

            SkinnedMeshRenderer[] outfitHairRenderers = FindHairRenderers(outfitInstance.transform);
            SkinnedMeshRenderer[] donorHairRenderers  = FindHairRenderers(hairDonor.transform);
            if ((donorHairRenderers.Length == 0) || (outfitHairRenderers.Length != donorHairRenderers.Length))
            {
                error = "헤어 LOD 렌더러 수가 의상과 다르다";
                return false;
            }

            LODGroup lodGroup = outfitInstance.GetComponentInChildren<LODGroup>(true);
            LOD[] lods = (lodGroup != null) ? lodGroup.GetLODs() : Array.Empty<LOD>();

            HashSet<SkinnedMeshRenderer> replacedRenderers = new HashSet<SkinnedMeshRenderer>(outfitHairRenderers);
            SkinnedMeshRenderer[] newHairRenderers = new SkinnedMeshRenderer[donorHairRenderers.Length];

            for (int i = 0; i < donorHairRenderers.Length; ++i)
            {
                SkinnedMeshRenderer oldRenderer = outfitHairRenderers[i];
                SkinnedMeshRenderer donorRenderer = donorHairRenderers[i];

                Transform[] donorBones = donorRenderer.bones;
                Transform[] mappedBones = new Transform[donorBones.Length];
                for (int boneIndex = 0; boneIndex < donorBones.Length; ++boneIndex)
                {
                    Transform donorBone = donorBones[boneIndex];
                    mappedBones[boneIndex] = (donorBone == null) ? null : ResolveOrGraftBone(donorBone, outfitBones, outfitRoot);
                }

                GameObject rendererObject = UnityEngine.Object.Instantiate(donorRenderer.gameObject, oldRenderer.transform.parent);
                rendererObject.name = oldRenderer.name;
                SkinnedMeshRenderer newRenderer = rendererObject.GetComponent<SkinnedMeshRenderer>();
                newRenderer.sharedMesh = donorRenderer.sharedMesh;
                newRenderer.bones      = mappedBones;
                newRenderer.rootBone   = (donorRenderer.rootBone != null)
                    ? ResolveOrGraftBone(donorRenderer.rootBone, outfitBones, outfitRoot) : outfitRoot;
                newHairRenderers[i] = newRenderer;

                // 옛 오브젝트를 지우기 전에 LOD 배열의 참조를 전부 새 렌더러로 옮긴다
                for (int lodIndex = 0; lodIndex < lods.Length; ++lodIndex)
                {
                    Renderer[] renderers = lods[lodIndex].renderers;
                    for (int r = 0; r < renderers.Length; ++r)
                    {
                        if (renderers[r] == oldRenderer)
                        {
                            renderers[r] = newRenderer;
                        }
                    }

                    lods[lodIndex].renderers = renderers;
                }
            }

            // 보조본 재연결은 렌더러를 지우기 전에, 아직 살아 있는 옛 렌더러/본 참조로 끝낸다
            if (!TryRebindSecondaryMotion(outfitInstance, hairDonor, replacedRenderers, newHairRenderers,
                outfitBones, outfitRoot, out error))
            {
                return false;
            }

            for (int i = 0; i < outfitHairRenderers.Length; ++i)
            {
                DestroyInstance(outfitHairRenderers[i].gameObject);
            }

            if (lodGroup != null)
            {
                lodGroup.SetLODs(lods);
                lodGroup.RecalculateBounds();
            }

            error = string.Empty;
            return true;
        }

        // outfitBinding의 _renderers/_chains/_capsules를 "의상의 비헤어 항목 + 헤어 도너의 헤어 항목"으로 다시 만들어
        // Configure를 재호출한다. 그대로 두면 바인딩이 곧 파괴될 옛 헤어 렌더러와 도너 쪽에만 있던 앞머리 체인을 놓친다
        private static bool TryRebindSecondaryMotion(GameObject outfitInstance, GameObject hairDonor,
            HashSet<SkinnedMeshRenderer> replacedRenderers, SkinnedMeshRenderer[] newHairRenderers,
            Dictionary<string, Transform> outfitBones, Transform outfitRoot, out string error)
        {
            PlayerFemaleCharacterSecondaryMotionBinding outfitBinding =
                outfitInstance.GetComponentInChildren<PlayerFemaleCharacterSecondaryMotionBinding>(true);
            if (outfitBinding == null)
            {
                error = string.Empty;
                return true;
            }

            PlayerFemaleCharacterSecondaryMotionBinding donorBinding =
                hairDonor.GetComponentInChildren<PlayerFemaleCharacterSecondaryMotionBinding>(true);

            SkinnedMeshRenderer[] previousRenderers = outfitBinding.Renderers ?? Array.Empty<SkinnedMeshRenderer>();
            List<SkinnedMeshRenderer> renderers = new List<SkinnedMeshRenderer>(previousRenderers.Length + newHairRenderers.Length);
            for (int i = 0; i < previousRenderers.Length; ++i)
            {
                if ((previousRenderers[i] != null) && (!replacedRenderers.Contains(previousRenderers[i])))
                {
                    renderers.Add(previousRenderers[i]);
                }
            }

            renderers.AddRange(newHairRenderers);

            SecondaryMotionChain[] previousChains = outfitBinding.Chains ?? Array.Empty<SecondaryMotionChain>();
            List<SecondaryMotionChain> chains = new List<SecondaryMotionChain>(previousChains.Length);
            for (int i = 0; i < previousChains.Length; ++i)
            {
                if (!IsHairRelatedChain(previousChains[i]))
                {
                    chains.Add(previousChains[i]);
                }
            }

            SecondaryMotionCapsule[] previousCapsules = outfitBinding.Capsules ?? Array.Empty<SecondaryMotionCapsule>();
            List<SecondaryMotionCapsule> capsules = new List<SecondaryMotionCapsule>(previousCapsules.Length);
            for (int i = 0; i < previousCapsules.Length; ++i)
            {
                if (!IsHairRelatedCapsule(previousCapsules[i]))
                {
                    capsules.Add(previousCapsules[i]);
                }
            }

            if (donorBinding != null)
            {
                SecondaryMotionChain[] donorChains = donorBinding.Chains ?? Array.Empty<SecondaryMotionChain>();
                for (int i = 0; i < donorChains.Length; ++i)
                {
                    if (IsHairRelatedChain(donorChains[i]))
                    {
                        chains.Add(RemapChain(donorChains[i], outfitBones, outfitRoot));
                    }
                }

                SecondaryMotionCapsule[] donorCapsules = donorBinding.Capsules ?? Array.Empty<SecondaryMotionCapsule>();
                for (int i = 0; i < donorCapsules.Length; ++i)
                {
                    if (IsHairRelatedCapsule(donorCapsules[i]))
                    {
                        capsules.Add(RemapCapsule(donorCapsules[i], outfitBones, outfitRoot));
                    }
                }
            }

            Animator animator = outfitInstance.GetComponentInChildren<Animator>(true);
            bool success = outfitBinding.Configure(animator, outfitBinding.MotionRoot, outfitBinding.Simulation,
                renderers.ToArray(), chains.ToArray(), capsules.ToArray());
            error = success ? string.Empty : $"보조본 재연결에 실패했다: {outfitBinding.ValidationError}";
            return success;
        }

        private static bool IsHairRelatedChain(SecondaryMotionChain chain)
        {
            if ((chain == null) || (chain.Root == null))
            {
                return false;
            }

            if (chain.Root.name.Contains(HAIR_BONE_MARKER))
            {
                return true;
            }

            for (int i = 0; i < chain.BoneCount; ++i)
            {
                Transform bone = chain.GetBone(i);
                if ((bone != null) && (bone.name.Contains(HAIR_BONE_MARKER)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsHairRelatedCapsule(SecondaryMotionCapsule capsule)
        {
            return (capsule != null) && (capsule.Anchor != null) && (capsule.Anchor.name.Contains(HAIR_BONE_MARKER));
        }

        private static SecondaryMotionChain RemapChain(SecondaryMotionChain chain, Dictionary<string, Transform> outfitBones,
            Transform outfitRoot)
        {
            Transform root = (chain.Root == null) ? null : ResolveOrGraftBone(chain.Root, outfitBones, outfitRoot);
            Transform[] bones = new Transform[chain.BoneCount];
            for (int i = 0; i < bones.Length; ++i)
            {
                Transform donorBone = chain.GetBone(i);
                bones[i] = (donorBone == null) ? null : ResolveOrGraftBone(donorBone, outfitBones, outfitRoot);
            }

            return new SecondaryMotionChain(root, bones, chain.EndLength, chain.EndAxis,
                chain.Stiffness, chain.Damping, chain.MaxAngle, chain.Radius);
        }

        private static SecondaryMotionCapsule RemapCapsule(SecondaryMotionCapsule capsule, Dictionary<string, Transform> outfitBones,
            Transform outfitRoot)
        {
            Transform anchor = (capsule.Anchor == null) ? null : ResolveOrGraftBone(capsule.Anchor, outfitBones, outfitRoot);
            return new SecondaryMotionCapsule(anchor, capsule.Start, capsule.End, capsule.Radius);
        }

        private static Transform ResolveOrGraftBone(Transform donorBone, Dictionary<string, Transform> outfitBones,
            Transform outfitRoot)
        {
            if (outfitBones.TryGetValue(donorBone.name, out Transform matched))
            {
                return matched;
            }

            Transform parent = ((donorBone.parent != null) && (outfitBones.TryGetValue(donorBone.parent.name, out Transform mappedParent)))
                ? mappedParent : outfitRoot;
            GameObject grafted = new GameObject(donorBone.name);
            grafted.transform.SetParent(parent, false);
            grafted.transform.localPosition = donorBone.localPosition;
            grafted.transform.localRotation = donorBone.localRotation;
            grafted.transform.localScale    = donorBone.localScale;
            outfitBones[donorBone.name] = grafted.transform;
            return grafted.transform;
        }

        private static SkinnedMeshRenderer[] FindHairRenderers(Transform root)
        {
            return root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => renderer.name.Contains(HAIR_RENDERER_MARKER)).ToArray();
        }

        private static Transform FindSkeletonRoot(Transform root)
        {
            Animator animator = root.GetComponentInChildren<Animator>(true);
            return (animator != null) ? animator.transform : null;
        }

        private static void CollectBonesByName(Transform root, Dictionary<string, Transform> bones)
        {
            bones[root.name] = root;
            for (int i = 0; i < root.childCount; ++i)
            {
                CollectBonesByName(root.GetChild(i), bones);
            }
        }
    }
}
