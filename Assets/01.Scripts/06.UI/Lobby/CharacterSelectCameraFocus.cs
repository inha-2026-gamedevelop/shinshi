// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;
using Unity.Cinemachine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> 선택한 캐릭터에 맞춰 신목 주위를 따라 카메라를 회전한다 </summary>
    public sealed class CharacterSelectCameraFocus : ClientView
    {
        private const int BASE_PRIORITY = 20;
        private const int IDLE_PRIORITY = 0;

        [SerializeField] private CharacterSelectSelection _selection = null;
        [SerializeField] private CinemachineCamera _baseCamera = null;
        [SerializeField] private CinemachineCamera[] _slotCameras = null;
        [SerializeField] private Vector3 _orbitCenter = new Vector3(-0.36f, 0f, 7.61f);
        [SerializeField] private float _orbitRadius = 11.81f;
        [SerializeField] private float _cameraHeight = 1.27f;
        [SerializeField] private float _sideOffset = 0.6f;
        [SerializeField] private float _pitch = 4f;
        [SerializeField] private float _transitionDuration = 0.65f;
        [SerializeField] private float[] _slotAngles = { -14f, 0f, 14f };

        private float _angle = 0f;
        private Tween _orbitTween = null;

        protected override void OnViewAwake()
        {
            if ((_selection == null) || (_baseCamera == null) || (_slotCameras == null) || (_slotAngles == null) || (_slotAngles.Length == 0))
            {
                Debug.LogError("[CharacterSelect] 카메라 회전 설정이 연결되지 않았다", this);
                enabled = false;
                return;
            }

            _baseCamera.Priority = BASE_PRIORITY;
            foreach (CinemachineCamera slotCamera in _slotCameras)
            {
                if (slotCamera != null)
                {
                    slotCamera.Priority = IDLE_PRIORITY;
                }
            }
        }

        private void OnEnable()
        {
            if ((_selection == null) || (_baseCamera == null) || (_slotAngles == null) || (_slotAngles.Length == 0))
            {
                return;
            }

            _selection.SelectionChanged += OnSelectionChanged;
            int slot = _selection.SelectedSlot;
            ApplyAngle(((slot >= 0) && (slot < _slotAngles.Length)) ? _slotAngles[slot] : 0f);
        }

        private void OnDisable()
        {
            if (_selection != null)
            {
                _selection.SelectionChanged -= OnSelectionChanged;
            }

            _orbitTween?.Kill();
            _orbitTween = null;
        }

        private void OnSelectionChanged(int slot)
        {
            if ((slot < 0) || (slot >= _slotAngles.Length))
            {
                return;
            }

            _orbitTween?.Kill();
            _orbitTween = DOTween.To(() => _angle, ApplyAngle, _slotAngles[slot], _transitionDuration)
                .SetEase(Ease.InOutSine).SetLink(gameObject);
        }

        private void ApplyAngle(float angle)
        {
            _angle = angle;
            float radians = angle * Mathf.Deg2Rad;
            float sine = Mathf.Sin(radians);
            float cosine = Mathf.Cos(radians);
            _baseCamera.transform.position = new Vector3(_orbitCenter.x + (sine * _orbitRadius) + (cosine * _sideOffset),
                _cameraHeight, _orbitCenter.z - (cosine * _orbitRadius) + (sine * _sideOffset));
            _baseCamera.transform.rotation = Quaternion.Euler(_pitch, -angle, 0f);
        }
    }
}
