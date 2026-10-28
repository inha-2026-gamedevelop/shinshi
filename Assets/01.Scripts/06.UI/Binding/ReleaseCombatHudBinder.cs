// Unity
using UnityEngine;

// 서드파티
using Mirror;
using TMPro;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 출시 전투 HUD에 클라이언트가 실제로 받은 보스, 공명, 물약, 일반전 진행 상태를 연결한다 </summary>
    public sealed class ReleaseCombatHudBinder : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float REFRESH_SECONDS = 0.1f;

        [SerializeField] private HudBossView _bossView;
        [SerializeField] private TMP_Text _bossPhaseLabel;
        [SerializeField] private GameObject _bossRevivesLabel;
        [SerializeField] private GameObject _bossPotionLabel;
        [SerializeField] private HudResonanceView _resonanceView;
        [SerializeField] private HudPotionView _potionView;
        [SerializeField] private HudWaveView _waveView;
        [SerializeField] private DungeonProgressView _progressView;
        [SerializeField] private HudReviveStatusView _reviveView;

        // 강림 비용과 재사용 상태를 받지 않는 장식은 공명 게이지와 별도로 숨긴다.
        [SerializeField] private GameObject[] _unavailableRoots;

        private BossState _boss;
        private float _nextRefreshAt;
        private int _lastPhase = -1;
        private uint _lastPotionSequence;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            PotionClient.StateChanged += OnPotionState;
            _lastPotionSequence = 0;
            _nextRefreshAt = 0f;
            HideAll();
        }

        private void OnDisable()
        {
            PotionClient.StateChanged -= OnPotionState;
            _boss = null;
            HideAll();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefreshAt)
            {
                return;
            }
            _nextRefreshAt = Time.unscaledTime + REFRESH_SECONDS;
            if (!ClientRoles.IsDungeon())
            {
                _boss = null;
                HideAll();
                return;
            }

            RefreshBoss();
            RefreshResonance();
            RefreshProgress();
            float duration = GameDB.IsInitialized ? GameDB.Player.PotionSharedCooldown : 0f;
            ApplyPotions(PotionClient.HasState, PotionClient.Current, NetworkTime.time, duration);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnPotionState(PotionStateMessage state)
        {
            if ((state.Sequence == 0) || (state.Sequence == _lastPotionSequence)
                || (state.Reason != ERejectReason.None) || (state.IsPending))
            {
                return;
            }

            _lastPotionSequence = state.Sequence;
            UiSound.Play(EUiSoundGroup.Hud, EUiSoundAction.Potion);
        }

        private void RefreshBoss()
        {
            if (_boss == null)
            {
                // Mirror의 현재 관찰 목록만 사용한다. 서버 객체나 개발용 보스를 HUD에 섞지 않는다.
                foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
                {
                    if ((identity != null) && identity.TryGetComponent(out BossState candidate))
                    {
                        _boss = candidate;
                        _lastPhase = -1;
                        if (_bossView != null)
                        {
                            _bossView.SetBossName(DungeonEntryRules.BossName(candidate.BossId));
                        }
                        break;
                    }
                }
            }
            SetVisible(_bossView, _boss != null);
            if ((_boss == null) || (_bossView == null))
            {
                SetVisible(_bossPhaseLabel, false);
                return;
            }
            _bossView.SetHealth(_boss.HealthRate);

            // BossState.PhaseState는 전환 FSM 상태이지 단계 번호가 아니다. 미터의 서버 단계만 표시한다.
            int phase = CombatMeterClient.HasSelf ? CombatMeterClient.Self.Phase : 0;
            SetVisible(_bossPhaseLabel, phase > 0);
            if ((_bossPhaseLabel != null) && (phase > 0) && (phase != _lastPhase))
            {
                _bossPhaseLabel.SetText("{0}단계", phase);
                _lastPhase = phase;
            }
        }

        // 서버가 일반전 진행을 보낸 출정에서만 켠다. 보스전은 메시지가 오지 않아 숨긴 채로 둔다
        private void RefreshProgress()
        {
            bool visible = SortieClient.HasWaveProgress && (SortieClient.WaveProgress.WaveCount > 0);
            SetVisible(_progressView, visible);
            if ((!visible) || (_progressView == null))
            {
                return;
            }

            WaveProgressMessage progress = SortieClient.WaveProgress;
            for (int i = 0; i < progress.WaveCount; ++i)
            {
                _progressView.SetNode(i, DungeonProgressRules.NodeState(in progress, i), DungeonProgressRules.NodeKilled(in progress, i),
                    progress.GetPlanned(i));
            }

            _progressView.SetWaveCount(DungeonProgressRules.CurrentNumber(in progress), progress.WaveCount);
            _progressView.SetOverall(DungeonProgressRules.OverallRatio(in progress));
            _progressView.SetTime(DungeonProgressRules.RemainingSeconds(in progress, NetworkTime.time), progress.TimeLimitSeconds);
        }

        private void RefreshResonance()
        {
            LocalPlayerGauges gauges = LocalPlayerView.Current.ReadGauges();
            bool visible = LocalPlayerView.Current.Snapshot.IsBound && gauges.HasResonance;
            SetVisible(_resonanceView, visible);
            if (visible && (_resonanceView != null))
            {
                _resonanceView.SetResonance(Mathf.FloorToInt(gauges.Resonance), Mathf.FloorToInt(gauges.MaxResonance));
            }
        }

        private void ApplyPotions(bool hasState, PotionStateMessage state, double now, float duration)
        {
            SetVisible(_potionView, hasState);
            if (hasState && (_potionView != null))
            {
                _potionView.SetCount(EPotionSize.Small, state.SmallRemaining);
                _potionView.SetCount(EPotionSize.Large, state.LargeRemaining);
                float remaining = (float)System.Math.Max(0d, state.CooldownEndsAt - now);
                _potionView.SetSharedCooldown(remaining, duration);
            }
            bool showBossPotion = hasState && (_boss != null) && (state.BossLimit > 0);
            SetVisible(_bossPotionLabel, showBossPotion);
            if (showBossPotion && (_bossView != null))
            {
                _bossView.SetPotionUse(state.BossUsed, state.BossLimit);
            }
        }

        private void HideAll()
        {
            SetVisible(_bossView, false);
            SetVisible(_bossPhaseLabel, false);
            SetVisible(_bossPotionLabel, false);
            SetVisible(_resonanceView, false);
            SetVisible(_potionView, false);
            SetVisible(_progressView, false);
            _lastPhase = -1;

            // 일반전 진행은 _progressView가 서버 진행 메시지로 보여 주므로 예전 웨이브 표시는 쓰지 않는다.
            // 부활 대기, 공용 잔량은 아직 클라이언트 복제 계약이 없다.
            // 로컬 사망 시각으로 부활 가능 시점을 추측하거나 데모 수치를 남기지 않는다.
            SetVisible(_waveView, false);
            SetVisible(_reviveView, false);
            SetVisible(_bossRevivesLabel, false);
            if (_unavailableRoots != null)
            {
                for (int i = 0; i < _unavailableRoots.Length; ++i)
                {
                    SetVisible(_unavailableRoots[i], false);
                }
            }
        }

        private static void SetVisible(Component target, bool visible)
        {
            if (target != null)
            {
                SetVisible(target.gameObject, visible);
            }
        }

        private static void SetVisible(GameObject target, bool visible)
        {
            if ((target != null) && (target.activeSelf != visible))
            {
                target.SetActive(visible);
            }
        }
    }
}
