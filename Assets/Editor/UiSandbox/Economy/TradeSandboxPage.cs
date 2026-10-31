// System
using System;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Editor
{
    /// <summary> 실제 TradeView의 여섯 가지 이벤트를 가짜 서버 응답에 묶는다. 거래 단계 · 확정 · 수락 · 제안 목록을 편집해 단계별 버튼 상태를 눈으로 확인하며 시험 배치 대상이다 </summary>
    public sealed class TradeSandboxPage : EconomyPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PAGE_ID    = "economy_trade";
        public const string SCENE_PATH = "Assets/00.Scenes/Release/ShinshiTown.unity";

        public const string KEY_PARTNER          = "trade.partner";
        public const string KEY_STATE            = "trade.state";
        public const string KEY_REASON           = "trade.reason";
        public const string KEY_MY_LOCKED        = "trade.myLocked";
        public const string KEY_PARTNER_LOCKED   = "trade.partnerLocked";
        public const string KEY_MY_ACCEPTED      = "trade.myAccepted";
        public const string KEY_PARTNER_ACCEPTED = "trade.partnerAccepted";
        public const string KEY_COUNTDOWN        = "trade.countdownSeconds";
        public const string KEY_MY_CURRENCY      = "trade.myCurrency";
        public const string KEY_PARTNER_CURRENCY = "trade.partnerCurrency";
        public const string KEY_YEOPJEON         = "trade.yeopjeon";

        public const string OP_ADD      = "제안 품목 추가";
        public const string OP_REMOVE   = "제안 품목 제거";
        public const string OP_CURRENCY = "제안 엽전 변경";
        public const string OP_LOCK     = "거래 확정 전환";
        public const string OP_ACCEPT   = "거래 수락";
        public const string OP_CANCEL   = "거래 취소";
        public const string OP_REFRESH  = "거래 상태 갱신";

        private const long MAX_CURRENCY   = 999999999L;
        private const int  MAX_LINES      = 50;
        private const int  MAX_QUANTITY   = 9999;
        private const int  MAX_COUNTDOWN  = 600;
        private const int  FALLBACK_ROWS  = 1;
        private const int  ONE_UNIT       = 1;

        private static readonly string[] G_StateOptions  = Enum.GetNames(typeof(ETradeState));
        private static readonly string[] G_ReasonOptions = Enum.GetNames(typeof(ERejectReason));

        private static readonly EconomyListSchema G_InventorySchema = new EconomyListSchema(
            "trade.inventory", "내 보관함", MAX_LINES,
            new[]
            {
                new EconomyField("name", "품목", EEconomyFieldKind.Text, "품목 {n}"),
                new EconomyField("qty", "수량", EEconomyFieldKind.Int, "1", 0, MAX_QUANTITY)
            },
            new[]
            {
                new[] { "약초", "12" },
                new[] { "회복약", "5" },
                new[] { "낡은 검", "1" }
            });

        private static readonly EconomyListSchema G_MyOfferSchema = new EconomyListSchema(
            "trade.my", "내 제안 품목", MAX_LINES,
            new[]
            {
                new EconomyField("name", "품목", EEconomyFieldKind.Text, "품목 {n}"),
                new EconomyField("qty", "수량", EEconomyFieldKind.Int, "1", 0, MAX_QUANTITY)
            },
            new[]
            {
                new[] { "부적", "1" }
            });

        private static readonly EconomyListSchema G_PartnerOfferSchema = new EconomyListSchema(
            "trade.partnerOffer", "상대 제안 품목", MAX_LINES,
            new[]
            {
                new EconomyField("name", "품목", EEconomyFieldKind.Text, "품목 {n}"),
                new EconomyField("qty", "수량", EEconomyFieldKind.Int, "1", 0, MAX_QUANTITY)
            },
            new[]
            {
                new[] { "정수 조각", "3" }
            });

        private TradeView _view;
        private int       _closedCount;

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return PAGE_ID; }
        }

        public override string Title
        {
            get { return "1:1 거래"; }
        }

        public override string SourceScenePath
        {
            get { return SCENE_PATH; }
        }

        public override string Requirement
        {
            get { return string.Empty; }
        }

        public TradeView View
        {
            get { return _view; }
        }

        /// <summary> 거래가 끝난 상태에서 닫기를 눌러 창을 닫은 횟수 </summary>
        public int ClosedCount
        {
            get { return _closedCount; }
        }

        public static EconomyListSchema InventorySchema
        {
            get { return G_InventorySchema; }
        }

        public static EconomyListSchema MyOfferSchema
        {
            get { return G_MyOfferSchema; }
        }

        public static EconomyListSchema PartnerOfferSchema
        {
            get { return G_PartnerOfferSchema; }
        }

        /****************************************
        *                Methods
        ****************************************/

        public static ETradeState StateOf(UiSandboxScenarioSO scenario)
        {
            return Enum.TryParse(scenario.GetString(KEY_STATE, nameof(ETradeState.Editing)), out ETradeState state) ? state : ETradeState.Editing;
        }

        public static ERejectReason ReasonOf(UiSandboxScenarioSO scenario)
        {
            return Enum.TryParse(scenario.GetString(KEY_REASON, nameof(ERejectReason.None)), out ERejectReason reason) ? reason : ERejectReason.None;
        }

        /// <summary> 지금 단계에서 품목과 엽전을 바꿀 수 있는지. 실제 View 단추 규칙을 그대로 쓴다 </summary>
        public static bool CanEdit(UiSandboxScenarioSO scenario)
        {
            return TradeViewRules.ButtonsFor(StateOf(scenario), scenario.GetBool(KEY_MY_LOCKED, false), scenario.GetBool(KEY_MY_ACCEPTED, false)).CanEdit;
        }

        /// <summary> from 목록의 한 줄에서 한 개를 to 목록으로 옮긴다. 같은 이름이 있으면 합치고, 옮길 수 없으면 아무것도 바꾸지 않고 거짓을 돌려준다 </summary>
        public static bool MoveOne(UiSandboxScenarioSO scenario, EconomyListSchema from, int fromIndex, EconomyListSchema to)
        {
            if ((fromIndex < 0) || (fromIndex >= EconomyList.Count(scenario, from)))
            {
                return false;
            }

            int owned = EconomyList.GetInt(scenario, from, fromIndex, "qty");
            if (owned < ONE_UNIT)
            {
                return false;
            }

            string name = EconomyList.GetText(scenario, from, fromIndex, "name");
            int target = IndexOfName(scenario, to, name);
            if (target < 0)
            {
                if (!EconomyList.Add(scenario, to))
                {
                    return false;
                }

                target = EconomyList.Count(scenario, to) - 1;
                EconomyList.SetText(scenario, to, target, "name", name);
                EconomyList.SetInt(scenario, to, target, "qty", 0);
            }

            EconomyList.SetInt(scenario, to, target, "qty", EconomyList.GetInt(scenario, to, target, "qty") + ONE_UNIT);
            if (owned == ONE_UNIT)
            {
                EconomyList.RemoveAt(scenario, from, fromIndex);
            }
            else
            {
                EconomyList.SetInt(scenario, from, fromIndex, "qty", owned - ONE_UNIT);
            }

            return true;
        }

        public static int IndexOfName(UiSandboxScenarioSO scenario, EconomyListSchema schema, string name)
        {
            int count = EconomyList.Count(scenario, schema);
            for (int i = 0; i < count; ++i)
            {
                if (EconomyList.GetText(scenario, schema, i, "name") == name)
                {
                    return i;
                }
            }

            return -1;
        }

        protected override void SeedScenario(UiSandboxScenarioSO scenario)
        {
            EconomyScenarioValues.SeedString(scenario, KEY_PARTNER, "상대 모험가");
            EconomyScenarioValues.SeedString(scenario, KEY_STATE, nameof(ETradeState.Editing));
            EconomyScenarioValues.SeedString(scenario, KEY_REASON, nameof(ERejectReason.None));
            EconomyScenarioValues.SeedBool(scenario, KEY_MY_LOCKED, false);
            EconomyScenarioValues.SeedBool(scenario, KEY_PARTNER_LOCKED, false);
            EconomyScenarioValues.SeedBool(scenario, KEY_MY_ACCEPTED, false);
            EconomyScenarioValues.SeedBool(scenario, KEY_PARTNER_ACCEPTED, false);
            EconomyScenarioValues.SeedInt(scenario, KEY_COUNTDOWN, 3);
            EconomyScenarioValues.SeedLong(scenario, KEY_MY_CURRENCY, 500L);
            EconomyScenarioValues.SeedLong(scenario, KEY_PARTNER_CURRENCY, 0L);
            EconomyScenarioValues.SeedLong(scenario, KEY_YEOPJEON, 12500L);
            EconomyList.Seed(scenario, G_InventorySchema);
            EconomyList.Seed(scenario, G_MyOfferSchema);
            EconomyList.Seed(scenario, G_PartnerOfferSchema);
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            _closedCount = 0;
            _view = EconomyFixtureFactory.EnsureTrade(context, out string note);
            SetFixtureNote(note);
            if (_view == null)
            {
                context.SetStatus(note);
                return;
            }

            _view.InventoryRowClicked += OnInventoryRowClicked;
            _view.MyLineClicked       += OnMyLineClicked;
            _view.CurrencyEdited      += OnCurrencyEdited;
            _view.LockToggleRequested += OnLockToggleRequested;
            _view.AcceptRequested     += OnAcceptRequested;
            _view.CancelRequested     += OnCancelRequested;
        }

        protected override void OnApply(UiSandboxContext context)
        {
            ApplyView(context.Scenario);
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;
            EconomyScenarioValues.DrawString(scenario, KEY_PARTNER, "상대 이름");
            DrawNamePopup(scenario, KEY_STATE, "거래 단계", G_StateOptions);
            DrawNamePopup(scenario, KEY_REASON, "거절 · 실패 사유", G_ReasonOptions);
            EconomyScenarioValues.DrawBool(scenario, KEY_MY_LOCKED, "내가 확정함");
            EconomyScenarioValues.DrawBool(scenario, KEY_PARTNER_LOCKED, "상대가 확정함");
            EconomyScenarioValues.DrawBool(scenario, KEY_MY_ACCEPTED, "내가 수락함");
            EconomyScenarioValues.DrawBool(scenario, KEY_PARTNER_ACCEPTED, "상대가 수락함");
            EconomyScenarioValues.DrawInt(scenario, KEY_COUNTDOWN, "카운트다운(초)", 0, MAX_COUNTDOWN);
            EconomyScenarioValues.DrawLong(scenario, KEY_MY_CURRENCY, "내 제안 엽전", 0L, MAX_CURRENCY);
            EconomyScenarioValues.DrawLong(scenario, KEY_PARTNER_CURRENCY, "상대 제안 엽전", 0L, MAX_CURRENCY);
            EconomyScenarioValues.DrawLong(scenario, KEY_YEOPJEON, "내 엽전", 0L, MAX_CURRENCY);
            EconomyList.Draw(scenario, G_InventorySchema);
            EconomyList.Draw(scenario, G_MyOfferSchema);
            EconomyList.Draw(scenario, G_PartnerOfferSchema);

            if (GUILayout.Button("편집 값을 화면에 다시 적용"))
            {
                Apply(context);
            }

            if ((_view != null) && (!_view.IsOpen) && (GUILayout.Button("창 다시 열기")))
            {
                _view.SetOpen(true);
            }

            if (DrawRequestButton(context, "서버 갱신 요청 (가짜)"))
            {
                Send(OP_REFRESH, () => ApplyView(context.Scenario), reason => FailView(OP_REFRESH, reason));
            }
        }

        protected override void OnDetach()
        {
            if (_view != null)
            {
                _view.InventoryRowClicked -= OnInventoryRowClicked;
                _view.MyLineClicked       -= OnMyLineClicked;
                _view.CurrencyEdited      -= OnCurrencyEdited;
                _view.LockToggleRequested -= OnLockToggleRequested;
                _view.AcceptRequested     -= OnAcceptRequested;
                _view.CancelRequested     -= OnCancelRequested;
                EconomyFixtureFactory.Release(Context, _view);
            }

            _view = null;
        }

        // 열거형 이름을 문자열로 저장하는 팝업
        private static void DrawNamePopup(UiSandboxScenarioSO scenario, string key, string label, string[] options)
        {
            int current = Mathf.Max(0, Array.IndexOf(options, scenario.GetString(key, options[0])));
            int edited = EditorGUILayout.Popup(label, current, options);
            if (edited != current)
            {
                scenario.SetString(key, options[edited]);
            }
        }

        private int Capacity(string field)
        {
            ListRowView[] rows = EconomyUiBuilder.GetRefs<ListRowView>(_view, field);
            return Mathf.Max(FALLBACK_ROWS, (rows == null) ? 0 : rows.Length);
        }

        private List<ListRowData> BuildRows(UiSandboxScenarioSO scenario, EconomyListSchema schema, int capacity)
        {
            int count = Mathf.Min(EconomyList.Count(scenario, schema), capacity);
            List<ListRowData> rows = new List<ListRowData>(count);
            for (int i = 0; i < count; ++i)
            {
                rows.Add(new ListRowData(0,
                    EconomyList.GetText(scenario, schema, i, "name"),
                    EconomyList.GetInt(scenario, schema, i, "qty").ToString()));
            }

            return rows;
        }

        private void FailView(string operation, string reason)
        {
            ReportFailure(operation, reason);
            if (_view != null)
            {
                _view.SetStatus(reason);
            }
        }

        private void ApplyView(UiSandboxScenarioSO scenario)
        {
            if (_view == null)
            {
                return;
            }

            ETradeState state = StateOf(scenario);
            bool myLocked = scenario.GetBool(KEY_MY_LOCKED, false);
            bool partnerLocked = scenario.GetBool(KEY_PARTNER_LOCKED, false);
            bool myAccepted = scenario.GetBool(KEY_MY_ACCEPTED, false);
            bool partnerAccepted = scenario.GetBool(KEY_PARTNER_ACCEPTED, false);
            int countdown = scenario.GetInt(KEY_COUNTDOWN, 0);

            _view.SetPartnerName(scenario.GetString(KEY_PARTNER, string.Empty));
            _view.SetStatus(TradeViewRules.StatusText(state, ReasonOf(scenario), myLocked, myAccepted, partnerAccepted, countdown));
            _view.SetCountdown((state == ETradeState.Countdown) ? countdown.ToString() : string.Empty);
            _view.SetMyOffer(BuildRows(scenario, G_MyOfferSchema, Capacity("_myRows")), scenario.GetLong(KEY_MY_CURRENCY, 0L));
            _view.SetPartnerOffer(BuildRows(scenario, G_PartnerOfferSchema, Capacity("_partnerRows")), scenario.GetLong(KEY_PARTNER_CURRENCY, 0L));
            _view.SetInventory(BuildRows(scenario, G_InventorySchema, Capacity("_inventoryRows")), scenario.GetLong(KEY_YEOPJEON, 0L));
            _view.SetMarks(myLocked, partnerLocked, myAccepted, partnerAccepted);
            _view.SetButtons(TradeViewRules.ButtonsFor(state, myLocked, myAccepted), TradeViewRules.IsFinished(state));
        }

        // 제안이 바뀌면 양쪽의 확정과 수락이 풀리고 편집 단계로 돌아간다
        private static void ResetToEditing(UiSandboxScenarioSO scenario)
        {
            scenario.SetBool(KEY_MY_LOCKED, false);
            scenario.SetBool(KEY_PARTNER_LOCKED, false);
            scenario.SetBool(KEY_MY_ACCEPTED, false);
            scenario.SetBool(KEY_PARTNER_ACCEPTED, false);
            scenario.SetString(KEY_STATE, nameof(ETradeState.Editing));
            scenario.SetString(KEY_REASON, nameof(ERejectReason.None));
        }

        private void OnInventoryRowClicked(int row)
        {
            UiSandboxContext context = Context;
            if ((context == null) || (!CanEdit(context.Scenario)))
            {
                return;
            }

            string name = EconomyList.GetText(context.Scenario, G_InventorySchema, row, "name");
            Send(OP_ADD, () => CompleteMove(G_InventorySchema, row, name, G_MyOfferSchema, OP_ADD), reason => FailView(OP_ADD, reason));
        }

        private void OnMyLineClicked(int row)
        {
            UiSandboxContext context = Context;
            if ((context == null) || (!CanEdit(context.Scenario)))
            {
                return;
            }

            string name = EconomyList.GetText(context.Scenario, G_MyOfferSchema, row, "name");
            Send(OP_REMOVE, () => CompleteMove(G_MyOfferSchema, row, name, G_InventorySchema, OP_REMOVE), reason => FailView(OP_REMOVE, reason));
        }

        // 요청한 순간의 줄 이름을 들고 있다가, 응답이 올 때 그 줄이 그대로일 때만 옮긴다
        private void CompleteMove(EconomyListSchema from, int row, string requestedName, EconomyListSchema to, string operation)
        {
            UiSandboxScenarioSO scenario = Context.Scenario;
            if ((row < 0) || (row >= EconomyList.Count(scenario, from))
                || (EconomyList.GetText(scenario, from, row, "name") != requestedName))
            {
                FailView(operation, "요청한 뒤 줄이 바뀌었다");
                return;
            }

            if (!MoveOne(scenario, from, row, to))
            {
                FailView(operation, "옮길 수 없는 줄이다");
                return;
            }

            ResetToEditing(scenario);
            ApplyView(scenario);
            ReportSuccess(operation);
        }

        private void OnCurrencyEdited(long value)
        {
            UiSandboxContext context = Context;
            if ((context == null) || (!CanEdit(context.Scenario)))
            {
                return;
            }

            Send(OP_CURRENCY, () =>
            {
                ResetToEditing(Context.Scenario);
                Context.Scenario.SetLong(KEY_MY_CURRENCY, value);
                ApplyView(Context.Scenario);
                ReportSuccess(OP_CURRENCY);
            }, reason =>
            {
                FailView(OP_CURRENCY, reason);
                ApplyView(Context.Scenario);
            });
        }

        // 확정을 켜고 끄면 양쪽 확정 여부에 따라 단계가 정해진다
        private void OnLockToggleRequested()
        {
            if (Context == null)
            {
                return;
            }

            bool locked = !Context.Scenario.GetBool(KEY_MY_LOCKED, false);
            Send(OP_LOCK, () =>
            {
                UiSandboxScenarioSO scenario = Context.Scenario;
                scenario.SetBool(KEY_MY_LOCKED, locked);
                scenario.SetBool(KEY_MY_ACCEPTED, false);
                scenario.SetBool(KEY_PARTNER_ACCEPTED, false);
                scenario.SetString(KEY_STATE, StateAfterLock(scenario, locked, scenario.GetBool(KEY_PARTNER_LOCKED, false)).ToString());
                ApplyView(scenario);
                ReportSuccess(OP_LOCK);
            }, reason => FailView(OP_LOCK, reason));
        }

        private static ETradeState StateAfterLock(UiSandboxScenarioSO scenario, bool myLocked, bool partnerLocked)
        {
            if (myLocked && partnerLocked)
            {
                return (scenario.GetInt(KEY_COUNTDOWN, 0) > 0) ? ETradeState.Countdown : ETradeState.Accepting;
            }

            return (myLocked || partnerLocked) ? ETradeState.Locked : ETradeState.Editing;
        }

        private void OnAcceptRequested()
        {
            if (Context == null)
            {
                return;
            }

            Send(OP_ACCEPT, () =>
            {
                UiSandboxScenarioSO scenario = Context.Scenario;
                scenario.SetBool(KEY_MY_ACCEPTED, true);
                if (scenario.GetBool(KEY_PARTNER_ACCEPTED, false))
                {
                    scenario.SetString(KEY_STATE, nameof(ETradeState.Done));
                }

                ApplyView(scenario);
                ReportSuccess(OP_ACCEPT);
            }, reason => FailView(OP_ACCEPT, reason));
        }

        // 끝난 거래의 닫기 단추는 창만 닫고, 진행 중이면 취소를 요청한다
        private void OnCancelRequested()
        {
            UiSandboxContext context = Context;
            if ((context == null) || (_view == null))
            {
                return;
            }

            if (TradeViewRules.IsFinished(StateOf(context.Scenario)))
            {
                _view.SetOpen(false);
                ++_closedCount;
                context.SetStatus("거래 창이 닫혔다");
                return;
            }

            Send(OP_CANCEL, () =>
            {
                UiSandboxScenarioSO scenario = Context.Scenario;
                scenario.SetString(KEY_STATE, nameof(ETradeState.Cancelled));
                ApplyView(scenario);
                ReportSuccess(OP_CANCEL);
            }, reason => FailView(OP_CANCEL, reason));
        }
    }
}
