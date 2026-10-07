// System
using System.Collections.Generic;
using System.Globalization;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Systems;

namespace Shinshi.UI
{
    /// <summary> CombatMeterClient가 받은 값을 전투 HUD 미터 창과 결과 화면 전투 기록 탭에 넣는다 </summary>
    // 일반전에는 메시지가 오지 않으므로 첫 CombatMeterSelfMessage를 받기 전에는 HUD 창을 숨긴다.
    // 파티 메시지는 접힘 여부와 무관하게 항상 오지만(설계 6장) 접힌 동안은 행을 갱신하지 않는다. 펼칠 때 SetExpanded가
    // 최신 Party 값으로 한 번 갱신한다. 이름은 CombatId -> 이름 캐시를 먼저 보고, 없으면 PlayerCombatState를 찾아 읽는다
    // (PartyHudBinder 방식). 결과 메시지는 이미 이름을 담아 와 캐시를 쓰지 않는다
    public sealed class CombatMeterHudBinder : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string EXPANDED_PREF_KEY   = "Shinshi.CombatMeter.Expanded";
        private const string UNKNOWN_MEMBER_NAME = "파티원";

        [SerializeField] private HotkeyRouter            _router     = null;
        [SerializeField] private HudCombatMeterView       _hudView    = null;
        [SerializeField] private HudCombatMeterResultView _resultView = null;

        private readonly List<int> _order = new List<int>(Constants.CombatMeter.MAX_PARTICIPANTS);

        // CombatId로 찾은 이름을 계속 보관한다. 파티원이 디스폰돼도 숫자 대신 마지막 이름을 보여준다
        private readonly Dictionary<int, string> _nameCache = new Dictionary<int, string>(Constants.CombatMeter.MAX_PARTICIPANTS);

        // 같은 값이면 CombatMeterViewRules가 새 문자열을 만들지 않고 이전 결과를 돌려준다
        private CombatMeterViewRules.FloatTextCache  _collapsedDamageCache;
        private CombatMeterViewRules.Float2TextCache _collapsedDpsCache;

        private readonly CombatMeterViewRules.FloatTextCache[]  _partyDamageCache = new CombatMeterViewRules.FloatTextCache[Constants.CombatMeter.MAX_PARTICIPANTS];
        private readonly CombatMeterViewRules.Float2TextCache[] _partyDpsCache    = new CombatMeterViewRules.Float2TextCache[Constants.CombatMeter.MAX_PARTICIPANTS];
        private readonly CombatMeterViewRules.FloatTextCache[]  _partyShareCache  = new CombatMeterViewRules.FloatTextCache[Constants.CombatMeter.MAX_PARTICIPANTS];

        private bool _isExpanded         = false;
        private bool _showEncounterTotal = false;
        private bool _hasEpochSeen       = false;
        private int  _lastEncounterEpoch = 0;

