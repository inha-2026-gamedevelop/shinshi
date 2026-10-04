#if UNITY_EDITOR || DEVELOPMENT_BUILD
// Unity
using UnityEngine;

// 프로젝트
using Shinshi.Common;
using Shinshi.UI;

namespace Shinshi.Dev
{
    /// <summary>
    /// Dev_CombatHud 씬에서 일반전과 보스 HUD, 공명, 물약, 부활 대기, 거절 안내를 가짜 값으로 시험한다.
    /// 키: B 일반전/보스 전환, H 보스 피해, 1, 2 물약, G 공명 +20, Z, X, C 강림, K 쓰러짐, F 부활, T 거절 안내
    /// </summary>
    public sealed class CombatHudDemo : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  TIME_LIMIT       = 420f;
        private const int    WAVE_TOTAL       = 6;
        private const float  WAVE_SECONDS     = 70f;
        private const int    ENEMIES_PER_WAVE = 30;
        private const float  POTION_COOLDOWN  = 10f;
        private const int    RESONANCE_MAX    = 100;
        private const int    RESONANCE_START  = 60;
        private const int    RESONANCE_STEP   = 20;
        private const int    ASCENSION_COUNT  = 3;
        private const float  ASCENSION_CD     = 3f;
        private const float  REVIVE_WAIT      = 10f;
        private const int    BOSS_PHASE_TOTAL = 2;
        private const float  PHASE_TWO_RATIO  = 0.5f;
        private const int    SHARED_REVIVES   = 3;
        private const int    BOSS_POTION_MAX  = 9;
        private const float  BOSS_HIT_RATIO   = 0.07f;
        private const int    SMALL_POTION     = 5;
        private const int    LARGE_POTION     = 2;
        private const string BOSS_NAME        = "월백";

        private static readonly int[] G_AscensionCosts = { 60, 80, 40 };

        [Header("HUD")]
        [SerializeField] private GameObject         _waveGroup      = null;
        [SerializeField] private GameObject         _bossGroup      = null;
        [SerializeField] private HudWaveView        _wave           = null;
        [SerializeField] private HudBossView        _boss           = null;
        [SerializeField] private HudResonanceView   _resonance      = null;
        [SerializeField] private HudPotionView      _potion         = null;
        [SerializeField] private HudReviveStatusView   _revive         = null;
        [SerializeField] private HudRejectFeedbackView _reject         = null;
        [SerializeField] private HudSkillSlotView[] _ascensionSlots = null;

        private bool                  _isBoss       = false;
        private float                 _elapsed      = 0f;
        private float                 _bossHealth   = 1f;
        private int                   _resonanceNow = RESONANCE_START;
        private int                   _potionUsed   = 0;
        private int                   _smallPotion  = SMALL_POTION;
        private int                   _largePotion  = LARGE_POTION;
        private float                 _potionCd     = 0f;
        private int                   _revivesLeft  = SHARED_REVIVES;
        private float                 _reviveWait   = 0f;
        private EParticipantLifeState _life         = EParticipantLifeState.Alive;
        private int                   _rejectCursor = 0;

        private readonly float[] _ascensionCd = new float[ASCENSION_COUNT];

