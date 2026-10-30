namespace Shinshi.AI
{
    /// <summary> 성장 메뉴 안내의 한 단계, 짧은 설명 하나와 강조할 대상의 이름 </summary>
    public readonly struct MenuGuideStepData
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _text;
        private readonly string _targetKey;

        /****************************************
        *              Properties
        ****************************************/

        public string Text => _text;

        // 메뉴 화면이 알려 주는 대상 이름, 비어 있으면 강조 없이 가운데에 설명만 보인다
        public string TargetKey => _targetKey;

        /****************************************
        *                Methods
        ****************************************/

        public MenuGuideStepData(string text, string targetKey)
        {
            _text      = text ?? string.Empty;
            _targetKey = targetKey ?? string.Empty;
        }
    }
}
