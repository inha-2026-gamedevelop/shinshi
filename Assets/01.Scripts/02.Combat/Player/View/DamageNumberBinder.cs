// Unity
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 소유권을 얻은 플레이어가 피해 표시 풀을 만들고, 연결·씬 수명이 끝나면 이벤트와 풀을 정리한다 </summary>
    public sealed class DamageNumberBinder : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly DamageFeedbackReceiver _receiver = new DamageFeedbackReceiver();
        private const string MAIN_CAMERA_TAG = "MainCamera";
        private PlayerInputState _source;
        private PlayerInputState _input;
        private DamageNumberPool _pool;
        private bool _watchingCamera;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake() => TryGetComponent(out _source);

        private void OnEnable()
        {
            if (_source != null)
            {
                Bind(_source);
            }
        }

        private void OnDisable() => Unbind();
        private void OnDestroy() => Unbind();

        /****************************************
        *                Methods
        ****************************************/

        public void Bind(PlayerInputState input)
        {
            if ((IsServerBuild) || (input == null) || (input.netIdentity == null) || (!input.isClient)
                || (!input.isOwned) || (_input == input))
            {
                return;
            }
            Unbind();
            _source = input;
            _input = input;
            _input.DamageFeedbackReceived += OnDamageFeedback;
            SceneManager.sceneLoaded += OnSceneLoaded;
            CreatePool();
        }

        public void Unbind()
        {
            if (_input != null)
            {
                _input.DamageFeedbackReceived -= OnDamageFeedback;
            }
            _input = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            ReleasePool();
        }

        private void CreatePool()
        {
            if ((IsServerBuild) || (_input == null) || (_pool != null))
            {
                return;
            }
            var root = new GameObject("OwnerDamageNumbers", typeof(RectTransform));
            root.hideFlags = HideFlags.DontSave;
            _pool = root.AddComponent<DamageNumberPool>();
            // 네트워크 스폰과 씬 로드 시점에 한 번만 카메라를 잡아 타격 루프의 전역 조회를 피한다
            Camera camera = Camera.main;
            _pool.Initialize(camera);
            // 실제 렌더 카메라의 수명을 관찰해 뒤늦은 생성·교체를 전역 조회 루프 없이 받는다
            _watchingCamera = true;
            RenderPipelineManager.beginCameraRendering += OnCameraRendering;
        }

        private void ReleasePool()
        {
            StopWatchingCamera();
            if (_pool == null)
            {
                return;
            }
            _pool.Clear();
            GameObject root = _pool.gameObject;
            root.SetActive(false);
            if (Application.isPlaying)
            {
                Destroy(root);
            }
            else
            {
                DestroyImmediate(root);
            }
            _pool = null;
        }

        private void OnCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if ((!_watchingCamera) || (_pool == null) || (camera == null)
                || (!camera.isActiveAndEnabled) || (!camera.CompareTag(MAIN_CAMERA_TAG)))
            {
                return;
            }
            if (!_pool.IsBoundToCamera(camera))
            {
                _pool.BindCamera(camera);
            }
        }

        private void StopWatchingCamera()
        {
            if (!_watchingCamera)
            {
                return;
            }
            _watchingCamera = false;
            RenderPipelineManager.beginCameraRendering -= OnCameraRendering;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if ((mode == LoadSceneMode.Additive) && (_pool != null))
            {
                return;
            }
            ReleasePool();
            CreatePool();
        }

        private void OnDamageFeedback(DamageFeedback feedback)
        {
            double now = NetworkServer.active ? NetworkTime.time
                : DamageFeedbackRules.ReceptionTime(NetworkTime.time, NetworkClient.bufferTime);
            if ((_input == null) || (_pool == null) || (!_input.isOwned)
                || (!_receiver.TryAccept(in feedback, _input.SessionEpoch, now)))
            {
                return;
            }
            _pool.Show(in feedback);
        }
    }
}
