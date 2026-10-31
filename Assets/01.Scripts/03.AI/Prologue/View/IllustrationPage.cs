// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 일러스트 한 장과 그 장의 글, 그림이 아직 없으면 image는 null로 두고 빈 화면을 보인다 </summary>
    public readonly struct IllustrationPage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Sprite _image;
        private readonly string _text;

        /****************************************
        *              Properties
        ****************************************/

        public Sprite Image => _image;
        public string Text  => _text;

        /****************************************
        *                Methods
        ****************************************/

        public IllustrationPage(Sprite image, string text)
        {
            _image = image;
            _text  = text ?? string.Empty;
        }
    }
}
