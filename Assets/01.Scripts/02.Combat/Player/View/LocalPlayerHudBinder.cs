// System
using System;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.UI;

namespace Shinshi.Combat
{
    /// <summary>
    /// 소유 플레이어의 체력, 기력, 기술 쿨다운을 씬의 HUD 뷰에 넣는다. 원격 플레이어와 HUD가 없는 씬에서는 아무것도 하지 않는다
    /// </summary>
    [RequireComponent(typeof(PlayerInputState))]
    [RequireComponent(typeof(PlayerCombatState))]
    public sealed class LocalPlayerHudBinder : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        // 기술 키 Q, W, E, R, Z, X, C 순서, ESkillInputKey에서 None을 뺀 개수다
        private const int KEY_COUNT = 7;
        private const string STATUS_HUD_RESOURCE = "PF_PlayerStatusHud";

        private PlayerCombatState _combat = null;
        private PlayerInputState  _input  = null;
        private PlayerInputReader _reader = null;

        private HudPlayerStatusView _statusView = null;
        private GameObject _ownedStatusHud;
        private bool _warnedMissingStatusHud;
        private CharacterLevelSettings _levelSettings;
        private int _experienceBindingVersion = LocalPlayerSnapshot.UNBOUND_VERSION;

        private bool _isBound          = false;
        private bool _isResolvePending = false;

