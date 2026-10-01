// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using UMA;
using UMA.CharacterSystem;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Art
{
    /// <summary> 저장된 외형과 장비 외형을 UMA 아바타에 적용한다, 서버 빌드에서는 꺼진다 </summary>
    public class AvatarView : ClientView
    {
        // UMA는 첫 생성이 끝나야 DNA 목록과 종족 기본 색을 채운다. 그래서 첫 적용은 종족과 의상만 넣어 생성을 시작하고,
        // 첫 생성 완료 콜백에서 종족 색을 복제해 색을 바꾸고 DNA를 넣은 뒤 한 번 더 갱신한다.
        // 생성 전에 색을 넣으면 종족 색의 채널 구성(피부 반투과, 광택)을 흰 기본값으로 덮어쓴다.
        // 서버 빌드에서는 UMA 컴포넌트 자체를 꺼서 UMA Start가 GPU 없는 서버에서 생성을 돌리지 않게 한다

        /****************************************
        *                Fields
        ****************************************/

        private const int DEFAULT_COLOR_CHANNELS = 3;  // 종족 색을 찾지 못했을 때 새로 만드는 UMA 색 채널 수

        [SerializeField] private DynamicCharacterAvatar _avatar;

        // 장면이 바뀌면 UMA가 생성기를 새로 찾거나 만들므로 설정을 넣은 생성기를 기억해 두고 바뀌면 다시 넣는다
        private static UMAGenerator _tunedGenerator;

        private AvatarCatalogSO     _catalog;
        private CharacterAppearance _appearance;
        private EPlayerClass        _playerClass;
        private bool                _isSubscribed;
        private bool                _isBuildStarted;
        private bool                _isFirstUpdatePending;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 마지막으로 요청한 외형과 장비가 반영된 생성이 끝났는지 </summary>
        public bool IsBuilt { get; private set; }

        /// <summary> 외형이나 장비가 반영된 생성이 끝날 때마다 알린다 </summary>
        public event Action AppearanceBuilt;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if (_avatar == null)
            {
                Debug.LogError("[Avatar] DynamicCharacterAvatar 참조가 없다", this);
                enabled = false;
                return;
            }

            _avatar.BuildCharacterEnabled = false;
        }

        // 프리팹 설정과 무관하게 서버에서는 UMA가 Start에서 생성하지 못하게 한다
        protected override void OnServerBuildAwake()
        {
            if (_avatar == null)
            {
                return;
            }

            _avatar.BuildCharacterEnabled = false;
            _avatar.enabled               = false;
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if ((_avatar != null) && (_avatar.BuildCharacterEnabled))
            {
                Debug.LogWarning("[Avatar] DynamicCharacterAvatar의 Build Character Enabled를 꺼 둔다. 첫 생성은 AvatarView가 시작한다", this);
            }
        }
#endif

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 생성기 참조가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _tunedGenerator = null;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 캐릭터 기본 외형을 적용한다, 잘못된 값이면 직업 기본 외형으로 대신한다 </summary>
        public void ApplyAppearance(CharacterAppearance appearance, EPlayerClass playerClass)
        {
            if ((IsServerBuild) || (!enabled))
            {
                return;
            }

            if (!TryEnsureCatalog())
            {
                return;
            }

            _appearance  = ResolveAppearance(appearance, playerClass);
            _playerClass = playerClass;

            PrepareAvatar();
            IsBuilt = false;

            if (!_isBuildStarted)
            {
                _avatar.RacePreset = _catalog.RaceName;
                ApplyBaseWardrobe();
                ApplyHair();

                _isBuildStarted       = true;
                _isFirstUpdatePending = true;
                _avatar.BuildCharacterEnabled = true;  // 재생 중이면 여기서 첫 생성이 시작된다
                return;
            }

            ApplyHair();
            ApplyColors();
            ApplyDna();
            _avatar.BuildCharacter(true);
        }

        /// <summary> 장비 부위의 UMA 의상을 바꾼다, 빈 목록이면 그 부위를 벗긴다 </summary>
        public void ApplyGear(EEquipmentSlot slot, IReadOnlyList<string> recipeNames)
        {
            if ((IsServerBuild) || (!enabled))
            {
                return;
            }

            if (!TryEnsureCatalog())
            {
                return;
            }

            // 무기는 의상이 아니라 손 소켓에 붙이는 표현이 맡는다
            IReadOnlyList<string> wardrobeSlots = _catalog.GetWardrobeSlots(slot);
            if (wardrobeSlots.Count == 0)
            {
                return;
            }

            PrepareAvatar();

            for (int i = 0; i < wardrobeSlots.Count; ++i)
            {
                _avatar.ClearSlot(wardrobeSlots[i]);
            }

            int recipeCount = (recipeNames == null) ? 0 : recipeNames.Count;
            for (int i = 0; i < recipeCount; ++i)
            {
                _avatar.SetSlot(recipeNames[i]);
            }

            // 첫 생성 전이면 첫 생성에 함께 들어간다
            if (_isBuildStarted)
            {
                IsBuilt = false;
                _avatar.BuildCharacter(true);
            }
        }

        // UMA는 Start 전에 다른 코드가 초기화하는 경우를 지원한다. 먼저 초기화하면 뒤에 오는 UMA Start는
        // 같은 초기화를 다시 하지 않으므로, 이 컴포넌트와 UMA의 Start 실행 순서에 기대지 않는다
        private void PrepareAvatar()
        {
            _avatar.InitialStartup();
            SubscribeOnce();
            TuneGenerator();
        }

        private CharacterAppearance ResolveAppearance(CharacterAppearance appearance, EPlayerClass playerClass)
        {
            if ((appearance != null) && (appearance.IsWellFormed()) && (_catalog.IsHairAllowed(playerClass, appearance.HairId)))
            {
                return appearance;
            }

            Debug.LogWarning($"[Avatar] 외형 값이 현재 카탈로그와 맞지 않아 {playerClass} 기본 외형으로 표시한다", this);
            return _catalog.CreateDefaultAppearance(playerClass);
        }

        private void ApplyBaseWardrobe()
        {
            IReadOnlyList<string> recipes = _catalog.BaseWardrobeRecipes;
            for (int i = 0; i < recipes.Count; ++i)
            {
                _avatar.SetSlot(recipes[i]);
            }
        }

        // TODO(민성): 헤어 선택을 열면 _appearance.HairId와 HairColor로 레시피와 색을 고른다. 지금은 직업 기본 헤어만 허용된다
        private void ApplyHair()
        {
            if (!_catalog.TryGetClassHair(_playerClass, out AvatarCatalogSO.ClassHair hair))
            {
                return;
            }

            _avatar.SetSlot(_catalog.HairWardrobeSlot, hair.RecipeName);
        }

        // 이어서 생성이나 갱신을 부르므로 색마다 텍스처를 다시 만들지 않는다
        private void ApplyColors()
        {
            ApplyColor(_catalog.SkinColorName, _catalog.SkinTones.Evaluate(AvatarSliderRules.ToUnitValue(_appearance.SkinTone)));
            ApplyPaletteColor(_catalog.EyeColorName, _catalog.EyeColors, _appearance.EyeColor);
            ApplyPaletteColor(_catalog.BrowColorName, _catalog.BrowColors, _appearance.BrowColor);
            ApplyPaletteColor(_catalog.LipColorName, _catalog.LipColors, _appearance.LipColor);
        }

        private void ApplyPaletteColor(string colorName, IReadOnlyList<Color> palette, int paletteId)
        {
            int index = AvatarSliderRules.ClampPaletteIndex(paletteId, palette.Count);
            if (index < 0)
            {
                return;
            }

            ApplyColor(colorName, palette[index]);
        }

        // 종족 색을 복제해 채널 구성을 유지하고 기본 채널의 색만 바꾼다
        private void ApplyColor(string colorName, Color color)
        {
            OverlayColorData current = _avatar.GetColor(colorName);
            OverlayColorData next    = (current != null) ? current.Clone() : new OverlayColorData(DEFAULT_COLOR_CHANNELS);
            next.color = color;
            _avatar.SetRawColor(colorName, next, false);
        }

        private void ApplyDna()
        {
            Dictionary<string, DnaSetter> dna = _avatar.GetDNA();
            ApplySliders(dna, _catalog.FaceSliders, _appearance.Face);
            ApplySliders(dna, _catalog.BodySliders, _appearance.Body);
        }

        // 이름을 비운 슬라이더는 현재 종족에 대응 DNA가 없어 건너뛴다. 이름이 있는데 없으면 카탈로그 오타라 알린다
        private void ApplySliders(Dictionary<string, DnaSetter> dna, IReadOnlyList<AvatarCatalogSO.SliderDefinition> sliders,
            IReadOnlyList<int> values)
        {
            int count = Math.Min(sliders.Count, values.Count);
            for (int i = 0; i < count; ++i)
            {
                AvatarCatalogSO.SliderDefinition slider = sliders[i];
                if (string.IsNullOrEmpty(slider.DnaName))
                {
                    continue;
                }

                if (!dna.TryGetValue(slider.DnaName, out DnaSetter setter))
                {
                    Debug.LogWarning($"[Avatar] DNA '{slider.DnaName}'가 종족 {_catalog.RaceName}에 없다 ({slider.SliderId})", this);
                    continue;
                }

                setter.Set(AvatarSliderRules.ToDnaValue(values[i], slider.DnaMin, slider.DnaNeutral, slider.DnaMax, slider.Inverted));
            }
        }

        private void HandleCharacterUpdated(UMAData data)
        {
            if (_isFirstUpdatePending)
            {
                _isFirstUpdatePending = false;
                ApplyColors();
                ApplyDna();
                _avatar.UpdateColors();
                _avatar.ForceUpdate(true, true);
                return;
            }

            IsBuilt = true;
            NotifyBuilt();
        }

        // UMA 이벤트 안에서 불리므로 구독자 예외가 UMA의 나머지 콜백과 정리를 끊지 않게 막는다
        private void NotifyBuilt()
        {
            try
            {
                AppearanceBuilt?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        private bool TryEnsureCatalog()
        {
            if (_catalog != null)
            {
                return true;
            }

            if (!GameDB.IsInitialized)
            {
                Debug.LogError("[Avatar] GameDB가 초기화되기 전에 외형을 적용하려 했다", this);
                return false;
            }

            _catalog = GameDB.Avatar;
            return _catalog != null;
        }

        private void SubscribeOnce()
        {
            if (_isSubscribed)
            {
                return;
            }

            _avatar.umaData.OnCharacterUpdated += HandleCharacterUpdated;
            _isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if ((!_isSubscribed) || (_avatar == null))
            {
                return;
            }

            _avatar.umaData.OnCharacterUpdated -= HandleCharacterUpdated;
            _isSubscribed = false;
        }

        // 생성기는 모든 아바타가 함께 쓴다. UMA 원본 프리팹을 고치지 않으려고 실행 중에 넣는다
        private void TuneGenerator()
        {
            UMAGenerator generator = _avatar.umaGenerator;
            if ((generator == null) || (generator == _tunedGenerator))
            {
                return;
            }

            generator.IterationCount    = _catalog.GeneratorIterationCount;
            generator.InterFrameDelay   = _catalog.GeneratorInterFrameDelay;
            generator.processAllPending = _catalog.GeneratorProcessAllPending;
            _tunedGenerator = generator;
        }
    }
}