        private static readonly ERejectReason[] G_RejectSamples =
        {
            ERejectReason.Cooldown, ERejectReason.InsufficientEnergy, ERejectReason.PotionLimit,
            ERejectReason.NoRevives, ERejectReason.FullHealth
        };

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            ApplyMode();
            ApplyStatic();
        }

        private void Update()
        {
            HandleKeys();
            Tick(Time.deltaTime);
            Refresh();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.B))
            {
                _isBoss = !_isBoss;
                ApplyMode();
            }

            if (Input.GetKeyDown(KeyCode.H))
            {
                _bossHealth = Mathf.Max(0f, _bossHealth - BOSS_HIT_RATIO);
            }

            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                UsePotion(EPotionSize.Small);
            }

            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                UsePotion(EPotionSize.Large);
            }

            if (Input.GetKeyDown(KeyCode.G))
            {
                _resonanceNow = Mathf.Min(RESONANCE_MAX, _resonanceNow + RESONANCE_STEP);
            }

            if (Input.GetKeyDown(KeyCode.Z))
            {
                UseAscension(0);
            }

            if (Input.GetKeyDown(KeyCode.X))
            {
                UseAscension(1);
            }

            if (Input.GetKeyDown(KeyCode.C))
            {
                UseAscension(2);
            }

            if (Input.GetKeyDown(KeyCode.K) && (_life == EParticipantLifeState.Alive))
            {
                _life       = EParticipantLifeState.ReviveWaiting;
                _reviveWait = REVIVE_WAIT;
            }

            if (Input.GetKeyDown(KeyCode.F))
            {
                TryRevive();
            }

            if (Input.GetKeyDown(KeyCode.T))
            {
                _reject.Show(G_RejectSamples[_rejectCursor % G_RejectSamples.Length]);
                ++_rejectCursor;
            }
        }

        private void Tick(float dt)
        {
            _elapsed = Mathf.Min(TIME_LIMIT, _elapsed + dt);
            _potionCd = Mathf.Max(0f, _potionCd - dt);
            _reviveWait = Mathf.Max(0f, _reviveWait - dt);

            for (int i = 0; i < _ascensionCd.Length; ++i)
            {
                _ascensionCd[i] = Mathf.Max(0f, _ascensionCd[i] - dt);
            }
        }

        private void UsePotion(EPotionSize size)
        {
            if (_potionCd > 0f)
            {
                _reject.Show(ERejectReason.Cooldown);
                return;
            }

            if (_isBoss && (_potionUsed >= BOSS_POTION_MAX))
            {
                _reject.Show(ERejectReason.PotionLimit);
                return;
            }

            bool small = (size == EPotionSize.Small);
            int stock = small ? _smallPotion : _largePotion;

            if (stock <= 0)
            {
                _reject.Show(ERejectReason.Disabled);
                return;
            }

            if (small)
            {
                --_smallPotion;
            }
            else
            {
                --_largePotion;
            }

            ++_potionUsed;
            _potionCd = POTION_COOLDOWN;
        }

        private void UseAscension(int index)
        {
            // 서버와 같은 순서로 공명을 먼저, 공용 쿨다운을 나중에 본다
            if (_resonanceNow < G_AscensionCosts[index])
            {
                _reject.Show(ERejectReason.InsufficientEnergy, true);
                return;
            }

            if (_ascensionCd[index] > 0f)
            {
                _reject.Show(ERejectReason.Cooldown);
                return;
            }

            _resonanceNow -= G_AscensionCosts[index];

            for (int i = 0; i < _ascensionCd.Length; ++i)
            {
                _ascensionCd[i] = ASCENSION_CD;
            }
        }

        private void TryRevive()
        {
            if (_life == EParticipantLifeState.Alive)
            {
                return;
            }

            if (_reviveWait > 0f)
            {
                _reject.Show(ERejectReason.TooEarly);
                return;
            }

            if (_isBoss && (_revivesLeft <= 0))
            {
                _life = EParticipantLifeState.Spectating;
                _reject.Show(ERejectReason.NoRevives);
                return;
            }

            if (_isBoss)
            {
                --_revivesLeft;
            }

            _life = EParticipantLifeState.Alive;
        }

        private void ApplyMode()
        {
            if (_waveGroup != null)
            {
                _waveGroup.SetActive(!_isBoss);
            }

            if (_bossGroup != null)
            {
                _bossGroup.SetActive(_isBoss);
            }

            _revivesLeft = SHARED_REVIVES;
            _potionUsed  = 0;
        }

        private void ApplyStatic()
        {
            _boss.SetBossName(BOSS_NAME);

            for (int i = 0; i < G_AscensionCosts.Length; ++i)
            {
                _resonance.SetAscensionCost(i, G_AscensionCosts[i]);
            }
        }

        private void Refresh()
        {
            float remain = TIME_LIMIT - _elapsed;
            int waveIndex = Mathf.Min(WAVE_TOTAL, 1 + Mathf.FloorToInt(_elapsed / WAVE_SECONDS));
            float intoWave = _elapsed - ((waveIndex - 1) * WAVE_SECONDS);

            _wave.SetWave(waveIndex, WAVE_TOTAL);
            _wave.SetTimeRemaining(remain);
            _wave.SetEnemiesRemaining(Mathf.Max(0, ENEMIES_PER_WAVE - Mathf.FloorToInt(intoWave / WAVE_SECONDS * ENEMIES_PER_WAVE)));
            _wave.SetIntermission(0f);

            _boss.SetHealth(_bossHealth);
            _boss.SetPhase((_bossHealth > PHASE_TWO_RATIO) ? 1 : BOSS_PHASE_TOTAL, BOSS_PHASE_TOTAL);
            _boss.SetSharedRevives(_revivesLeft, SHARED_REVIVES);
            _boss.SetPotionUse(_potionUsed, BOSS_POTION_MAX);

            _resonance.SetResonance(_resonanceNow, RESONANCE_MAX);

            for (int i = 0; i < _ascensionCd.Length; ++i)
            {
                _resonance.SetAscensionCooling(i, _ascensionCd[i] > 0f);

                if ((_ascensionSlots != null) && (i < _ascensionSlots.Length) && (_ascensionSlots[i] != null))
                {
                    _ascensionSlots[i].SetCooldown(_ascensionCd[i], ASCENSION_CD);
                }
            }

            _potion.SetCount(EPotionSize.Small, _smallPotion);
            _potion.SetCount(EPotionSize.Large, _largePotion);
            _potion.SetSharedCooldown(_potionCd, POTION_COOLDOWN);

            _revive.SetState(_life, _reviveWait, _revivesLeft);
        }
    }
}
#endif
