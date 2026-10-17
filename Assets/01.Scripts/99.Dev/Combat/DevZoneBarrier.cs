// Unity
using UnityEngine;

using UnityEngine.VFX;

// 서드파티
using DG.Tweening;

namespace Shinshi.Dev
{
    /// <summary> 구역의 몬스터를 모두 처치하기 전까지 다음 구역으로 가는 길을 막는 시험용 결계 </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class DevZoneBarrier : MonoBehaviour
    {
        // 실제로는 서버가 웨이브 진행에 맞춰 결계 상태를 내려 준다. 서버가 없는 시험 씬에서는 스포너의 처치 판정을 그 대역으로 쓴다.
        // 충돌체는 Ignore Raycast 레이어에 둔다. 스포너가 땅을 찾는 레이캐스트와 자리 검사가 결계 윗면을 땅으로 오인하지 않게 하기 위해서다

        /****************************************
        *                Fields
        ****************************************/

        [Header("참조")]
        [SerializeField] private DevHitFeedbackSpawner _spawner = null;
        [SerializeField] private Renderer              _visual  = null; // 버블 쉴드를 쓰면 비워도 된다

        [Header("버블 쉴드 (선택, 풀릴 때 입자가 멈추고 사라진다)")]
        [SerializeField] private GameObject     _bubbleRoot = null;
        [SerializeField] private Renderer       _bubbleMesh = null; // 있으면 디졸브로 사라진다
        [SerializeField] private VisualEffect[] _bubbleFxs  = null; // 풀릴 때 멈추고 꺼지는 VFX 그래프 효과

        [Header("벽 파티클 (풀리면 방출만 멈추고 남은 입자는 스스로 사라진다)")]
        [SerializeField] private ParticleSystem[] _wallParticles = null;

        [Header("규칙")]
        [SerializeField, Min(0)] private int   _zoneIndex    = 0;    // 이 구역의 몬스터를 모두 처치하면 풀린다
        [SerializeField, Min(0f)] private float _fadeDuration = 0.8f; // 초, 풀릴 때 결계가 사라지는 시간

        private static readonly int DISSOLVE_AMOUNT_ID = Shader.PropertyToID("_Dissolve_Amount");

        private BoxCollider _collider = null;
        private bool        _isOpen   = false;

        /****************************************
        *               Properties
        ****************************************/

        public bool IsOpen => _isOpen;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _collider = GetComponent<BoxCollider>();

            bool hasBubbleFx     = (_bubbleFxs != null) && (_bubbleFxs.Length > 0);
            bool hasWallParticle = (_wallParticles != null) && (_wallParticles.Length > 0);
            if ((_spawner == null) || ((_visual == null) && (_bubbleMesh == null) && (!hasBubbleFx) && (!hasWallParticle)))
            {
                Debug.LogError("[DevBarrier] 스포너나 결계 모양이 연결되지 않았다", this);
                enabled = false;
            }
        }

        private void Update()
        {
            if ((_zoneIndex >= _spawner.ZoneCount) || (!_spawner.IsZoneCleared(_zoneIndex)))
            {
                return;
            }

            Open();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Open()
        {
            _isOpen           = true;
            _collider.enabled = false;
            enabled           = false;

            if (_bubbleFxs != null)
            {
                for (int i = 0; i < _bubbleFxs.Length; ++i)
                {
                    _bubbleFxs[i].Stop();
                }
            }

            if (_wallParticles != null)
            {
                for (int i = 0; i < _wallParticles.Length; ++i)
                {
                    _wallParticles[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            // 인스턴스 재질을 쓴다. 공유 재질의 색을 바꾸면 다른 결계도 함께 사라진다
            Tween fade = null;
            if (_visual != null)
            {
                fade = _visual.material.DOFade(0f, _fadeDuration);
            }
            if (_bubbleMesh != null)
            {
                fade = _bubbleMesh.material.DOFloat(1f, DISSOLVE_AMOUNT_ID, _fadeDuration);
            }

            if (fade == null)
            {
                DOVirtual.DelayedCall(_fadeDuration, HideVisual).SetLink(gameObject);
                return;
            }

            fade.OnComplete(HideVisual);
        }

        private void HideVisual()
        {
            if (_visual != null)
            {
                _visual.gameObject.SetActive(false);
            }
            if (_bubbleRoot != null)
            {
                _bubbleRoot.SetActive(false);
            }
        }
    }
}
