// Unity
using UnityEngine;

// 프로젝트
using Shinshi.UI;

namespace Shinshi.Dev
{
    /// <summary> 시험 플레이어가 몬스터에게 맞은 횟수를 화면 아래 HP 바에 그대로 옮겨 주는 시험용 연결부 </summary>
    public sealed class DevPlayerHpBinder : MonoBehaviour
    {
        // 실제로는 서버가 내려 주는 체력을 HUD가 받는다. 서버가 없는 시험 씬에서는 남은 피격 횟수를 체력 대역으로 쓴다

        /****************************************
        *                Fields
        ****************************************/

        [Header("참조")]
        [SerializeField] private DevHitFeedbackPlayer _player = null;
        [SerializeField] private HudPlayerStatusView  _view   = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if ((_player == null) || (_view == null))
            {
                Debug.LogError("[DevHpBinder] 플레이어나 HP 바가 연결되지 않았다", this);
                enabled = false;
                return;
            }

            _player.Damaged += OnDamaged;
        }

        private void Start()
        {
            Refresh();
        }

        private void OnDisable()
        {
            if (_player != null)
            {
                _player.Damaged -= OnDamaged;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnDamaged(int hitsLeft)
        {
            Refresh();
        }

        private void Refresh()
        {
            _view.SetHp(_player.HitsLeft, _player.MaxHits);
        }
    }
}
