// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Art
{
    /// <summary> P09 모듈 캐릭터 조립에 쓰는 프리팹, 컨트롤러, 색 재질과 가슴 크기. 에디터 도구(P09LookCatalogBuilder)가 채운다 </summary>
    [CreateAssetMenu(fileName = "PlayerCharacterLookCatalog", menuName = "Shinshi/PlayerCharacterLookCatalogSO")]
    public sealed class PlayerCharacterLookCatalogSO : ScriptableObject
    {
        // 배열 인덱스는 외형 ID - 1이다(피부, 눈, 헤어 색, 가슴 크기는 모두 1부터). 메시 이름 규칙은 PlayerCharacterLookApplier에 있다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private GameObject                _prefab       = null;
        [SerializeField] private RuntimeAnimatorController _controller   = null;
        [SerializeField] private Material[]                _femaleSkins  = Array.Empty<Material>();
        [SerializeField] private Material[]                _maleSkins    = Array.Empty<Material>();
        [SerializeField] private Material[]                _eyes         = Array.Empty<Material>();
        [SerializeField] private Material[]                _hairColors   = Array.Empty<Material>();
        [SerializeField] private Vector3[]                 _bustScales   = Array.Empty<Vector3>();

        // 생성 화면 표정 미리보기 이름. 인덱스 + 1이 Animator FaceEmotion 값이다(0 = 무표정)
        [SerializeField] private string[]                  _faceEmotions = Array.Empty<string>();

        /****************************************
        *              Properties
        ****************************************/

        public GameObject                Prefab           => _prefab;
        public RuntimeAnimatorController Controller       => _controller;
        public int                       FaceEmotionCount => (_faceEmotions == null) ? 0 : _faceEmotions.Length;

        /****************************************
        *                Methods
        ****************************************/

        public Material GetSkin(bool isMale, int skinTone) { return GetById(isMale ? _maleSkins : _femaleSkins, skinTone); }
        public Material GetEye(int eyeColor)               { return GetById(_eyes, eyeColor); }
        public Material GetHairColor(int hairColor)        { return GetById(_hairColors, hairColor); }

        public bool TryGetBustScale(int bustSize, out Vector3 scale)
        {
            int index = bustSize - 1;
            if ((_bustScales == null) || (index < 0) || (index >= _bustScales.Length))
            {
                scale = Vector3.one;
                return false;
            }

            scale = _bustScales[index];
            return true;
        }

        public string GetFaceEmotionName(int emotion)
        {
            int index = emotion - 1;
            return ((_faceEmotions != null) && (index >= 0) && (index < _faceEmotions.Length)) ? _faceEmotions[index] : string.Empty;
        }

        /// <summary> 외형 계약 v3의 모든 ID에 재질과 값이 채워졌는지 본다, 빈 항목을 errors에 더한다 </summary>
        public bool Validate(List<string> errors)
        {
            int before = errors.Count;
            if (_prefab == null)
            {
                errors.Add("프리팹이 비어 있다");
            }

            if (_controller == null)
            {
                errors.Add("애니메이터 컨트롤러가 비어 있다");
            }

            RequireMaterials(errors, nameof(_femaleSkins), _femaleSkins, CharacterAppearanceCatalog.P09_SKIN_MAX);
            RequireMaterials(errors, nameof(_maleSkins), _maleSkins, CharacterAppearanceCatalog.P09_SKIN_MAX);
            RequireMaterials(errors, nameof(_eyes), _eyes, CharacterAppearanceCatalog.P09_EYE_COLOR_MAX);
            RequireMaterials(errors, nameof(_hairColors), _hairColors, CharacterAppearanceCatalog.P09_HAIR_COLOR_MAX);
            if ((_bustScales == null) || (_bustScales.Length != CharacterAppearanceCatalog.P09_BUST_MAX))
            {
                errors.Add($"가슴 크기가 {CharacterAppearanceCatalog.P09_BUST_MAX}개가 아니다");
            }

            return errors.Count == before;
        }

        private static void RequireMaterials(List<string> errors, string field, Material[] materials, int count)
        {
            if ((materials == null) || (materials.Length != count))
            {
                errors.Add($"{field} 재질이 {count}개가 아니다");
                return;
            }

            for (int i = 0; i < materials.Length; ++i)
            {
                if (materials[i] == null)
                {
                    errors.Add($"{field}[{i}] 재질이 비어 있다");
                }
            }
        }

        private static Material GetById(Material[] materials, int id)
        {
            int index = id - 1;
            return ((materials != null) && (index >= 0) && (index < materials.Length)) ? materials[index] : null;
        }
    }
}
