using System;

namespace Shinshi.Editor
{
    /// <summary> 계약 전용 페이지 입력 칸의 종류 </summary>
    public enum EContractFieldKind
    {
        Header,
        Int,
        Float,
        Bool,
        Text,
        Choice
    }

    /// <summary> 계약 전용 페이지의 편집 가능한 입력 한 칸. 시나리오 키로 저장되고 첫 열기에만 기본값이 들어간다 </summary>
    public sealed class ContractField
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string             _key;
        private readonly string             _label;
        private readonly EContractFieldKind _kind;
        private readonly int                _intDefault;
        private readonly int                _min;
        private readonly int                _max;
        private readonly float              _floatDefault;
        private readonly bool               _boolDefault;
        private readonly string             _textDefault;
        private readonly string[]           _choices;

        /****************************************
        *              Properties
        ****************************************/

        public string Key
        {
            get { return _key; }
        }

        public string Label
        {
            get { return _label; }
        }

        public EContractFieldKind Kind
        {
            get { return _kind; }
        }

        public int IntDefault
        {
            get { return _intDefault; }
        }

        public int Min
        {
            get { return _min; }
        }

        public int Max
        {
            get { return _max; }
        }

        public float FloatDefault
        {
            get { return _floatDefault; }
        }

        public bool BoolDefault
        {
            get { return _boolDefault; }
        }

        public string TextDefault
        {
            get { return _textDefault; }
        }

        public string[] Choices
        {
            get { return _choices; }
        }

        /****************************************
        *                Methods
        ****************************************/

        private ContractField(string key, string label, EContractFieldKind kind, int intDefault, int min, int max,
            float floatDefault, bool boolDefault, string textDefault, string[] choices)
        {
            _key          = key;
            _label        = label;
            _kind         = kind;
            _intDefault   = intDefault;
            _min          = min;
            _max          = max;
            _floatDefault = floatDefault;
            _boolDefault  = boolDefault;
            _textDefault  = textDefault;
            _choices      = choices;
        }

        public static ContractField Header(string label)
        {
            return new ContractField(null, label, EContractFieldKind.Header, 0, 0, 0, 0.0f, false, null, null);
        }

        public static ContractField Int(string key, string label, int defaultValue, int min, int max)
        {
            return new ContractField(key, label, EContractFieldKind.Int, defaultValue, min, max, 0.0f, false, null, null);
        }

        public static ContractField Float(string key, string label, float defaultValue, int min, int max)
        {
            return new ContractField(key, label, EContractFieldKind.Float, 0, min, max, defaultValue, false, null, null);
        }

        public static ContractField Bool(string key, string label, bool defaultValue)
        {
            return new ContractField(key, label, EContractFieldKind.Bool, 0, 0, 0, 0.0f, defaultValue, null, null);
        }

        public static ContractField Text(string key, string label, string defaultValue)
        {
            return new ContractField(key, label, EContractFieldKind.Text, 0, 0, 0, 0.0f, false, defaultValue, null);
        }

        /// <summary> 선택지는 정수 번호로 저장한다 </summary>
        public static ContractField Choice(string key, string label, int defaultIndex, params string[] choices)
        {
            return new ContractField(key, label, EContractFieldKind.Choice, defaultIndex, 0, choices.Length - 1, 0.0f, false, null, choices);
        }
    }

    /// <summary> 계약 전용 페이지의 모의 요청 버튼. 로컬 검증이 문구를 돌려주면 요청 없이 거절로 표시한다 </summary>
    public sealed class ContractOperation
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string                            _operation;
        private readonly string                            _label;
        private readonly Func<UiSandboxScenarioSO, string> _validate;
        private readonly Func<UiSandboxScenarioSO, string> _describeSuccess;

        /****************************************
        *              Properties
        ****************************************/

        public string Operation
        {
            get { return _operation; }
        }

        public string Label
        {
            get { return _label; }
        }

        /****************************************
        *                Methods
        ****************************************/

        public ContractOperation(string operation, string label, Func<UiSandboxScenarioSO, string> validate,
            Func<UiSandboxScenarioSO, string> describeSuccess)
        {
            _operation       = operation;
            _label           = label;
            _validate        = validate;
            _describeSuccess = describeSuccess;
        }

        /// <summary> 거절 문구를 돌려주고 통과하면 null </summary>
        public string Validate(UiSandboxScenarioSO scenario)
        {
            return (_validate != null) ? _validate(scenario) : null;
        }

        public string DescribeSuccess(UiSandboxScenarioSO scenario)
        {
            return (_describeSuccess != null) ? _describeSuccess(scenario) : (_operation + " 처리됨");
        }
    }

    /// <summary> 계약 전용 페이지 한 개의 정의 </summary>
    public sealed class ContractSpec
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string              _id;
        private readonly string              _title;
        private readonly string              _requirement;
        private readonly ContractField[]     _fields;
        private readonly ContractOperation[] _operations;

        /****************************************
        *              Properties
        ****************************************/

        public string Id
        {
            get { return _id; }
        }

        public string Title
        {
            get { return _title; }
        }

        public string Requirement
        {
            get { return _requirement; }
        }

        public ContractField[] Fields
        {
            get { return _fields; }
        }

        public ContractOperation[] Operations
        {
            get { return _operations; }
        }

        /****************************************
        *                Methods
        ****************************************/

        public ContractSpec(string id, string title, string requirement, ContractField[] fields, ContractOperation[] operations)
        {
            _id          = id;
            _title       = title;
            _requirement = requirement;
            _fields      = fields;
            _operations  = operations;
        }
    }
}
