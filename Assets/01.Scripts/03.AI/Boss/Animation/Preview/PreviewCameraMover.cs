// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 시험 씬 카메라를 WASD로 수평 이동하는 확인용 도구, 높이와 각도는 바꾸지 않는다 </summary>
    public sealed class PreviewCameraMover : MonoBehaviour
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string HORIZONTAL_AXIS = "Horizontal";
        private const string VERTICAL_AXIS   = "Vertical";

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private float _speed = 12f;
        [SerializeField] private float _fastMultiplier = 2f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = transform.right;
            right.y = 0f;
            right.Normalize();

            Vector3 move = (forward * Input.GetAxisRaw(VERTICAL_AXIS)) + (right * Input.GetAxisRaw(HORIZONTAL_AXIS));

            if (move.sqrMagnitude <= 0f)
            {
                return;
            }

            float speed = (Input.GetKey(KeyCode.LeftShift)) ? _speed * _fastMultiplier : _speed;
            transform.position += move.normalized * (speed * Time.unscaledDeltaTime);
        }
    }
}
