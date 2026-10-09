// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Data
{
    /// <summary> 캐릭터 외형 슬라이더와 UMA DNA 대응, 색 목록, 직업별 고정 헤어, 장비 부위의 UMA 의상 슬롯 </summary>
    [CreateAssetMenu(fileName = "AvatarCatalog", menuName = "Shinshi/AvatarCatalogSO")]
    public class AvatarCatalogSO : GameDataSO
    {
        // 지금 대응값은 우리 여성 종족을 만들기 전까지 쓰는 UMA 기본 종족 Human Female 3.0 기준 임시값이다.
        // 우리 종족을 만들면 종족 이름, DNA 이름, 레시피 이름만 바꾸고 슬라이더 순서와 ID는 그대로 둔다.
        // 슬라이더 배열의 순서는 CharacterAppearance의 face, body 인덱스와 같아야 하며 바꾸면 외형 버전을 올린다

        /// <summary> 저장 인덱스 하나가 UMA DNA 하나를 어떻게 움직이는지 </summary>
        [Serializable]
        public class SliderDefinition
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private string _sliderId;           // 저장 인덱스의 의미, 버전 안에서 바꾸지 않는다
            [SerializeField] private string _dnaName;            // 비우면 현재 종족에 대응 DNA가 없어 적용하지 않는다
            [SerializeField] private float  _dnaMin     = 0f;    // 저장값 0일 때 DNA 값
            [SerializeField] private float  _dnaNeutral = 0.5f;  // 저장값 128일 때 DNA 값
            [SerializeField] private float  _dnaMax     = 1f;    // 저장값 255일 때 DNA 값
            [SerializeField] private bool   _inverted;           // DNA가 슬라이더와 반대 방향일 때 켠다

            public string SliderId   => _sliderId;
            public string DnaName    => _dnaName;
            public float  DnaMin     => _dnaMin;
            public float  DnaNeutral => _dnaNeutral;
            public float  DnaMax     => _dnaMax;
            public bool   Inverted   => _inverted;

            /****************************************
            *                Methods
            ****************************************/

            public SliderDefinition(string sliderId, string dnaName, float dnaMin = 0f, float dnaNeutral = 0.5f,
                float dnaMax = 1f, bool inverted = false)
            {
                _sliderId   = sliderId;
                _dnaName    = dnaName;
                _dnaMin     = dnaMin;
                _dnaNeutral = dnaNeutral;
                _dnaMax     = dnaMax;
                _inverted   = inverted;
            }
        }

        /// <summary> 직업마다 고정한 헤어, 선택 기능을 열면 직업당 여러 개로 늘린다 </summary>
        [Serializable]
        public class ClassHair
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private EPlayerClass _playerClass;
            [SerializeField] private int          _hairId;      // 외형 데이터에 저장하는 값, 바꾸지 않는다
            [SerializeField] private string       _recipeName;  // UMA 의상 레시피 에셋 이름

            public EPlayerClass PlayerClass => _playerClass;
            public int          HairId      => _hairId;
            public string       RecipeName  => _recipeName;

            /****************************************
            *                Methods
            ****************************************/

            public ClassHair(EPlayerClass playerClass, int hairId, string recipeName)
            {
                _playerClass = playerClass;
                _hairId      = hairId;
                _recipeName  = recipeName;
            }
        }

        /// <summary> 장비 부위가 차지하는 UMA 의상 슬롯 </summary>
        [Serializable]
        public class GearSlotMapping
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private EEquipmentSlot _slot;
            [SerializeField] private string[]       _wardrobeSlots;  // 몸통은 상의와 하의 두 슬롯을 함께 쓴다

            public EEquipmentSlot        Slot          => _slot;
            public IReadOnlyList<string> WardrobeSlots => _wardrobeSlots;

            /****************************************
            *                Methods
            ****************************************/

            public GearSlotMapping(EEquipmentSlot slot, params string[] wardrobeSlots)
            {
                _slot          = slot;
                _wardrobeSlots = wardrobeSlots;
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        private const float DNA_RANGE_MIN    = 0f;   // UMA DNA 값 범위
        private const float DNA_RANGE_MAX    = 1f;
        private const int   PALETTE_MAX_SIZE = CharacterAppearance.VALUE_MAX + 1;  // 팔레트 ID는 1바이트

        private static readonly string[] G_EmptySlots = new string[0];

        [Header("종족")]
        [SerializeField] private string _raceName = "Human Female 3.0";  // UMA RaceData.raceName

        [Header("슬라이더 - CharacterAppearance 인덱스 순서")]
        [SerializeField] private SliderDefinition[] _faceSliders = CreateDefaultFaceSliders();
        [SerializeField] private SliderDefinition[] _bodySliders = CreateDefaultBodySliders();

        [Header("색 - UMA 공유 색 이름과 팔레트")]
        [SerializeField] private string   _skinColorName = "Skin";
        [SerializeField] private string   _eyeColorName  = "Eyes";
        [SerializeField] private string   _browColorName = "Brows";
        [SerializeField] private string   _lipColorName  = "Lips";   // TODO(민성): 기본 종족에 입술 색 채널이 없다. 우리 종족에서 연결
        [SerializeField] private Gradient _skinTones     = CreateDefaultSkinTones();
        [SerializeField] private Color[]  _eyeColors     = CreateDefaultEyeColors();
        [SerializeField] private Color[]  _browColors    = CreateDefaultBrowColors();
        [SerializeField] private Color[]  _lipColors     = CreateDefaultLipColors();

        [Header("의상")]
        [SerializeField] private string[]          _baseWardrobeRecipes = { "underwear_white_granit_top_Recipe", "underwear_white_granit_bottom_Recipe" };
        [SerializeField] private string            _hairWardrobeSlot    = "Hair";
        [SerializeField] private ClassHair[]       _classHairs          = CreateDefaultClassHairs();
        [SerializeField] private ClassHair[]       _newClassHairs       = CreateDefaultNewClassHairs();  // 검사, 마검사, 궁수
        [SerializeField] private GearSlotMapping[] _gearSlots           = CreateDefaultGearSlots();

        [Header("UMA 생성기 - 마을에 여러 명이 한꺼번에 들어올 때 프레임 튐을 막는다")]
        [SerializeField] private int  _generatorIterationCount    = 1;      // 프레임당 처리할 생성 단계 수
        [SerializeField] private int  _generatorInterFrameDelay   = 1;      // 생성 사이에 쉬는 프레임 수
        [SerializeField] private bool _generatorProcessAllPending = false;  // 켜면 대기 중인 모두를 한 프레임에 처리한다

        [NonSerialized] private ClassHair[] _allClassHairs;

        public string                          RaceName                   => _raceName;
        public IReadOnlyList<SliderDefinition> FaceSliders                => _faceSliders;
        public IReadOnlyList<SliderDefinition> BodySliders                => _bodySliders;
        public string                          SkinColorName              => _skinColorName;
        public string                          EyeColorName               => _eyeColorName;
        public string                          BrowColorName              => _browColorName;
        public string                          LipColorName               => _lipColorName;
        public Gradient                        SkinTones                  => _skinTones;
        public IReadOnlyList<Color>            EyeColors                  => _eyeColors;
        public IReadOnlyList<Color>            BrowColors                 => _browColors;
        public IReadOnlyList<Color>            LipColors                  => _lipColors;
        public IReadOnlyList<string>           BaseWardrobeRecipes        => _baseWardrobeRecipes;
        public string                          HairWardrobeSlot           => _hairWardrobeSlot;
        public IReadOnlyList<ClassHair>        ClassHairs                 => GetAllClassHairs();
        public int                             GeneratorIterationCount    => _generatorIterationCount;
        public int                             GeneratorInterFrameDelay   => _generatorInterFrameDelay;
        public bool                            GeneratorProcessAllPending => _generatorProcessAllPending;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 직업의 고정 헤어, 정의가 없으면 false </summary>
        public bool TryGetClassHair(EPlayerClass playerClass, out ClassHair hair)
        {
            ClassHair[] all = GetAllClassHairs();
            for (int i = 0; i < all.Length; ++i)
            {
                if ((all[i] != null) && (all[i].PlayerClass == playerClass))
                {
                    hair = all[i];
                    return true;
                }
            }

            hair = null;
            return false;
        }

        // 구 직업 헤어 뒤에 신규 직업 헤어를 이은 목록, 처음 읽을 때 한 번 만들고 재사용한다
        private ClassHair[] GetAllClassHairs()
        {
            if (_allClassHairs != null)
            {
                return _allClassHairs;
            }

            int legacyCount = (_classHairs == null) ? 0 : _classHairs.Length;
            int newCount    = (_newClassHairs == null) ? 0 : _newClassHairs.Length;
            var merged      = new ClassHair[legacyCount + newCount];
            for (int i = 0; i < legacyCount; ++i)
            {
                merged[i] = _classHairs[i];
            }

            for (int i = 0; i < newCount; ++i)
            {
                merged[legacyCount + i] = _newClassHairs[i];
            }

            _allClassHairs = merged;
            return merged;
        }

        protected override void OnValidate()
        {
            _allClassHairs = null;
            base.OnValidate();
        }

        private void OnEnable()
        {
            _allClassHairs = null;
        }

        /// <summary> 저장된 hairId를 그 직업이 쓸 수 있는지, 지금은 직업 기본 헤어만 허용한다 </summary>
        public bool IsHairAllowed(EPlayerClass playerClass, int hairId)
        {
            return (TryGetClassHair(playerClass, out ClassHair hair)) && (hair.HairId == hairId);
        }

        /// <summary> 장비 부위가 쓰는 UMA 의상 슬롯, 무기처럼 의상이 아닌 부위는 빈 목록 </summary>
        public IReadOnlyList<string> GetWardrobeSlots(EEquipmentSlot slot)
        {
            for (int i = 0; i < _gearSlots.Length; ++i)
            {
                if ((_gearSlots[i] != null) && (_gearSlots[i].Slot == slot))
                {
                    return _gearSlots[i].WardrobeSlots;
                }
            }

            return G_EmptySlots;
        }

        /// <summary> 모든 슬라이더가 중립이고 첫 팔레트 색과 직업 기본 헤어를 쓰는 외형 </summary>
        public CharacterAppearance CreateDefaultAppearance(EPlayerClass playerClass)
        {
            int hairId = TryGetClassHair(playerClass, out ClassHair hair) ? hair.HairId : CharacterAppearance.VALUE_MIN;

            // UMA 슬라이더 외형은 v2 의미다. CURRENT_VERSION(v3, P09)으로 올리지 않는다
            return new CharacterAppearance(CharacterAppearance.VERSION_2, CharacterAppearance.BODY_TYPE_FEMALE,
                CreateNeutralSliders(CharacterAppearance.FACE_SLIDER_COUNT),
                CreateNeutralSliders(CharacterAppearance.BODY_SLIDER_COUNT),
                CharacterAppearance.SLIDER_NEUTRAL, CharacterAppearance.VALUE_MIN, CharacterAppearance.VALUE_MIN,
                CharacterAppearance.VALUE_MIN, hairId, CharacterAppearance.VALUE_MIN);
        }

        protected override void ValidateValues(List<string> errors)
        {
            string owner = nameof(AvatarCatalogSO);

            if (string.IsNullOrWhiteSpace(_raceName))
            {
                errors.Add($"{owner}.{nameof(_raceName)}: 종족 이름이 비어 있다");
            }

            DataValidation.RequireCount(errors, owner, nameof(_faceSliders), _faceSliders, CharacterAppearance.FACE_SLIDER_COUNT);
            DataValidation.RequireCount(errors, owner, nameof(_bodySliders), _bodySliders, CharacterAppearance.BODY_SLIDER_COUNT);

            var sliderIds = new HashSet<string>();
            ValidateSliders(errors, owner, nameof(_faceSliders), _faceSliders, sliderIds);
            ValidateSliders(errors, owner, nameof(_bodySliders), _bodySliders, sliderIds);

            ValidateColorName(errors, owner, nameof(_skinColorName), _skinColorName);
            ValidateColorName(errors, owner, nameof(_eyeColorName), _eyeColorName);
            ValidateColorName(errors, owner, nameof(_browColorName), _browColorName);
            ValidateColorName(errors, owner, nameof(_lipColorName), _lipColorName);

            if (_skinTones == null)
            {
                errors.Add($"{owner}.{nameof(_skinTones)}: 피부색 그라데이션이 없다");
            }

            ValidatePalette(errors, owner, nameof(_eyeColors), _eyeColors);
            ValidatePalette(errors, owner, nameof(_browColors), _browColors);
            ValidatePalette(errors, owner, nameof(_lipColors), _lipColors);

            if (string.IsNullOrWhiteSpace(_hairWardrobeSlot))
            {
                errors.Add($"{owner}.{nameof(_hairWardrobeSlot)}: 헤어 의상 슬롯 이름이 비어 있다");
            }

            ValidateNames(errors, owner, nameof(_baseWardrobeRecipes), _baseWardrobeRecipes);
            ValidateClassHairs(errors, owner);
            ValidateGearSlots(errors, owner);

            DataValidation.RequirePositive(errors, owner, nameof(_generatorIterationCount), _generatorIterationCount);
            DataValidation.RequireNonNegative(errors, owner, nameof(_generatorInterFrameDelay), _generatorInterFrameDelay);
        }

        private static void ValidateSliders(List<string> errors, string owner, string field, SliderDefinition[] sliders,
            HashSet<string> seenIds)
        {
            if (sliders == null)
            {
                return;
            }

            for (int i = 0; i < sliders.Length; ++i)
            {
                SliderDefinition slider = sliders[i];
                if (slider == null)
                {
                    errors.Add($"{owner}.{field}[{i}]: 항목이 비어 있다");
                    continue;
                }

                if ((string.IsNullOrWhiteSpace(slider.SliderId)) || (!seenIds.Add(slider.SliderId)))
                {
                    errors.Add($"{owner}.{field}[{i}]: sliderId '{slider.SliderId}'가 비었거나 중복된다");
                }

                bool inRange = (slider.DnaMin >= DNA_RANGE_MIN) && (slider.DnaMin <= slider.DnaNeutral)
                    && (slider.DnaNeutral <= slider.DnaMax) && (slider.DnaMax <= DNA_RANGE_MAX);
                if (!inRange)
                {
                    errors.Add($"{owner}.{field}[{i}]: DNA 값은 {DNA_RANGE_MIN} <= 최소 <= 중립 <= 최대 <= {DNA_RANGE_MAX}이어야 한다");
                }
            }
        }

        private static void ValidateColorName(List<string> errors, string owner, string field, string colorName)
        {
            if (string.IsNullOrWhiteSpace(colorName))
            {
                errors.Add($"{owner}.{field}: UMA 공유 색 이름이 비어 있다");
            }
        }

        private static void ValidatePalette(List<string> errors, string owner, string field, Color[] palette)
        {
            int count = (palette == null) ? 0 : palette.Length;
            if ((count == 0) || (count > PALETTE_MAX_SIZE))
            {
                errors.Add($"{owner}.{field}: 팔레트 색 수 {count}는 1 이상 {PALETTE_MAX_SIZE} 이하여야 한다");
            }
        }

        private static void ValidateNames(List<string> errors, string owner, string field, string[] names)
        {
            if (names == null)
            {
                return;
            }

            for (int i = 0; i < names.Length; ++i)
            {
                if (string.IsNullOrWhiteSpace(names[i]))
                {
                    errors.Add($"{owner}.{field}[{i}]: 이름이 비어 있다");
                }
            }
        }

        // 직업마다 정확히 하나, hairId는 직업 사이에서도 겹치지 않아야 저장값만으로 헤어를 알 수 있다
        private void ValidateClassHairs(List<string> errors, string owner)
        {
            var hairIds = new HashSet<int>();
            var classes = new HashSet<EPlayerClass>();
            ClassHair[] all = GetAllClassHairs();
            int count   = all.Length;

            for (int i = 0; i < count; ++i)
            {
                ClassHair hair = all[i];
                if (hair == null)
                {
                    errors.Add($"{owner}.{nameof(_classHairs)}[{i}]: 항목이 비어 있다");
                    continue;
                }

                if (!classes.Add(hair.PlayerClass))
                {
                    errors.Add($"{owner}.{nameof(_classHairs)}[{i}]: 직업 {hair.PlayerClass}가 중복된다");
                }

                bool idInRange = (hair.HairId >= CharacterAppearance.VALUE_MIN) && (hair.HairId <= CharacterAppearance.HAIR_ID_MAX);
                if ((!idInRange) || (!hairIds.Add(hair.HairId)))
                {
                    errors.Add($"{owner}.{nameof(_classHairs)}[{i}]: hairId {hair.HairId}가 범위를 벗어났거나 중복된다");
                }

                if (string.IsNullOrWhiteSpace(hair.RecipeName))
                {
                    errors.Add($"{owner}.{nameof(_classHairs)}[{i}]: 레시피 이름이 비어 있다");
                }
            }

            foreach (EPlayerClass playerClass in Enum.GetValues(typeof(EPlayerClass)))
            {
                if (!classes.Contains(playerClass))
                {
                    errors.Add($"{owner}.{nameof(_classHairs)}: 직업 {playerClass}의 헤어가 없다");
                }
            }
        }

        private void ValidateGearSlots(List<string> errors, string owner)
        {
            var slots = new HashSet<EEquipmentSlot>();
            int count = (_gearSlots == null) ? 0 : _gearSlots.Length;

            for (int i = 0; i < count; ++i)
            {
                GearSlotMapping mapping = _gearSlots[i];
                if (mapping == null)
                {
                    errors.Add($"{owner}.{nameof(_gearSlots)}[{i}]: 항목이 비어 있다");
                    continue;
                }

                if (!slots.Add(mapping.Slot))
                {
                    errors.Add($"{owner}.{nameof(_gearSlots)}[{i}]: 장비 부위 {mapping.Slot}가 중복된다");
                }

                int slotCount = (mapping.WardrobeSlots == null) ? 0 : mapping.WardrobeSlots.Count;
                if (slotCount == 0)
                {
                    errors.Add($"{owner}.{nameof(_gearSlots)}[{i}]: 의상 슬롯이 없다");
                    continue;
                }

                for (int j = 0; j < slotCount; ++j)
                {
                    if (string.IsNullOrWhiteSpace(mapping.WardrobeSlots[j]))
                    {
                        errors.Add($"{owner}.{nameof(_gearSlots)}[{i}][{j}]: 의상 슬롯 이름이 비어 있다");
                    }
                }
            }
        }

        private static int[] CreateNeutralSliders(int count)
        {
            int[] sliders = new int[count];
            for (int i = 0; i < count; ++i)
            {
                sliders[i] = CharacterAppearance.SLIDER_NEUTRAL;
            }

            return sliders;
        }

        // 기본 종족에 대응 DNA가 없는 슬라이더는 DNA 이름을 비워 둔다(눈썹 기울기와 두께, 입 높이, 아랫입술, 목 길이, 종아리)
        private static SliderDefinition[] CreateDefaultFaceSliders()
        {
            return new[]
            {
                new SliderDefinition("face_width",       "headWidth"),
                new SliderDefinition("cheekbone_width",  "cheekWidth"),
                new SliderDefinition("cheek_fullness",   "cheekSize"),
                new SliderDefinition("jaw_length",       "chinPosition"),
                new SliderDefinition("jaw_width",        "jawsSize"),
                new SliderDefinition("chin_shape",       "chinPronounced"),
                new SliderDefinition("eye_size",         "eyeSize"),
                new SliderDefinition("eye_spacing",      "eyeSpacing"),
                new SliderDefinition("eye_height",       "EyePosition", 0.6f, 1f, 1f),  // 기본 종족은 중립이 1이라 아래로만 움직인다
                new SliderDefinition("eye_tilt",         "eyeRotation"),
                new SliderDefinition("brow_height",      "BrowPosition"),
                new SliderDefinition("brow_tilt",        ""),
                new SliderDefinition("brow_thickness",   ""),
                new SliderDefinition("nose_size",        "noseSize"),
                new SliderDefinition("nose_length",      "nosePronounced"),
                new SliderDefinition("nose_bridge",      "noseFlatten", inverted: true),  // 납작할수록 콧대가 낮다
                new SliderDefinition("nose_width",       "noseWidth"),
                new SliderDefinition("nose_tip_height",  "noseInclination"),
                new SliderDefinition("mouth_width",      "mouthSize"),
                new SliderDefinition("mouth_height",     ""),
                new SliderDefinition("upper_lip",        "lipsSize"),
                new SliderDefinition("lower_lip",        "")
            };
        }

        private static SliderDefinition[] CreateDefaultBodySliders()
        {
            return new[]
            {
                new SliderDefinition("height",          "height"),
                new SliderDefinition("head_size",       "headSize"),
                new SliderDefinition("neck_length",     ""),
                new SliderDefinition("shoulder_width",  "shoulderWidth"),
                new SliderDefinition("arm_thickness",   "armWidth"),
                new SliderDefinition("thigh_thickness", "legsSize"),
                new SliderDefinition("calf_thickness",  ""),
                new SliderDefinition("chest",           "breastSize"),
                new SliderDefinition("waist",           "waist"),
                new SliderDefinition("hips",            "gluteusSize")
            };
        }

        // TODO(민성): 피부색, 눈, 눈썹, 입술 팔레트는 아트 확정 전 자리값
        private static Gradient CreateDefaultSkinTones()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.96f, 0.86f, 0.78f), 0f),
                    new GradientColorKey(new Color(0.87f, 0.72f, 0.60f), 0.5f),
                    new GradientColorKey(new Color(0.62f, 0.46f, 0.36f), 1f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }

        private static Color[] CreateDefaultEyeColors()
        {
            return new[]
            {
                new Color(0.18f, 0.11f, 0.07f), new Color(0.24f, 0.15f, 0.09f), new Color(0.31f, 0.20f, 0.12f),
                new Color(0.12f, 0.08f, 0.06f), new Color(0.38f, 0.26f, 0.15f), new Color(0.27f, 0.22f, 0.16f),
                new Color(0.20f, 0.20f, 0.19f), new Color(0.33f, 0.29f, 0.22f), new Color(0.43f, 0.31f, 0.18f),
                new Color(0.16f, 0.13f, 0.11f), new Color(0.22f, 0.25f, 0.24f), new Color(0.47f, 0.37f, 0.24f)
            };
        }

        private static Color[] CreateDefaultBrowColors()
        {
            return new[]
            {
                new Color(0.06f, 0.05f, 0.05f), new Color(0.10f, 0.08f, 0.07f), new Color(0.15f, 0.11f, 0.08f),
                new Color(0.21f, 0.15f, 0.10f), new Color(0.28f, 0.20f, 0.13f), new Color(0.12f, 0.12f, 0.12f),
                new Color(0.34f, 0.25f, 0.17f), new Color(0.18f, 0.14f, 0.12f)
            };
        }

        private static Color[] CreateDefaultLipColors()
        {
            return new[]
            {
                new Color(0.78f, 0.47f, 0.45f), new Color(0.72f, 0.40f, 0.40f), new Color(0.82f, 0.52f, 0.50f),
                new Color(0.66f, 0.36f, 0.36f), new Color(0.75f, 0.45f, 0.48f), new Color(0.70f, 0.42f, 0.38f),
                new Color(0.60f, 0.33f, 0.33f), new Color(0.85f, 0.58f, 0.55f)
            };
        }

        // UMA 기본 헤어 중 직업 시안(높은 말총, 낮게 묶은 머리, 긴 땋은 머리)에 가까운 것을 임시로 고른다
        private static ClassHair[] CreateDefaultClassHairs()
        {
            return new[]
            {
                new ClassHair(EPlayerClass.Warrior,  1, "HairPonytail_Recipe"),
                new ClassHair(EPlayerClass.Sorcerer, 2, "Hair_Bun_Recipe"),
                new ClassHair(EPlayerClass.Summoner, 3, "Hair_LongSwept_Recipe")
            };
        }

        // 이미 저장된 AvatarCatalog 에셋의 _classHairs 길이는 유지되므로 신규 직업은 별도 배열의 기본값으로 받는다
        private static ClassHair[] CreateDefaultNewClassHairs()
        {
            return new[]
            {
                new ClassHair(EPlayerClass.Swordsman,  4, "HairPonytail_Recipe"),
                new ClassHair(EPlayerClass.Mageknight, 5, "Hair_Bun_Recipe"),
                new ClassHair(EPlayerClass.Archer,     6, "Hair_LongSwept_Recipe")
            };
        }

        // 무기는 의상이 아니라 손 소켓에 붙이므로 여기에 없다
        private static GearSlotMapping[] CreateDefaultGearSlots()
        {
            return new[]
            {
                new GearSlotMapping(EEquipmentSlot.Head,      "Helmet"),
                new GearSlotMapping(EEquipmentSlot.Body,      "Chest", "Legs"),
                new GearSlotMapping(EEquipmentSlot.Hands,     "Hands"),
                new GearSlotMapping(EEquipmentSlot.Feet,      "Feet"),
                new GearSlotMapping(EEquipmentSlot.Accessory, "Waist")
            };
        }
    }
}