        private System.Func<bool> _onHotkeyToggle = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _onHotkeyToggle = OnHotkeyToggle;
            _isExpanded     = PlayerPrefs.GetInt(EXPANDED_PREF_KEY, 0) != 0;
        }

        private void OnEnable()
        {
            CombatMeterClient.SelfChanged       += OnSelfChanged;
            CombatMeterClient.PartyChanged      += OnPartyChanged;
            CombatMeterClient.FinalSelfReceived += OnFinalSelfReceived;
            CombatMeterClient.ResultReceived    += OnResultReceived;
            CombatMeterClient.RealtimeCleared   += OnRealtimeCleared;

            if (_router != null)
            {
                _router.Register(EHotkeyAction.CombatMeter, _onHotkeyToggle);
            }

            if (_hudView != null)
            {
                _hudView.PhaseToggleClicked += OnPhaseToggleClicked;
                _hudView.SelfRowClicked     += OnHudSelfRowClicked;
                _hudView.SetExpanded(_isExpanded);
                _hudView.SetWindowVisible(CombatMeterClient.HasSelf);
                RefreshPhaseToggleLabel();
            }

            if (_resultView != null)
            {
                _resultView.SelfRowClicked += OnResultSelfRowClicked;
            }

            if (CombatMeterClient.HasSelf)
            {
                RefreshCollapsedLine();
            }

            if (CombatMeterClient.HasParty)
            {
                RefreshPartyRows();
            }

            RefreshResult();
        }

        private void OnDisable()
        {
            CombatMeterClient.SelfChanged       -= OnSelfChanged;
            CombatMeterClient.PartyChanged      -= OnPartyChanged;
            CombatMeterClient.FinalSelfReceived -= OnFinalSelfReceived;
            CombatMeterClient.ResultReceived    -= OnResultReceived;
            CombatMeterClient.RealtimeCleared   -= OnRealtimeCleared;

            if (_router != null)
            {
                _router.Unregister(EHotkeyAction.CombatMeter, _onHotkeyToggle);
            }

            if (_hudView != null)
            {
                _hudView.PhaseToggleClicked -= OnPhaseToggleClicked;
                _hudView.SelfRowClicked     -= OnHudSelfRowClicked;
            }

            if (_resultView != null)
            {
                _resultView.SelfRowClicked -= OnResultSelfRowClicked;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private bool OnHotkeyToggle()
        {
            SetExpanded(!_isExpanded);
            return true;
        }

        private void SetExpanded(bool expanded)
        {
            _isExpanded = expanded;
            PlayerPrefs.SetInt(EXPANDED_PREF_KEY, expanded ? 1 : 0);

            if (_hudView == null)
            {
                return;
            }

            _hudView.SetExpanded(expanded);
            _hudView.SetWindowVisible(CombatMeterClient.HasSelf);
            if (expanded)
            {
                RefreshPartyRows();
            }
        }

        private void OnPhaseToggleClicked()
        {
            _showEncounterTotal = !_showEncounterTotal;
            RefreshPhaseToggleLabel();
            RefreshCollapsedLine();
            RefreshPartyRows();
        }

        private void OnHudSelfRowClicked()
        {
            if (_hudView == null)
            {
                return;
            }

            bool next = !_hudView.IsSelfBreakdownVisible();
            _hudView.SetSelfBreakdownVisible(next);
            if (next)
            {
                RefreshSelfBreakdown();
            }
        }

        private void OnResultSelfRowClicked()
        {
            if (_resultView == null)
            {
                return;
            }

            bool next = !_resultView.IsSelfDetailVisible();
            _resultView.SetSelfDetailVisible(next);
            if (next)
            {
                RefreshResultSelfDetail();
            }
        }

        private void OnSelfChanged(CombatMeterSelfMessage message)
        {
            if (_hudView == null)
            {
                return;
            }

            _hudView.SetWindowVisible(true);
            RefreshCollapsedLine();
            if (_hudView.IsSelfBreakdownVisible())
            {
                RefreshSelfBreakdown();
            }
        }

        private void OnPartyChanged(CombatMeterPartyMessage message)
        {
            RefreshPartyRows();
        }

        private void OnFinalSelfReceived(CombatMeterFinalSelfMessage message)
        {
            NoteEncounterEpoch(message.SessionEpoch);

            if ((_resultView != null) && (_resultView.IsSelfDetailVisible()))
            {
                RefreshResultSelfDetail();
            }
        }

        private void OnResultReceived(CombatMeterResultMessage message)
        {
            NoteEncounterEpoch(message.SessionEpoch);
            RefreshResult();
        }

        private void OnRealtimeCleared()
        {
            _nameCache.Clear();

            // 출정 출발 때의 전체 초기화면 결과도 비었으므로 결과 패널을 다시 그려 숨긴다
            RefreshResult();
        }

        // 최종/결과 메시지의 SessionEpoch가 바뀌면 새 보스전으로 보고 이름 캐시를 비운다
        private void NoteEncounterEpoch(int sessionEpoch)
        {
            if ((_hasEpochSeen) && (_lastEncounterEpoch == sessionEpoch))
            {
                return;
            }

            _hasEpochSeen       = true;
            _lastEncounterEpoch = sessionEpoch;
            _nameCache.Clear();
        }

        private void RefreshPhaseToggleLabel()
        {
            if (_hudView != null)
            {
                _hudView.SetPhaseToggleLabel(_showEncounterTotal ? "보스전 전체" : "현재 구간");
            }
        }

        private void RefreshCollapsedLine()
        {
            if ((_hudView == null) || (!CombatMeterClient.HasSelf))
            {
                return;
            }

            CombatMeterSelfMessage self    = CombatMeterClient.Self;
            float                  damage  = CombatMeterViewRules.SelectSelfDamage(in self, _showEncounterTotal);
            float                  seconds = CombatMeterViewRules.SelectSelfSeconds(in self, _showEncounterTotal);
            _hudView.SetCollapsed(CombatMeterViewRules.FormatDamageCached(damage, ref _collapsedDamageCache),
                CombatMeterViewRules.FormatDpsCached(damage, seconds, ref _collapsedDpsCache));
        }

        private void RefreshSelfBreakdown()
        {
            if ((_hudView == null) || (!CombatMeterClient.HasSelf))
            {
                return;
            }

            CombatMeterSlotStat[] slots     = CombatMeterClient.Self.Slots;
            int                   slotCount = _hudView.SelfSlotCount;
            for (int i = 0; i < slotCount; ++i)
            {
                if (i >= slots.Length)
                {
                    continue;
                }

                CombatMeterSlotStat stat = slots[i];
                _hudView.SetSelfSlot(i, CombatMeterViewRules.SlotLabel((ECombatMeterSlot)i),
                    CombatMeterViewRules.FormatDamage(stat.Damage), stat.Hits.ToString(CultureInfo.InvariantCulture),
                    CombatMeterViewRules.FormatDamage(stat.MaxHit));
            }
        }

        // 접힘 상태에서는 부르지 않는다(설계와 달리 행 갱신은 펼침에서만 필요하다). 펼칠 때 SetExpanded가 한 번 불러준다
        private void RefreshPartyRows()
        {
            if ((_hudView == null) || (!_isExpanded))
            {
                return;
            }

            CombatMeterPartyMessage party   = CombatMeterClient.Party;
            CombatMeterPartyEntry[] entries = party.Entries;
            TryGetLocalCombatId(out int localCombatId);
            CombatMeterViewRules.BuildPartyRowOrder(localCombatId, entries, _order);
            float total   = CombatMeterViewRules.SumPartyDamage(entries, _showEncounterTotal);
            float seconds = _showEncounterTotal ? party.EncounterElapsedSeconds : party.PhaseElapsedSeconds;

            int rowCount = _hudView.PartyRowCount;
            for (int i = 0; i < rowCount; ++i)
            {
                if (i >= _order.Count)
                {
                    _hudView.ClearPartyRow(i);
                    continue;
                }

                CombatMeterPartyEntry entry  = entries[_order[i]];
                bool                  isSelf = entry.CombatId == localCombatId;
                float                 damage = CombatMeterViewRules.SelectPartyDamage(in entry, _showEncounterTotal);
                float                 share  = CombatMeterViewRules.ComputeShare(damage, total);
                _hudView.SetPartyRow(i, ResolveName(entry.CombatId),
                    CombatMeterViewRules.FormatDamageCached(damage, ref _partyDamageCache[i]),
                    CombatMeterViewRules.FormatDpsCached(damage, seconds, ref _partyDpsCache[i]),
                    CombatMeterViewRules.FormatShareCached(share, ref _partyShareCache[i]), share / 100f, isSelf);
            }
        }

        private void RefreshResult()
        {
            if (_resultView == null)
            {
                return;
            }

            bool hasRecord = CombatMeterClient.HasResult;
            _resultView.SetHasRecord(hasRecord);
            if (!hasRecord)
            {
                return;
            }

            CombatMeterResultMessage result  = CombatMeterClient.Result;
            CombatMeterResultEntry[] entries = result.Entries;
            TryGetLocalCombatId(out int localCombatId);
            CombatMeterViewRules.BuildResultRowOrder(localCombatId, entries, _order);

            int rowCount = _resultView.RowCount;
            for (int i = 0; i < rowCount; ++i)
            {
                if (i >= _order.Count)
                {
                    _resultView.ClearRow(i);
                    continue;
                }

                CombatMeterResultEntry entry  = entries[_order[i]];
                bool                   isSelf = entry.CombatId == localCombatId;
                _resultView.SetRow(i, entry.DisplayName, CombatMeterViewRules.FormatDamage(entry.Damage),
                    CombatMeterViewRules.FormatDamage(entry.DamageTaken), entry.Deaths.ToString(CultureInfo.InvariantCulture),
                    entry.Revives.ToString(CultureInfo.InvariantCulture), isSelf);
            }

            if (_resultView.IsSelfDetailVisible())
            {
                RefreshResultSelfDetail();
            }
        }

        private void RefreshResultSelfDetail()
        {
            if ((_resultView == null) || (!CombatMeterClient.HasFinalSelf))
            {
                return;
            }

            CombatMeterFinalSelfMessage final = CombatMeterClient.FinalSelf;

            CombatMeterSlotStat[] slots     = final.Slots;
            int                   slotCount = _resultView.SelfSlotCount;
            for (int i = 0; i < slotCount; ++i)
            {
                if (i >= slots.Length)
                {
                    continue;
                }

                CombatMeterSlotStat stat = slots[i];
                _resultView.SetSelfSlot(i, CombatMeterViewRules.SlotLabel((ECombatMeterSlot)i),
                    CombatMeterViewRules.FormatDamage(stat.Damage), stat.Hits.ToString(CultureInfo.InvariantCulture),
                    CombatMeterViewRules.FormatDamage(stat.MaxHit));
            }

            CombatMeterPhaseStat[] phases        = final.Phases;
            int                    phaseRowCount = _resultView.PhaseRowCount;
            for (int i = 0; i < phaseRowCount; ++i)
            {
                if (i >= phases.Length)
                {
                    _resultView.ClearPhaseRow(i);
                    continue;
                }

                CombatMeterPhaseStat phase = phases[i];
                _resultView.SetPhaseRow(i, CombatMeterViewRules.PhaseLabel(phase.Phase), CombatMeterViewRules.FormatSeconds(phase.Seconds),
                    CombatMeterViewRules.FormatDamage(phase.Damage), CombatMeterViewRules.FormatDps(phase.Damage, phase.Seconds));
            }

            CombatMeterSourceAmount[] topDamageTaken = final.TopDamageTaken;
            int                       topCount       = _resultView.TopDamageTakenCount;
            for (int i = 0; i < topCount; ++i)
            {
                if (i >= topDamageTaken.Length)
                {
                    _resultView.ClearTopDamageTaken(i);
                    continue;
                }

                CombatMeterSourceAmount source = topDamageTaken[i];
                _resultView.SetTopDamageTaken(i, source.SourceId, CombatMeterViewRules.FormatDamage(source.Amount));
            }

            CombatMeterDeathEntry[] lastDeathRecap = final.LastDeathRecap;
            int                     deathCount     = _resultView.DeathRowCount;
            for (int i = 0; i < deathCount; ++i)
            {
                if (i >= lastDeathRecap.Length)
                {
                    _resultView.ClearDeathRow(i);
                    continue;
                }

                CombatMeterDeathEntry death = lastDeathRecap[i];
                _resultView.SetDeathRow(i, death.SourceId, CombatMeterViewRules.FormatDeathAmount(death.Amount, death.SecondsBeforeDeath));
            }
        }

        // 처음 찾은 이름을 _nameCache에 계속 보관한다. 그 뒤 디스폰돼도 숫자 대신 마지막 이름을 보여준다.
        // 끝내 한 번도 못 찾았으면(아직 스폰되지 않음 등) 캐시하지 않고 "파티원"을 보여준다
        private string ResolveName(int combatId)
        {
            if (_nameCache.TryGetValue(combatId, out string cachedName))
            {
                return cachedName;
            }

            // PartyHudBinder.CollectRemotePlayers 방식과 같다. 소유, 원격 모두 스폰 목록에서 CombatId로 찾는다
            PlayerCombatState combat = FindByCombatId(combatId);
            if (combat == null)
            {
                return UNKNOWN_MEMBER_NAME;
            }

            string name = combat.DisplayName;
            _nameCache[combatId] = name;
            return name;
        }

        private static PlayerCombatState FindByCombatId(int combatId)
        {
            foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
            {
                if ((identity != null) && (identity.TryGetComponent(out PlayerCombatState combat)) && (combat.CombatId == combatId))
                {
                    return combat;
                }
            }

            return null;
        }

        private static bool TryGetLocalCombatId(out int combatId)
        {
            NetworkIdentity local = NetworkClient.localPlayer;
            if ((local == null) || (!local.TryGetComponent(out PlayerCombatState combat)))
            {
                combatId = 0;
                return false;
            }

            combatId = combat.CombatId;
            return true;
        }
    }
}
