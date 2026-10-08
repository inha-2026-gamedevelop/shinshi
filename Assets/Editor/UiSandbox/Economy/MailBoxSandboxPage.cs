// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.UI;

namespace Shinshi.Editor
{
    /// <summary> 실제 MailBoxView에 편집 가능한 우편 목록을 넣고, 행 선택 · 수령 · 쪽 이동 이벤트를 가짜 서버 응답에 묶는다. 시험 배치 대상이다 </summary>
    public sealed class MailBoxSandboxPage : EconomyPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PAGE_ID    = "economy_mailbox";
        public const string SCENE_PATH = "Assets/00.Scenes/Dev_HUD/Dev_MailBox.unity";
        public const string KEY_NOW    = "mail.serverNowMs";
        public const string KEY_PAGE   = "mail.page";

        public const string OP_CLAIM = "우편 수령";
        public const string OP_PAGE  = "우편 목록 조회";

        private const long   DEFAULT_NOW  = 1760000000000L;
        private const long   MS_PER_HOUR  = 3600000L;
        private const int    MAX_MAILS    = 200;
        private const int    MAX_PAGE     = 1000;
        private const string NO_REWARD    = "없음";
        private const string ALL_GRANT    = "-1";

        private static readonly string[] G_SourceOptions = { "SessionReward", "AuctionReward", "MarketReturn", "OperationsPayout" };
        private static readonly string[] G_AssetOptions  = { NO_REWARD, "Currency", "Tickets", "Essence", "Shards", "AccountItem" };

        private static readonly EconomyListSchema G_MailSchema = new EconomyListSchema(
            "mail.entry", "우편 목록", MAX_MAILS,
            new[]
            {
                new EconomyField("id", "우편 ID", EEconomyFieldKind.Text, "mail.{n}"),
                new EconomyField("source", "출처", EEconomyFieldKind.Text, G_SourceOptions[0], 0, 0, G_SourceOptions),
                new EconomyField("ageHours", "발신 후 경과(시간)", EEconomyFieldKind.Long, "1", 0, 99999),
                new EconomyField("expiresHours", "만료까지(시간, 0 이하는 만료)", EEconomyFieldKind.Long, "72", -99999, 99999),
                new EconomyField("requiresCharacter", "캐릭터 필요", EEconomyFieldKind.Bool, "false"),
                new EconomyField("kind1", "보상1 종류", EEconomyFieldKind.Text, "Currency", 0, 0, G_AssetOptions),
                new EconomyField("asset1", "보상1 에셋 ID", EEconomyFieldKind.Text, string.Empty),
                new EconomyField("qty1", "보상1 수량", EEconomyFieldKind.Long, "100", 0, 999999999),
                new EconomyField("kind2", "보상2 종류", EEconomyFieldKind.Text, NO_REWARD, 0, 0, G_AssetOptions),
                new EconomyField("asset2", "보상2 에셋 ID", EEconomyFieldKind.Text, string.Empty),
                new EconomyField("qty2", "보상2 수량", EEconomyFieldKind.Long, "0", 0, 999999999),
                new EconomyField("grant1", "한 번에 수령되는 보상1 수량 (음수는 전부, 모의 응답)", EEconomyFieldKind.Long, ALL_GRANT, -1, 999999999),
                new EconomyField("grant2", "한 번에 수령되는 보상2 수량 (음수는 전부, 모의 응답)", EEconomyFieldKind.Long, ALL_GRANT, -1, 999999999)
            },
            new[]
            {
                new[] { "mail.1", "SessionReward", "2", "72", "false", "Currency", string.Empty, "1500", NO_REWARD, string.Empty, "0" },
                new[] { "mail.2", "AuctionReward", "30", "10", "false", "AccountItem", "약초", "3", "Currency", string.Empty, "500", ALL_GRANT, "200" },
                new[] { "mail.3", "MarketReturn", "100", "-1", "true", "Essence", string.Empty, "7", NO_REWARD, string.Empty, "0" }
            });

