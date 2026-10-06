// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Art
{
    /// <summary> P09 프리팹 인스턴스에 v3 외형을 적용한다. 필요한 부위를 모두 찾은 뒤에만 바꾸므로 실패하면 인스턴스는 그대로다 </summary>
    public static class PlayerCharacterLookApplier
    {
        // 부위는 같은 골격을 공유하는 자식이라 켜고 끄기만 한다. 규칙은 P09 데모 AvatarView와 같다.
        // 성별 분기(Female/Male) 아래 얼굴, 맨몸, 장비 메시가 있고 헤어는 공용이다. 헤어는 그룹과 렌더러가 같은 이름이라 이름별 목록으로 다룬다.
        // 색은 재질 슬롯 교체다. 슬롯은 변환 사본의 이름 접두사로 찾으므로 교체 뒤에도 같은 규칙으로 다시 찾을 수 있다

        /****************************************
        *                Fields
        ****************************************/

        public const string FEMALE_BRANCH = "Female";
        public const string MALE_BRANCH   = "Male";

        private const string FEMALE_ARMOR_PREFIX = "Fem";
        private const string MALE_ARMOR_PREFIX   = "Male";
        private const string HAIR_01             = "Hair_01";
        private const string HAIR_02             = "Hair_02";
        private const string SKINHEAD_FORMAT     = "SkinHead_{0}";
        private const string FACE_FORMAT         = "{0}_Face_{1:D2}";
        private const string FACIAL_HAIR_FORMAT  = "Male_FacialHair_{0:D2}";
        private const string BODY_FORMAT         = "{0}_Body_{1}";
        private const string ARMOR_FORMAT        = "{0}_Armor_003_{1}";
        private const string BUST_LEFT           = "bust_01_L";
        private const string BUST_RIGHT          = "bust_01_R";
        private const string FEMALE_SKIN_PREFIX  = "M_P09_Female_Skin";
        private const string MALE_SKIN_PREFIX    = "M_P09_Male_Skin";
        private const string EYE_PREFIX          = "M_P09_Eye_";
        private const string HAIR_COLOR_PREFIX   = "M_P09_Hair_";
        private const int    FACE_COUNT          = CharacterAppearanceCatalog.P09_FACE_MAX;
        private const int    FACIAL_HAIR_COUNT   = CharacterAppearanceCatalog.P09_FACIAL_HAIR_MAX;

        // 장비 비트와 부위 이름. 가슴, 팔, 다리는 장비를 벗으면 맨몸 메시를 켠다(머리, 허리는 맨몸 메시가 없다)
        private static readonly int[]    G_OutfitBits  = { CharacterAppearanceCatalog.P09_OUTFIT_HEAD, CharacterAppearanceCatalog.P09_OUTFIT_CHEST,
                                                           CharacterAppearanceCatalog.P09_OUTFIT_ARM, CharacterAppearanceCatalog.P09_OUTFIT_WAIST,
                                                           CharacterAppearanceCatalog.P09_OUTFIT_LEG };
        private static readonly string[] G_OutfitParts = { "Head", "Chest", "Arm", "Waist", "Leg" };
        private static readonly bool[]   G_HasBareBody = { false, true, true, false, true };

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 인스턴스에 외형을 적용한다. 부위나 재질이 하나라도 없으면 아무것도 바꾸지 않고 거짓과 오류를 돌려준다 </summary>
        public static bool TryApply(GameObject instance, PlayerCharacterLookCatalogSO catalog, CharacterP09Look look, out string error)
        {
            if ((instance == null) || (catalog == null))
            {
                error = "인스턴스와 카탈로그가 필요하다";
                return false;
            }

            var nodes = new Dictionary<string, List<Transform>>();
            Transform[] transforms = instance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; ++i)
            {
                if (!nodes.TryGetValue(transforms[i].name, out List<Transform> list))
                {
                    list = new List<Transform>(1);
                    nodes.Add(transforms[i].name, list);
                }

                list.Add(transforms[i]);
            }

            // 1단계: 바꿀 대상을 모두 모은다
            var  toggles = new List<KeyValuePair<GameObject, bool>>();
            var  missing = new List<string>();
            bool isMale  = look.IsMale;

            AddToggle(nodes, FEMALE_BRANCH, !isMale, toggles, missing);
            AddToggle(nodes, MALE_BRANCH, isMale, toggles, missing);
            AddSexParts(nodes, FEMALE_BRANCH, FEMALE_ARMOR_PREFIX, look, !isMale, toggles, missing);
            AddSexParts(nodes, MALE_BRANCH, MALE_ARMOR_PREFIX, look, isMale, toggles, missing);

            AddToggle(nodes, HAIR_01, look.HairId == CharacterAppearanceCatalog.P09_HAIR_01, toggles, missing);
            AddToggle(nodes, HAIR_02, look.HairId == CharacterAppearanceCatalog.P09_HAIR_02, toggles, missing);
            AddToggle(nodes, string.Format(SKINHEAD_FORMAT, FEMALE_BRANCH),
                (!isMale) && (look.HairId == CharacterAppearanceCatalog.P09_HAIR_SKINHEAD), toggles, missing);
            AddToggle(nodes, string.Format(SKINHEAD_FORMAT, MALE_BRANCH),
                isMale && (look.HairId == CharacterAppearanceCatalog.P09_HAIR_SKINHEAD), toggles, missing);

            // 수염 0은 메시가 없다(모두 끔). 1 ~ 8만 메시를 찾는다
            for (int i = 1; i <= FACIAL_HAIR_COUNT; ++i)
            {
                AddToggle(nodes, string.Format(FACIAL_HAIR_FORMAT, i), isMale && (look.FacialHair == i), toggles, missing);
            }

            Material femaleSkin = catalog.GetSkin(false, look.SkinTone);
            Material maleSkin   = catalog.GetSkin(true, look.SkinTone);
            Material eye        = catalog.GetEye(look.EyeColor);
            Material hairColor  = catalog.GetHairColor(look.HairColor);
            if ((femaleSkin == null) || (maleSkin == null) || (eye == null) || (hairColor == null))
            {
                missing.Add("색 재질");
            }

            Transform bustLeft  = FindFirst(nodes, BUST_LEFT);
            Transform bustRight = FindFirst(nodes, BUST_RIGHT);
            Vector3   bustScale = Vector3.one;
            if ((bustLeft == null) || (bustRight == null))
            {
                missing.Add("가슴 뼈");
            }
            else if ((!isMale) && (!catalog.TryGetBustScale(look.BustSize, out bustScale)))
            {
                missing.Add("가슴 크기");
            }

            if (missing.Count > 0)
            {
                error = "P09 부위를 찾지 못했다: " + string.Join(", ", missing);
                return false;
            }

            // 2단계: 실제로 바꾼다
            for (int i = 0; i < toggles.Count; ++i)
            {
                if (toggles[i].Key.activeSelf != toggles[i].Value)
                {
                    toggles[i].Key.SetActive(toggles[i].Value);
                }
            }

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; ++i)
            {
                ReplaceSlots(renderers[i], femaleSkin, maleSkin, eye, hairColor);
            }

            bustLeft.localScale  = bustScale;
            bustRight.localScale = bustScale;
            error = string.Empty;
            return true;
        }

        // 한 성별의 얼굴, 장비, 맨몸 메시. 꺼진 성별은 분기째 꺼지므로 자식은 프리팹 기본 상태(얼굴 1, 장비 착용)로 둔다
        private static void AddSexParts(Dictionary<string, List<Transform>> nodes, string sex, string armorPrefix, CharacterP09Look look,
            bool isActiveSex, List<KeyValuePair<GameObject, bool>> toggles, List<string> missing)
        {
            for (int face = 1; face <= FACE_COUNT; ++face)
            {
                bool isFaceOn = isActiveSex ? (look.FaceId == face) : (face == 1);
                AddToggle(nodes, string.Format(FACE_FORMAT, sex, face), isFaceOn, toggles, missing);
            }

            for (int i = 0; i < G_OutfitParts.Length; ++i)
            {
                bool wearsArmor = (!isActiveSex) || look.HasOutfit(G_OutfitBits[i]);
                AddToggle(nodes, string.Format(ARMOR_FORMAT, armorPrefix, G_OutfitParts[i]), wearsArmor, toggles, missing);
                if (G_HasBareBody[i])
                {
                    AddToggle(nodes, string.Format(BODY_FORMAT, sex, G_OutfitParts[i]), !wearsArmor, toggles, missing);
                }
            }
        }

        private static void AddToggle(Dictionary<string, List<Transform>> nodes, string name, bool active,
            List<KeyValuePair<GameObject, bool>> toggles, List<string> missing)
        {
            if (!nodes.TryGetValue(name, out List<Transform> list))
            {
                missing.Add(name);
                return;
            }

            for (int i = 0; i < list.Count; ++i)
            {
                toggles.Add(new KeyValuePair<GameObject, bool>(list[i].gameObject, active));
            }
        }

        private static Transform FindFirst(Dictionary<string, List<Transform>> nodes, string name)
        {
            return nodes.TryGetValue(name, out List<Transform> list) ? list[0] : null;
        }

        // 인스턴스 재질이 생기지 않도록 sharedMaterials 배열을 바꾼다
        private static void ReplaceSlots(Renderer renderer, Material femaleSkin, Material maleSkin, Material eye, Material hairColor)
        {
            Material[] materials = renderer.sharedMaterials;
            bool       changed   = false;
            for (int i = 0; i < materials.Length; ++i)
            {
                Material current = materials[i];
                if (current == null)
                {
                    continue;
                }

                Material next = current;
                string   name = current.name;
                if (name.StartsWith(FEMALE_SKIN_PREFIX))
                {
                    next = femaleSkin;
                }
                else if (name.StartsWith(MALE_SKIN_PREFIX))
                {
                    next = maleSkin;
                }
                else if (name.StartsWith(EYE_PREFIX))
                {
                    next = eye;
                }
                else if (name.StartsWith(HAIR_COLOR_PREFIX))
                {
                    next = hairColor;
                }

                if (next != current)
                {
                    materials[i] = next;
                    changed      = true;
                }
            }

            if (changed)
            {
                renderer.sharedMaterials = materials;
            }
        }
    }
}
