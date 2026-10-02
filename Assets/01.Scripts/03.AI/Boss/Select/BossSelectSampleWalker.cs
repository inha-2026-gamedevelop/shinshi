// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> Dev_AI 씬에는 플레이어가 없어 NPC 상호작용을 확인하려고 두는 캡슐 이동 샘플. 선택 창이 열려 있으면 멈춘다 </summary>
    public sealed class BossSelectSampleWalker : MonoBehaviour
    {
        // 실제 이동은 서버 권위 플레이어 몫이다. 이 샘플은 월드 축으로 직선 이동만 하고 충돌과 지형은 보지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const string AXIS_HORIZONTAL = "Horizontal";
        private const string AXIS_VERTICAL   = "Vertical";

        [SerializeField] private BossSelectPanel _panel = null;
        [SerializeField, Min(0f)] private float  _moveSpeed = 5f; // m/s

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if ((_panel != null) && _panel.IsOpen)
            {
                return;
            }

            Vector3 direction = new Vector3(Input.GetAxisRaw(AXIS_HORIZONTAL), 0f, Input.GetAxisRaw(AXIS_VERTICAL));
            if (direction.sqrMagnitude <= 0f)
            {
                return;
            }

            transform.position += direction.normalized * (_moveSpeed * Time.deltaTime);
        }
    }
}
