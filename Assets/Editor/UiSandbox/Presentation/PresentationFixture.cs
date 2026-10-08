using System.Collections.Generic;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 시험 전용으로 조립한 View 한 벌. 실제 씬에 배치된 화면이 아니며 페이지가 소유하고 Detach에서 파괴한다 </summary>
    public sealed class PresentationFixture
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string       _label;
        private readonly Component    _view;
        private readonly List<string> _unwiredFields;
        private GameObject            _root;

        /****************************************
        *              Properties
        ****************************************/

        public string Label
        {
            get { return _label; }
        }

        public GameObject Root
        {
            get { return _root; }
        }

        public Component View
        {
            get { return _view; }
        }

        public bool IsAlive
        {
            get { return _root != null; }
        }

        /// <summary> 필드 타입을 안전하게 조립하지 못해 null로 남긴 필드 목록 </summary>
        public IReadOnlyList<string> UnwiredFields
        {
            get { return _unwiredFields; }
        }

        /****************************************
        *                Methods
        ****************************************/

        public PresentationFixture(string label, GameObject root, Component view, List<string> unwiredFields)
        {
            _label         = label ?? string.Empty;
            _root          = root;
            _view          = view;
            _unwiredFields = unwiredFields ?? new List<string>();
        }

        public void Destroy()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }

            _root = null;
        }
    }
}
