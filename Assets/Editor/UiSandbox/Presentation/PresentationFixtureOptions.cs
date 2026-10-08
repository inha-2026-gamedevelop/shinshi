using System.Collections.Generic;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 시험 배치를 놓을 화면 위치 </summary>
    public enum EFixtureAnchor
    {
        TopLeft,
        TopCenter,
        TopRight,
        MiddleLeft,
        Center,
        MiddleRight,
        BottomLeft,
        BottomCenter,
        BottomRight
    }

    /// <summary> 시험 배치 조립 옵션. 배열 길이와 건너뛸 필드를 View마다 다르게 줄 수 있다 </summary>
    public sealed class PresentationFixtureOptions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int DEFAULT_ARRAY_LENGTH = 4;

        private readonly Dictionary<string, int> _arrayLengths = new Dictionary<string, int>();
        private readonly HashSet<string>         _skipFields   = new HashSet<string>();
        private readonly Dictionary<string, int> _childButtons = new Dictionary<string, int>();
        private readonly Dictionary<string, UnityEngine.Object> _assets = new Dictionary<string, UnityEngine.Object>();

        private EFixtureAnchor _anchor = EFixtureAnchor.Center;
        private Vector2        _offset = Vector2.zero;

        /****************************************
        *              Properties
        ****************************************/

        public EFixtureAnchor Anchor
        {
            get { return _anchor; }
        }

        public Vector2 Offset
        {
            get { return _offset; }
        }

        /// <summary> RectTransform 필드 이름 -> 그 아래에 활성화 전에 미리 만들 버튼 수 </summary>
        public IReadOnlyDictionary<string, int> ChildButtons
        {
            get { return _childButtons; }
        }

        /****************************************
        *                Methods
        ****************************************/

        public PresentationFixtureOptions() { }

        public PresentationFixtureOptions(EFixtureAnchor anchor)
        {
            _anchor = anchor;
        }

        public PresentationFixtureOptions WithOffset(float x, float y)
        {
            _offset = new Vector2(x, y);
            return this;
        }

        /// <summary> 필드 이름 -> 배열 길이. 지정하지 않은 필드는 DEFAULT_ARRAY_LENGTH </summary>
        public PresentationFixtureOptions WithLength(string fieldName, int length)
        {
            _arrayLengths[fieldName] = Mathf.Max(0, length);
            return this;
        }

        /// <summary> 조립하지 않고 null로 두는 필드. 등장 연출처럼 시험 배치에서 화면을 가리는 필드에 쓴다 </summary>
        public PresentationFixtureOptions Skip(string fieldName)
        {
            _skipFields.Add(fieldName);
            return this;
        }

        /// <summary> 패널처럼 View가 활성화될 때 자식 버튼을 훑어 구독하는 RectTransform 필드 아래에 버튼을 미리 만든다 </summary>
        public PresentationFixtureOptions WithChildButtons(string fieldName, int count)
        {
            _childButtons[fieldName] = Mathf.Max(0, count);
            return this;
        }

        /// <summary> 직렬화 필드에 프로젝트에 이미 있는 에셋(프리팹 등)을 그대로 연결한다. 만들어 낸 대용품 대신 검증된 원본을 쓸 때만 쓴다 </summary>
        public PresentationFixtureOptions WithAsset(string fieldName, UnityEngine.Object asset)
        {
            if (asset != null)
            {
                _assets[fieldName] = asset;
            }

            return this;
        }

        public bool TryGetAsset(string fieldName, System.Type fieldType, out UnityEngine.Object asset)
        {
            if ((_assets.TryGetValue(fieldName, out asset)) && (asset != null) && (fieldType.IsInstanceOfType(asset)))
            {
                return true;
            }

            asset = null;
            return false;
        }

        public int GetLength(string fieldName)
        {
            return _arrayLengths.TryGetValue(fieldName, out int length) ? length : DEFAULT_ARRAY_LENGTH;
        }

        public bool IsSkipped(string fieldName)
        {
            return _skipFields.Contains(fieldName);
        }
    }
}