        // 칸마다 하나씩, 인덱스는 ESkillInputKey - 1이다
        private readonly HudSkillSlotView[]  _slotViews           = new HudSkillSlotView[KEY_COUNT];
        private readonly bool[]              _slotMapped          = new bool[KEY_COUNT];
        private readonly bool[]              _slotSeeded          = new bool[KEY_COUNT];
        private readonly bool[]              _slotCooling         = new bool[KEY_COUNT];
        private readonly ESkillRequestKind[] _slotKind            = new ESkillRequestKind[KEY_COUNT];
        private readonly byte[]              _slotIndex           = new byte[KEY_COUNT];
        private readonly float[]             _slotCatalogCooldown = new float[KEY_COUNT];
        private readonly float[]             _slotDuration        = new float[KEY_COUNT];
        private readonly double[]            _slotReadyAt         = new double[KEY_COUNT];
        private readonly int[]               _slotRequiredStacks  = new int[KEY_COUNT];

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            TryGetComponent(out _combat);
            TryGetComponent(out _input);
            TryGetComponent(out _reader);
        }

        private void OnDisable()
        {
            if (_isBound)
            {
                Unbind();
            }
        }

        // 소유권은 스폰 뒤에 정해지고 재접속으로 바뀔 수 있어 매 프레임 확인한다. 원격 플레이어는 여기서 바로 끝난다
        private void Update()
        {
            if ((_input == null) || (_combat == null) || (!_input.isOwned))
            {
                if (_isBound)
                {
                    Unbind();
                }

                return;
            }

            if (!_isBound)
            {
                Bind();
            }

            if ((_isResolvePending) && (GameDB.IsInitialized))
            {
                ResolveSlots(_combat.PlayerClass);
            }

            PushGauges();
            PushCooldowns();
            PushStacks();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Bind()
        {
            _isBound = true;
            FindHud();

            LocalPlayerView view = LocalPlayerView.Current;
            view.Attach(_combat);
            view.SubscribeSnapshot(OnSnapshot);
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (_reader != null)
            {
                _reader.SkillRejected += OnSkillRejected;
            }
        }

        private void Unbind()
        {
            _isBound = false;
            if (_statusView != null)
            {
                _statusView.ResetExperience();
                _statusView.SetClassStacks(ClassStackDisplay.Hidden);
            }
            for (int i = 0; i < KEY_COUNT; ++i)
            {
                if (_slotViews[i] != null)
                {
                    _slotViews[i].SetLocked(false);
                }
            }
            ReleaseOwnedStatusHud();
            _statusView = null;
            _levelSettings = null;
            _experienceBindingVersion = LocalPlayerSnapshot.UNBOUND_VERSION;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_reader != null)
            {
                _reader.SkillRejected -= OnSkillRejected;
            }

            LocalPlayerView view = LocalPlayerView.Current;
            view.UnsubscribeSnapshot(OnSnapshot);
            view.Detach(_combat);
            Array.Clear(_slotSeeded, 0, KEY_COUNT);
        }

        // HUD는 씬에 있고 네트워크 프리팹은 씬 객체를 직접 참조할 수 없다. 묶을 때와 씬이 로드될 때만 찾고 프레임마다 찾지 않는다.
        // 기술 칸은 이름 끝의 키 글자로 구분한다
        private void FindHud()
        {
            if (_statusView != null)
            {
                _statusView.ResetExperience();
            }
            ResolveStatusHud(FindObjectsByType<HudPlayerStatusView>(FindObjectsInactive.Include, FindObjectsSortMode.None));

            if (_statusView != null)
            {
                _statusView.ResetExperience();
            }
            Array.Clear(_slotViews, 0, KEY_COUNT);
            HudSkillSlotView[] slots = FindObjectsByType<HudSkillSlotView>(FindObjectsInactive.Include);
            for (int i = 0; i < slots.Length; ++i)
            {
                if (LocalPlayerHudRules.TryParseSlotKey(slots[i].gameObject.name, out ESkillInputKey key))
                {
                    _slotViews[(int)key - 1] = slots[i];
                }
            }
        }

        private void ResolveStatusHud(HudPlayerStatusView[] candidates)
        {
            for (int i = 0; i < candidates.Length; ++i)
            {
                HudPlayerStatusView candidate = candidates[i];
                if ((candidate == null) || !candidate.enabled || candidate.IsRuntimeOwned || ((_ownedStatusHud != null)
                    && candidate.transform.IsChildOf(_ownedStatusHud.transform)))
                {
                    continue;
                }
                ReleaseOwnedStatusHud();
                _statusView = candidate;
                return;
            }
            if (_ownedStatusHud != null)
            {
                _statusView = _ownedStatusHud.GetComponentInChildren<HudPlayerStatusView>(true);
                return;
            }
            GameObject prefab = Resources.Load<GameObject>(STATUS_HUD_RESOURCE);
            if (prefab != null)
            {
                _ownedStatusHud = Instantiate(prefab);
                _ownedStatusHud.name = "PlayerStatusHud";
                _ownedStatusHud.hideFlags = HideFlags.DontSave;
                _statusView = _ownedStatusHud.GetComponentInChildren<HudPlayerStatusView>(true);
                if (_statusView != null)
                {
                    _statusView.MarkRuntimeOwned();
                    return;
                }
                ReleaseOwnedStatusHud();
            }
            _statusView = null;
            if (!_warnedMissingStatusHud)
            {
                _warnedMissingStatusHud = true;
                Debug.LogWarning("[HUD] PF_PlayerStatusHud 리소스나 상태 표시가 없어 플레이어 상태를 표시할 수 없다");
            }
        }

        private void ReleaseOwnedStatusHud()
        {
            if (_ownedStatusHud == null)
            {
                return;
            }
            // Destroy가 프레임 끝으로 미뤄져도 다음 씬 콜백에서 이전 HUD를 다시 선택하지 않는다.
            HudPlayerStatusView retiring = _ownedStatusHud.GetComponentInChildren<HudPlayerStatusView>(true);
            if (retiring != null)
            {
                retiring.enabled = false;
            }
            _ownedStatusHud.SetActive(false);
            if (Application.isPlaying)
            {
                Destroy(_ownedStatusHud);
            }
            else
            {
                DestroyImmediate(_ownedStatusHud);
            }
            _ownedStatusHud = null;
        }

        // 직업이 바뀌거나 묶인 캐릭터가 바뀌면 칸이 보여 줄 기술을 다시 정한다
        private void OnSnapshot(LocalPlayerSnapshot snapshot)
        {
            if (snapshot.BindingVersion != _experienceBindingVersion)
            {
                _experienceBindingVersion = snapshot.BindingVersion;
                if (_statusView != null)
                {
                    _statusView.ResetExperience();
                }
            }
            if (!snapshot.IsBound)
            {
                return;
            }

            _isResolvePending = true;
            if (GameDB.IsInitialized)
            {
                ResolveSlots(snapshot.PlayerClass);
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single)
            {
                ReleaseOwnedStatusHud();
            }
            FindHud();
            _isResolvePending = true;
        }

        private void OnSkillRejected(ESkillRequestKind kind, byte slot, ERejectReason reason)
        {
            LocalPlayerView.Current.RaiseSkillRejected(kind, slot, reason);
        }

        private void ResolveSlots(EPlayerClass playerClass)
        {
            _isResolvePending = false;

            SkillCatalogSO catalog = GameDB.Skills;
            for (int i = 0; i < KEY_COUNT; ++i)
            {
                var key = (ESkillInputKey)(i + 1);
                _slotSeeded[i] = false;
                _slotMapped[i] = LocalPlayerHudRules.TryResolveSlot(catalog, playerClass, key, out _slotKind[i],
                    out _slotIndex[i], out _slotCatalogCooldown[i], out Sprite icon);
                _slotRequiredStacks[i] = _slotMapped[i]
                    ? SkillInputRules.GetRequiredStacks(catalog, playerClass, _slotKind[i], _slotIndex[i])
                    : 0;

                HudSkillSlotView view = _slotViews[i];
                if (view == null)
                {
                    continue;
                }

                if (!_slotMapped[i])
                {
                    view.SetCooldown(0f, 0f);
                    view.SetLocked(false);
                    continue;
                }

                // 아이콘 데이터가 없으면 HUD의 기본 그림을 그대로 둔다
                if (icon != null)
                {
                    view.SetIcon(icon);
                }
            }
        }

        // HUD가 값이 같으면 바로 돌아오고 바뀔 때만 글자를 다시 만든다. 최대치가 아직 복제되지 않았으면 기본 표시를 덮지 않는다

        private void PushGauges()
        {
            if (_statusView == null)
            {
                return;
            }

            LocalPlayerGauges gauges = LocalPlayerView.Current.ReadGauges();
            if (GameDB.IsInitialized)
            {
                if (_levelSettings == null)
                {
                    _levelSettings = GameDB.CharacterLevel.ToSettings();
                }
                _statusView.SetExperience(_levelSettings, LocalPlayerView.Current.TotalExperience);
            }
            else
            {
                _statusView.SetExperience(null, -1);
            }
            if (gauges.MaxHealth > 0f)
            {
                _statusView.SetHp(LocalPlayerHudRules.ToDisplayInt(gauges.Health, gauges.MaxHealth),
                    LocalPlayerHudRules.ToDisplayInt(gauges.MaxHealth, gauges.MaxHealth));
            }

            if (gauges.MaxEnergy > 0f)
            {
                _statusView.SetMp(LocalPlayerHudRules.ToDisplayInt(gauges.Energy, gauges.MaxEnergy),
                    LocalPlayerHudRules.ToDisplayInt(gauges.MaxEnergy, gauges.MaxEnergy));
            }
        }

        // 쿨다운 중인 칸만 매 프레임 갱신하고, 끝난 칸은 한 번만 비운다
        private void PushCooldowns()
        {
            double now = NetworkTime.time;
            for (int i = 0; i < KEY_COUNT; ++i)
            {
                HudSkillSlotView view = _slotViews[i];
                if ((!_slotMapped[i]) || (view == null))
                {
                    continue;
                }

                double readyAt = _input.GetSkillReadyAt(_slotKind[i], _slotIndex[i]);
                if ((!_slotSeeded[i]) || (readyAt != _slotReadyAt[i]))
                {
                    bool isNewCooldown = _slotSeeded[i];
                    _slotSeeded[i]   = true;
                    _slotReadyAt[i]  = readyAt;
                    _slotDuration[i] = LocalPlayerHudRules.ResolveDuration(readyAt, now, _slotCatalogCooldown[i], isNewCooldown);

                    // 처음 읽을 때 한 번은 칸의 기존 표시를 지우도록 쿨다운 중으로 시작한다
                    _slotCooling[i] = true;
                }

                float remaining = LocalPlayerHudRules.Remaining(readyAt, now);
                if (remaining > 0f)
                {
                    view.SetCooldown(remaining, _slotDuration[i]);
                    continue;
                }

                if (_slotCooling[i])
                {
                    _slotCooling[i] = false;
                    view.SetCooldown(0f, 0f);
                }
            }
        }

        // 스택은 서버가 복제한 표시 값이다. 칸 잠금은 입력이 요청을 보내지 않는 기준과 같고, 판정은 서버가 다시 한다.
        // 뷰가 값이 같으면 바로 돌아오므로 매 프레임 넣어도 바뀔 때만 다시 그린다
        private void PushStacks()
        {
            int stacks = _combat.ClassStacks;
            for (int i = 0; i < KEY_COUNT; ++i)
            {
                HudSkillSlotView view = _slotViews[i];
                if (view == null)
                {
                    continue;
                }

                view.SetLocked((_slotMapped[i]) && (!SkillInputRules.HasRequiredStacks(_slotRequiredStacks[i], stacks)));
            }

            if (_statusView == null)
            {
                return;
            }

            int max = GameDB.IsInitialized ? GameDB.Skills.ClassStackMax : 0;
            ClassStackDisplay display = ClassStackHudRules.Build(_combat.PlayerClass, stacks, max, _combat.ArcherElement);
            _statusView.SetClassStacks(in display);
        }
    }
}
