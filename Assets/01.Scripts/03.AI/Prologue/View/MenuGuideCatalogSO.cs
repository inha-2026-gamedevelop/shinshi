// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 성장 메뉴별 안내 단계 모음, 로컬 표현 전용이라 GameDB에 넣지 않고 서버 판정과 무관하다 </summary>
    [CreateAssetMenu(fileName = "MenuGuideCatalog", menuName = "Shinshi/MenuGuideCatalogSO")]
    public sealed class MenuGuideCatalogSO : ScriptableObject
    {
        // 안내 대상 메뉴 목록과 문구는 기획이 정해지지 않았다. 지금 들어 있는 것은 모두 임시 문구이고 수련, 부적은 기획의 예시다

        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        public sealed class StepEntry
        {
            [SerializeField, TextArea] private string _text;
            [SerializeField] private string _targetKey; // 메뉴 화면이 알려 주는 강조 대상 이름, 비우면 가운데 설명만

            public string Text      => _text;
            public string TargetKey => _targetKey;

            public StepEntry(string text, string targetKey)
            {
                _text      = text;
                _targetKey = targetKey;
            }
        }

        [Serializable]
        public sealed class MenuEntry
        {
            [SerializeField] private string      _menuId;
            [SerializeField] private StepEntry[] _steps;

            public string      MenuId => _menuId;
            public StepEntry[] Steps  => _steps;

            public MenuEntry(string menuId, StepEntry[] steps)
            {
                _menuId = menuId;
                _steps  = steps;
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private MenuEntry[] _menus;

        private Dictionary<string, MenuGuideStepData[]> _lookup = null;

        /****************************************
        *              Properties
        ****************************************/

        public int MenuCount => (_menus != null) ? _menus.Length : 0;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 에디터에서 에셋을 만들 때 쓰는 채우기, 런타임에서는 부르지 않는다 </summary>
        public void Fill(MenuEntry[] menus)
        {
            _menus  = menus;
            _lookup = null;
        }

        /// <summary> 메뉴의 안내 단계를 찾는다, 등록되지 않은 메뉴이거나 단계가 없으면 거짓이다 </summary>
        public bool TryGetSteps(string menuId, out MenuGuideStepData[] steps)
        {
            steps = null;
            if (string.IsNullOrEmpty(menuId))
            {
                return false;
            }

            if (_lookup == null)
            {
                BuildLookup();
            }

            return _lookup.TryGetValue(menuId, out steps);
        }

        private void BuildLookup()
        {
            _lookup = new Dictionary<string, MenuGuideStepData[]>(StringComparer.Ordinal);
            if (_menus == null)
            {
                return;
            }

            for (int i = 0; i < _menus.Length; ++i)
            {
                MenuEntry menu = _menus[i];
                if ((menu == null) || (string.IsNullOrEmpty(menu.MenuId)) || (menu.Steps == null) || (menu.Steps.Length == 0))
                {
                    continue;
                }

                var steps = new MenuGuideStepData[menu.Steps.Length];
                for (int s = 0; s < steps.Length; ++s)
                {
                    steps[s] = new MenuGuideStepData(menu.Steps[s].Text, menu.Steps[s].TargetKey);
                }

                _lookup[menu.MenuId] = steps;
            }
        }
    }
}
