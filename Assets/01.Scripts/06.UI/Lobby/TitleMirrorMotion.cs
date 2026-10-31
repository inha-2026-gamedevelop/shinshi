using UnityEngine;

namespace Shinshi.UI
{
    public sealed class TitleMirrorMotion : MonoBehaviour
    {
        [SerializeField] private float _yawDegrees = 10f;
        [SerializeField] private float _pitchDegrees = 7f;
        [SerializeField] private float _response = 4f;

        private Quaternion _restRotation;

        private void Awake()
        {
            _restRotation = transform.localRotation;
        }

        private void Update()
        {
            Vector3 mouse = Input.mousePosition;
            float x = 0f;
            float y = 0f;
            if (Application.isFocused && (Screen.width > 0) && (Screen.height > 0))
            {
                x = Mathf.Clamp((mouse.x / Screen.width * 2f) - 1f, -1f, 1f);
                y = Mathf.Clamp((mouse.y / Screen.height * 2f) - 1f, -1f, 1f);
            }

            Quaternion target = _restRotation * Quaternion.Euler(-y * _pitchDegrees, x * _yawDegrees, 0f);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, target, 1f - Mathf.Exp(-_response * Time.unscaledDeltaTime));
        }
    }
}
