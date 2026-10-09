using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary>
    /// View가 아직 없는 문서 요구사항용 계약 전용 페이지. 씬 객체 없이 편집 가능한 입력, 로컬 검증, 모의 요청과 응답 표시만 제공한다.
    /// 실제 화면 구현이 생기면 이 페이지가 아니라 해당 View 페이지로 옮겨야 한다
    /// </summary>
    public sealed class ContractOnlyPage : PresentationPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string CONTRACT_HELP = "계약 전용 페이지: 이 화면의 View 구현이 없어 씬 객체를 구동하지 않는다. 입력과 모의 요청 응답만 검증하며 실제 화면 배치와 연출은 확인할 수 없다";

        private const string LOCAL_REJECT = "로컬 검증 거절: ";
        private const string NO_LOCAL     = "없음";
        private const string SECTION_OPS  = "모의 요청";

        private readonly ContractSpec _spec;
        private string                _localResult = NO_LOCAL;

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return _spec.Id; }
        }

        public override string Title
        {
            get { return _spec.Title + " (계약 전용)"; }
        }

        public override string SourceScenePath
        {
            get { return string.Empty; }
        }

        public override string Requirement
        {
            get { return _spec.Requirement + " " + CONTRACT_HELP + ". " + PresentationNotes.NETWORK_NOTE; }
        }

        public override bool HasView
        {
            get { return false; }
        }

        public ContractSpec Spec
        {
            get { return _spec; }
        }

        public string LocalResult
        {
            get { return _localResult; }
        }

        /****************************************
        *                Methods
        ****************************************/

        public ContractOnlyPage(ContractSpec spec)
        {
            _spec = spec;
        }

        /// <summary> 입력 한 칸을 검증하지 않고 시나리오에서 문자열로 읽는다. 검증 함수와 테스트가 같은 키를 쓴다 </summary>
        public static string Describe(UiSandboxScenarioSO scenario, ContractField field)
        {
            switch (field.Kind)
            {
                case EContractFieldKind.Int:
                case EContractFieldKind.Choice:
                    return scenario.GetInt(field.Key).ToString();
                case EContractFieldKind.Float:
                    return scenario.GetFloat(field.Key).ToString("0.##");
                case EContractFieldKind.Bool:
                    return scenario.GetBool(field.Key).ToString();
                case EContractFieldKind.Text:
                    return scenario.GetString(field.Key);
                default:
                    return string.Empty;
            }
        }

        protected override void Seed(UiSandboxScenarioSO scenario)
        {
            for (int i = 0; i < _spec.Fields.Length; ++i)
            {
                ContractField field = _spec.Fields[i];
                switch (field.Kind)
                {
                    case EContractFieldKind.Int:
                    case EContractFieldKind.Choice:
                        scenario.SeedInt(field.Key, field.IntDefault);
                        break;
                    case EContractFieldKind.Float:
                        scenario.SeedFloat(field.Key, field.FloatDefault);
                        break;
                    case EContractFieldKind.Bool:
                        scenario.SeedBool(field.Key, field.BoolDefault);
                        break;
                    case EContractFieldKind.Text:
                        scenario.SeedString(field.Key, field.TextDefault);
                        break;
                }
            }
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            _localResult = NO_LOCAL;
        }

        protected override void OnApply(UiSandboxContext context)
        {
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            EditorGUILayout.HelpBox(CONTRACT_HELP, MessageType.Info);

            UiSandboxScenarioSO scenario = context.Scenario;
            for (int i = 0; i < _spec.Fields.Length; ++i)
            {
                DrawField(scenario, _spec.Fields[i]);
            }

            DrawHeader(SECTION_OPS);
            for (int i = 0; i < _spec.Operations.Length; ++i)
            {
                ContractOperation operation = _spec.Operations[i];
                if (GUILayout.Button(operation.Label))
                {
                    Run(context, operation);
                }
            }

            EditorGUILayout.LabelField("로컬 검증 결과", _localResult);
        }

        protected override void OnDetach()
        {
            _localResult = NO_LOCAL;
        }

        /// <summary> 로컬 검증을 통과하면 모의 요청을 보내고, 거절되면 요청 없이 문구만 남긴다 </summary>
        public bool Run(UiSandboxContext context, ContractOperation operation)
        {
            string rejection = operation.Validate(context.Scenario);
            if (rejection != null)
            {
                _localResult = LOCAL_REJECT + rejection;
                context.SetStatus(_localResult);
                return false;
            }

            _localResult = NO_LOCAL;
            return RunRequest(context, operation.Operation,
                () => context.SetStatus(operation.DescribeSuccess(context.Scenario)),
                message => context.SetStatus(operation.Operation + " 실패: " + message));
        }

        private void DrawField(UiSandboxScenarioSO scenario, ContractField field)
        {
            switch (field.Kind)
            {
                case EContractFieldKind.Header:
                    DrawHeader(field.Label);
                    break;
                case EContractFieldKind.Int:
                    DrawInt(scenario, field.Key, field.Label, field.Min, field.Max);
                    break;
                case EContractFieldKind.Float:
                    DrawFloat(scenario, field.Key, field.Label, field.Min, field.Max);
                    break;
                case EContractFieldKind.Bool:
                    DrawBool(scenario, field.Key, field.Label);
                    break;
                case EContractFieldKind.Text:
                    DrawString(scenario, field.Key, field.Label);
                    break;
                case EContractFieldKind.Choice:
                    DrawChoice(scenario, field);
                    break;
            }
        }

        private void DrawChoice(UiSandboxScenarioSO scenario, ContractField field)
        {
            int current = Mathf.Clamp(scenario.GetInt(field.Key), field.Min, field.Max);
            int next    = EditorGUILayout.Popup(field.Label, current, field.Choices);
            if (next != scenario.GetInt(field.Key))
            {
                scenario.SetInt(field.Key, next);
                MarkDirty();
            }
        }
    }
}
