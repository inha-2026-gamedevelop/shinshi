// System
using System;
using System.Globalization;

// Unity
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 실제 View가 없는 미래 경제 화면의 계약 전용 페이지. 편집 값과 가짜 요청 결과만 다루고 실제 경제 규칙은 계산하지 않는다 </summary>
    public sealed class ContractOnlySandboxPage : EconomyPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string KEY_STATUS        = "contract.status";
        public const string KEY_LAST_OP       = "contract.lastOperation";
        public const string KEY_OK_COUNT      = "contract.okCount";
        public const string KEY_FAIL_COUNT    = "contract.failCount";
        public const string KEY_RESULT_NAME   = "contract.resultName";
        public const string KEY_RESULT_AMOUNT = "contract.resultAmount";
        public const string KEY_TARGET_INDEX  = "contract.targetIndex";

        public const string STATUS_IDLE = "대기";
        public const string STATUS_OK   = "완료";
        public const string STATUS_FAIL = "실패";

        private const string FIELD_PREFIX = "contract.field.";
        private const int    RESULT_MAX   = 30;
        private const int    TARGET_MAX   = 29;

        private static readonly EconomyListSchema G_ResultSchema = new EconomyListSchema(
            "contract.result", "결과 기록 (가짜 응답이 남기는 줄)", RESULT_MAX,
            new[]
            {
                new EconomyField("name", "이름", EEconomyFieldKind.Text, "결과 {n}"),
                new EconomyField("amount", "수량", EEconomyFieldKind.Int, "1", 0, int.MaxValue)
            },
            null);

        private readonly ContractOnlyDefinition _definition;

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return _definition.Id; }
        }

        public override string Title
        {
            get { return _definition.Title; }
        }

        public override string SourceScenePath
        {
            get { return string.Empty; }
        }

        public override string Requirement
        {
            get { return _definition.Requirement; }
        }

        public override bool HasView
        {
            get { return false; }
        }

        public ContractOnlyDefinition Definition
        {
            get { return _definition; }
        }

        public static EconomyListSchema ResultSchema
        {
            get { return G_ResultSchema; }
        }

        /****************************************
        *                Methods
        ****************************************/

        public ContractOnlySandboxPage(ContractOnlyDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            _definition = definition;
        }

        public static string FieldKey(string field)
        {
            return FIELD_PREFIX + field;
        }

        /// <summary> 정의된 번호의 가짜 요청을 보낸다. 시작하지 못하면 거짓이다 </summary>
        public bool RunOperation(int index)
        {
            UiSandboxContext context = Context;
            if ((context == null) || (index < 0) || (index >= _definition.Operations.Length))
            {
                return false;
            }

            EconomyContractOperation operation = _definition.Operations[index];
            string targetName = string.Empty;
            int targetIndex = -1;
            if (operation.Effect == EContractEffect.RemoveInput)
            {
                targetIndex = context.Scenario.GetInt(KEY_TARGET_INDEX);
                if ((targetIndex < 0) || (targetIndex >= EconomyList.Count(context.Scenario, _definition.InputList)))
                {
                    context.SetStatus($"{operation.Label}: 대상 줄 번호가 입력 목록 밖이다");
                    return false;
                }

                targetName = EconomyList.GetText(context.Scenario, _definition.InputList, targetIndex, "name");
            }

            string resultName = context.Scenario.GetString(KEY_RESULT_NAME);
            int resultAmount = context.Scenario.GetInt(KEY_RESULT_AMOUNT);
            return Send(operation.Label, () => Complete(operation, targetIndex, targetName, resultName, resultAmount), reason => Fail(operation, reason));
        }

        protected override void SeedScenario(UiSandboxScenarioSO scenario)
        {
            EconomyScenarioValues.SeedString(scenario, KEY_STATUS, STATUS_IDLE);
            EconomyScenarioValues.SeedString(scenario, KEY_LAST_OP, string.Empty);
            EconomyScenarioValues.SeedInt(scenario, KEY_OK_COUNT, 0);
            EconomyScenarioValues.SeedInt(scenario, KEY_FAIL_COUNT, 0);
            EconomyScenarioValues.SeedString(scenario, KEY_RESULT_NAME, "결과 항목");
            EconomyScenarioValues.SeedInt(scenario, KEY_RESULT_AMOUNT, 1);
            EconomyScenarioValues.SeedInt(scenario, KEY_TARGET_INDEX, 0);

            for (int i = 0; i < _definition.Fields.Length; ++i)
            {
                EconomyField field = _definition.Fields[i];
                string key = FieldKey(field.Key);
                if (scenario.Contains(key))
                {
                    continue;
                }

                switch (field.Kind)
                {
                    case EEconomyFieldKind.Int:
                        scenario.SetInt(key, ParseInt(field.DefaultValue));
                        break;
                    case EEconomyFieldKind.Long:
                        scenario.SetLong(key, ParseLong(field.DefaultValue));
                        break;
                    case EEconomyFieldKind.Bool:
                        scenario.SetBool(key, string.Equals(field.DefaultValue, "true", StringComparison.OrdinalIgnoreCase));
                        break;
                    default:
                        scenario.SetString(key, field.DefaultValue);
                        break;
                }
            }

            EconomyList.Seed(scenario, _definition.InputList);
            EconomyList.Seed(scenario, G_ResultSchema);
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            SetFixtureNote("계약 전용 화면이다. 실제 View가 없어 원본 씬을 열지 않고 편집 값과 가짜 요청 결과만 확인한다");
        }

        protected override void OnApply(UiSandboxContext context)
        {
            context.SetStatus("계약 전용 화면: 화면에 반영할 View가 없어 편집 값만 유지한다");
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;
            EditorGUILayout.HelpBox("계약 전용 (실제 화면 없음)\n" + _definition.Requirement, MessageType.Warning);

            for (int i = 0; i < _definition.Fields.Length; ++i)
            {
                DrawField(scenario, _definition.Fields[i]);
            }

            EconomyList.Draw(scenario, _definition.InputList);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("가짜 응답이 남기는 값", EditorStyles.boldLabel);
            EconomyScenarioValues.DrawString(scenario, KEY_RESULT_NAME, "결과 줄 이름");
            EconomyScenarioValues.DrawInt(scenario, KEY_RESULT_AMOUNT, "결과 줄 수량", 0, int.MaxValue);
            EconomyScenarioValues.DrawInt(scenario, KEY_TARGET_INDEX, "제거 대상 줄 번호", 0, TARGET_MAX);

            EditorGUILayout.Space();
            for (int i = 0; i < _definition.Operations.Length; ++i)
            {
                if (DrawRequestButton(context, _definition.Operations[i].Label))
                {
                    RunOperation(i);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("마지막 요청", scenario.GetString(KEY_LAST_OP));
            EditorGUILayout.LabelField("상태", scenario.GetString(KEY_STATUS));
            EditorGUILayout.LabelField("성공 / 실패 횟수",
                scenario.GetInt(KEY_OK_COUNT).ToString(CultureInfo.InvariantCulture) + " / " +
                scenario.GetInt(KEY_FAIL_COUNT).ToString(CultureInfo.InvariantCulture));
            EconomyList.Draw(scenario, G_ResultSchema);
        }

        protected override void OnDetach()
        {
        }

        private void Complete(EconomyContractOperation operation, int targetIndex, string targetName, string resultName, int resultAmount)
        {
            UiSandboxContext context = Context;
            if (context == null)
            {
                return;
            }

            UiSandboxScenarioSO scenario = context.Scenario;
            switch (operation.Effect)
            {
                case EContractEffect.AppendResult:
                    if (!EconomyList.Add(scenario, G_ResultSchema))
                    {
                        Fail(operation, "결과 기록이 가득 찼다");
                        return;
                    }

                    int last = EconomyList.Count(scenario, G_ResultSchema) - 1;
                    EconomyList.SetText(scenario, G_ResultSchema, last, "name", resultName);
                    EconomyList.SetInt(scenario, G_ResultSchema, last, "amount", resultAmount);
                    break;

                case EContractEffect.RemoveInput:
                    bool stale = (targetIndex >= EconomyList.Count(scenario, _definition.InputList)) ||
                                 (!string.Equals(EconomyList.GetText(scenario, _definition.InputList, targetIndex, "name"), targetName, StringComparison.Ordinal));
                    if ((stale) || (!EconomyList.RemoveAt(scenario, _definition.InputList, targetIndex)))
                    {
                        Fail(operation, "대상 줄이 이미 바뀌었다");
                        return;
                    }

                    break;
            }

            scenario.SetString(KEY_STATUS, STATUS_OK);
            scenario.SetString(KEY_LAST_OP, operation.Label);
            scenario.SetInt(KEY_OK_COUNT, scenario.GetInt(KEY_OK_COUNT) + 1);
            ReportSuccess(operation.Label);
        }

        private void Fail(EconomyContractOperation operation, string reason)
        {
            UiSandboxContext context = Context;
            if (context == null)
            {
                return;
            }

            UiSandboxScenarioSO scenario = context.Scenario;
            scenario.SetString(KEY_STATUS, STATUS_FAIL);
            scenario.SetString(KEY_LAST_OP, operation.Label);
            scenario.SetInt(KEY_FAIL_COUNT, scenario.GetInt(KEY_FAIL_COUNT) + 1);
            ReportFailure(operation.Label, reason);
        }

        private static void DrawField(UiSandboxScenarioSO scenario, EconomyField field)
        {
            string key = FieldKey(field.Key);
            switch (field.Kind)
            {
                case EEconomyFieldKind.Int:
                    EconomyScenarioValues.DrawInt(scenario, key, field.Label, (int)field.Min, (int)Math.Min(field.Max, int.MaxValue));
                    break;
                case EEconomyFieldKind.Long:
                    EconomyScenarioValues.DrawLong(scenario, key, field.Label, field.Min, field.Max);
                    break;
                case EEconomyFieldKind.Bool:
                    EconomyScenarioValues.DrawBool(scenario, key, field.Label);
                    break;
                default:
                    EconomyScenarioValues.DrawString(scenario, key, field.Label);
                    break;
            }
        }

        private static int ParseInt(string text)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
        }

        private static long ParseLong(string text)
        {
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0L;
        }
    }
}