        private MailBoxView _view;
        private string      _selectedId;

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return PAGE_ID; }
        }

        public override string Title
        {
            get { return "우편함"; }
        }

        public override string SourceScenePath
        {
            get { return SCENE_PATH; }
        }

        public override string Requirement
        {
            get { return string.Empty; }
        }

        public MailBoxView View
        {
            get { return _view; }
        }

        /// <summary> 지금 고른 우편 ID. 고른 우편이 없으면 null </summary>
        public string SelectedId
        {
            get { return _selectedId; }
        }

        public static EconomyListSchema MailSchema
        {
            get { return G_MailSchema; }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 시나리오의 우편 목록을 실제 계약 객체로 바꾼다. 만료 시각은 서버 시각 기준이다 </summary>
        public static InboxEntry[] BuildEntries(UiSandboxScenarioSO scenario)
        {
            long now = scenario.GetLong(KEY_NOW, DEFAULT_NOW);
            int count = EconomyList.Count(scenario, G_MailSchema);
            InboxEntry[] entries = new InboxEntry[count];
            for (int i = 0; i < count; ++i)
            {
                List<InboxReward> rewards = new List<InboxReward>();
                AddReward(rewards, scenario, i, "kind1", "asset1", "qty1");
                AddReward(rewards, scenario, i, "kind2", "asset2", "qty2");
                entries[i] = new InboxEntry(
                    EconomyList.GetText(scenario, G_MailSchema, i, "id"),
                    EconomyList.GetText(scenario, G_MailSchema, i, "source"),
                    now - (EconomyList.GetLong(scenario, G_MailSchema, i, "ageHours") * MS_PER_HOUR),
                    now + (EconomyList.GetLong(scenario, G_MailSchema, i, "expiresHours") * MS_PER_HOUR),
                    rewards.ToArray(),
                    EconomyList.GetBool(scenario, G_MailSchema, i, "requiresCharacter"));
            }

            return entries;
        }

        /// <summary> 우편 ID로 시나리오 목록의 줄 번호를 찾는다. 없으면 -1 </summary>
        public static int IndexOfId(UiSandboxScenarioSO scenario, string inboxId)
        {
            int count = EconomyList.Count(scenario, G_MailSchema);
            for (int i = 0; i < count; ++i)
            {
                if (EconomyList.GetText(scenario, G_MailSchema, i, "id") == inboxId)
                {
                    return i;
                }
            }

            return -1;
        }

        protected override void SeedScenario(UiSandboxScenarioSO scenario)
        {
            EconomyScenarioValues.SeedLong(scenario, KEY_NOW, DEFAULT_NOW);
            EconomyScenarioValues.SeedInt(scenario, KEY_PAGE, 0);
            EconomyList.Seed(scenario, G_MailSchema);
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            _selectedId = null;
            _view = EconomyFixtureFactory.EnsureMailBox(context, out string note);
            SetFixtureNote(note);
            if (_view == null)
            {
                context.SetStatus(note);
                return;
            }

            _view.RowSelected        += OnRowSelected;
            _view.ClaimRequested     += OnClaimRequested;
            _view.PageDeltaRequested += OnPageDeltaRequested;
        }

        protected override void OnApply(UiSandboxContext context)
        {
            ApplyView(context.Scenario);
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;
            EconomyScenarioValues.DrawLong(scenario, KEY_NOW, "서버 시각 (Unix ms)", 0L, long.MaxValue / 2L);
            EconomyScenarioValues.DrawInt(scenario, KEY_PAGE, "현재 쪽 (0부터)", 0, MAX_PAGE);
            EconomyList.Draw(scenario, G_MailSchema);

            EditorGUILayout.LabelField("고른 우편", string.IsNullOrEmpty(_selectedId) ? "(없음)" : _selectedId);
            if (GUILayout.Button("편집 값을 화면에 다시 적용"))
            {
                Apply(context);
            }

            if ((_view != null) && (!_view.IsOpen) && (GUILayout.Button("창 다시 열기")))
            {
                _view.SetOpen(true);
            }

            if (DrawRequestButton(context, "목록 새로고침 요청 (가짜)"))
            {
                Send(OP_PAGE, () => ApplyView(context.Scenario), reason => FailView(OP_PAGE, reason));
            }
        }

        protected override void OnDetach()
        {
            if (_view != null)
            {
                _view.RowSelected        -= OnRowSelected;
                _view.ClaimRequested     -= OnClaimRequested;
                _view.PageDeltaRequested -= OnPageDeltaRequested;
                EconomyFixtureFactory.Release(Context, _view);
            }

            _view       = null;
            _selectedId = null;
        }

        private static void AddReward(List<InboxReward> rewards, UiSandboxScenarioSO scenario, int index, string kindField, string assetField, string quantityField)
        {
            string kind = EconomyList.GetText(scenario, G_MailSchema, index, kindField);
            if ((string.IsNullOrEmpty(kind)) || (kind == NO_REWARD) || (EconomyList.GetLong(scenario, G_MailSchema, index, quantityField) <= 0L))
            {
                return;
            }

            rewards.Add(new InboxReward(kind, EconomyList.GetText(scenario, G_MailSchema, index, assetField), "None",
                EconomyList.GetLong(scenario, G_MailSchema, index, quantityField)));
        }

        // 쪽 번호를 목록 길이에 맞게 줄인 뒤 한 쪽 분량을 화면에 채운다
        private void ApplyView(UiSandboxScenarioSO scenario)
        {
            if (_view == null)
            {
                return;
            }

            InboxEntry[] entries = BuildEntries(scenario);
            int pageSize = Mathf.Max(1, _view.RowCapacity);
            int pageCount = Mathf.Max(1, MailBoxViewRules.PageCount(entries.Length, pageSize));
            int page = Mathf.Clamp(scenario.GetInt(KEY_PAGE, 0), 0, pageCount - 1);
            if (page != scenario.GetInt(KEY_PAGE, 0))
            {
                scenario.SetInt(KEY_PAGE, page);
            }

            MailBoxViewRules.PageRange(entries.Length, pageSize, page, out int start, out int count);
            List<ListRowData> rows = new List<ListRowData>(count);
            MailBoxViewRules.BuildRows(entries, scenario.GetLong(KEY_NOW, DEFAULT_NOW), start, count, rows);

            _selectedId = null;
            _view.SetList(rows, page, pageCount);
            _view.ShowDetail(null, null, null, null);
            _view.SetStatus(string.Empty);
        }

        private void FailView(string operation, string reason)
        {
            ReportFailure(operation, reason);
            if (_view != null)
            {
                _view.SetStatus(reason);
            }
        }

        private void OnRowSelected(int rowIndex)
        {
            UiSandboxContext context = Context;
            if ((context == null) || (_view == null))
            {
                return;
            }

            InboxEntry[] entries = BuildEntries(context.Scenario);
            int pageSize = Mathf.Max(1, _view.RowCapacity);
            int absolute = (context.Scenario.GetInt(KEY_PAGE, 0) * pageSize) + rowIndex;
            if ((rowIndex < 0) || (absolute >= entries.Length))
            {
                return;
            }

            ShowDetail(entries[absolute]);
        }

        private void ShowDetail(InboxEntry entry)
        {
            _selectedId = entry.InboxId;
            _view.ShowDetail(MailBoxViewRules.TitleOf(entry.SourceKind), MailBoxViewRules.SenderOf(entry.SourceKind),
                MailBoxViewRules.DateText(entry.CreatedAtUnixMs), MailBoxViewRules.Body(entry));
        }

        private void OnClaimRequested()
        {
            if ((Context == null) || (string.IsNullOrEmpty(_selectedId)))
            {
                return;
            }

            string claimedId = _selectedId;
            Send(OP_CLAIM, () => CompleteClaim(claimedId), reason => FailView(OP_CLAIM, reason));
        }

        // 응답이 올 때 목록이 바뀌었을 수 있어 줄 번호가 아니라 우편 ID로 다시 찾는다
        private void CompleteClaim(string inboxId)
        {
            UiSandboxContext context = Context;
            if (context == null)
            {
                return;
            }

            int index = IndexOfId(context.Scenario, inboxId);
            if (index < 0)
            {
                FailView(OP_CLAIM, "이미 목록에서 사라진 우편이다");
                return;
            }

            if (EconomyList.GetLong(context.Scenario, G_MailSchema, index, "expiresHours") <= 0L)
            {
                FailView(OP_CLAIM, "만료된 우편이다");
                return;
            }

            // 모의 응답: 보상별 수령량은 편집 값(grant)이다. 남은 보상이 하나라도 있으면 우편을 지우지 않고 잔여량만 남긴다
            bool remains = ConsumeReward(context.Scenario, index, "kind1", "qty1", "grant1");
            remains |= ConsumeReward(context.Scenario, index, "kind2", "qty2", "grant2");
            if (!remains)
            {
                EconomyList.RemoveAt(context.Scenario, G_MailSchema, index);
                ApplyView(context.Scenario);
                ReportSuccess(OP_CLAIM);
                if (_view != null)
                {
                    _view.SetStatus("수령을 마쳤다");
                }

                return;
            }

            ApplyView(context.Scenario);
            ReportSuccess(OP_CLAIM + " (일부)");
            if (_view != null)
            {
                InboxEntry[] entries = BuildEntries(context.Scenario);
                ShowDetail(entries[index]);
                _view.SetStatus("일부만 수령했다. 남은 보상은 우편에 남아 있다");
            }
        }

        // 보상 하나의 이번 수령량을 반영한다. 아직 남은 수량이 있으면 참이다
        private static bool ConsumeReward(UiSandboxScenarioSO scenario, int index, string kindField, string quantityField, string grantField)
        {
            string kind = EconomyList.GetText(scenario, G_MailSchema, index, kindField);
            long quantity = EconomyList.GetLong(scenario, G_MailSchema, index, quantityField);
            if ((string.IsNullOrEmpty(kind)) || (kind == NO_REWARD) || (quantity <= 0L))
            {
                return false;
            }

            long grant = EconomyList.GetLong(scenario, G_MailSchema, index, grantField);
            if ((grant < 0L) || (grant >= quantity))
            {
                EconomyList.SetLong(scenario, G_MailSchema, index, quantityField, 0L);
                return false;
            }

            EconomyList.SetLong(scenario, G_MailSchema, index, quantityField, quantity - grant);
            return true;
        }

        private void OnPageDeltaRequested(int delta)
        {
            if (Context == null)
            {
                return;
            }

            int targetPage = Mathf.Max(0, Context.Scenario.GetInt(KEY_PAGE, 0) + delta);
            Send(OP_PAGE, () => MovePage(targetPage), reason => FailView(OP_PAGE, reason));
        }

        // 요청한 순간에 정한 목표 쪽으로 옮기며, 응답을 기다리는 사이 바뀐 쪽 값은 읽지 않는다
        private void MovePage(int targetPage)
        {
            UiSandboxContext context = Context;
            if (context == null)
            {
                return;
            }

            context.Scenario.SetInt(KEY_PAGE, targetPage);
            ApplyView(context.Scenario);
            ReportSuccess(OP_PAGE);
        }
    }
}
