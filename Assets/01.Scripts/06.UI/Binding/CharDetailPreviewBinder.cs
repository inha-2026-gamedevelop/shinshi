// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Combat;
using Shinshi.Contracts;

namespace Shinshi.UI
{
    /// <summary> 상세 창의 모델을 현재 접속한 캐릭터의 서버 외형으로 갱신한다 </summary>
    public sealed class CharDetailPreviewBinder : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PlayerFemaleLookPreviewView _preview;
        [SerializeField] private GameObject _modelRoot;
        private NetworkIdentity _local;
        private PlayerCombatState _combat;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            NetworkIdentity local = NetworkClient.localPlayer;
            if (_local != local)
            {
                _local = local;
                _combat = local != null ? local.GetComponent<PlayerCombatState>() : null;
            }
            bool hasLook = false;
            if ((_combat != null)
                && (CharacterP09Look.TryFrom(_combat.Appearance, out CharacterP09Look look)))
            {
                _preview.Show(look);
                hasLook = _preview.IsReady;
            }
            if (_modelRoot.activeSelf != hasLook)
            {
                _modelRoot.SetActive(hasLook);
            }
        }

        private void OnDisable()
        {
            if (_modelRoot != null)
            {
                _modelRoot.SetActive(false);
            }
        }
    }
}
