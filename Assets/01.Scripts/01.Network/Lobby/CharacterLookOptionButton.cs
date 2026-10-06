// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Shinshi.Network
{
    /// <summary> 커스터마이즈 탭의 옵션 버튼 하나, 어느 부위의 어느 ID를 고르는지 Inspector에서 정한다 </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterLookOptionButton : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private ECharacterLookPart _part   = ECharacterLookPart.Face;
        [SerializeField] private int                _id     = 1;
        [SerializeField] private Button             _button = null;

        /****************************************
        *              Properties
        ****************************************/

        public ECharacterLookPart Part   => _part;
        public int                Id     => _id;
        public Button             Button => _button;
    }
}
