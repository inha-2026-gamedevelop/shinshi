// System
using System.Collections;

// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;

namespace Shinshi.Art
{
    /// <summary> 무철 석주를 파괴 신호에 맞춰 온전한 모델에서 파편으로 바꾸고 클라이언트 로컬 물리로만 흩뜨린다 </summary>
    public sealed class MucheolPillarShatter : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly int BASE_COLOR_ID = Shader.PropertyToID("_BaseColor");

        [Header("온전한 석주")]
        [SerializeField] private GameObject[] _intactObjects;
        [SerializeField] private Collider     _intactCollider;

        [Header("파편")]
        [SerializeField] private GameObject  _fragmentRoot;
        [SerializeField] private Rigidbody[] _fragments;

        [Header("천")]
        [SerializeField] private Cloth[]        _cloths;
        [SerializeField] private SphereCollider _clothFloor;

        [Header("사라짐")]
        // 같은 순서로 짝을 이룬다. 불투명 재질을 같은 모양의 반투명 재질로 바꿔 서서히 투명하게 만든다
        [SerializeField] private Material[] _solidMaterials;
        [SerializeField] private Material[] _fadeMaterials;

        [Header("연출")]
        [SerializeField] private Vector3 _defaultImpactLocal = new Vector3(0f, 1.2f, 0f);
        [SerializeField] private float   _blastSpeed         = 7f;
        [SerializeField] private float   _blastRadius        = 8f;
        [SerializeField] private float   _upwardModifier     = 0.8f;
        [SerializeField] private float   _spinSpeed          = 4f;
        [SerializeField] private float   _vanishDelay        = 4f;
        [SerializeField] private float   _fadeSeconds        = 1f;

        private Vector3[]      _homePositions;
        private Quaternion[]   _homeRotations;
        private Collider[]     _fragmentColliders;
        private Renderer[]     _fragmentRenderers;
        private Material[][]   _solidSlots;
        private Material[][]   _fadeSlots;
        private Material[]     _fadeInstances;
        private Color[]        _fadeBaseColors;
        private Tween          _fadeTween;
        private Vector3        _clothFloorHome;
        private WaitForSeconds _vanishWait;
        private Coroutine      _vanishRoutine;
        private float          _alpha;
        private bool           _isShattered;

        public bool IsShattered => _isShattered;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            int count = _fragments.Length;
            _homePositions     = new Vector3[count];
            _homeRotations     = new Quaternion[count];
            _fragmentColliders = new Collider[count];
            _vanishWait        = new WaitForSeconds(_vanishDelay);

            for (int i = 0; i < count; i++)
            {
                _homePositions[i]     = _fragments[i].transform.localPosition;
                _homeRotations[i]     = _fragments[i].transform.localRotation;
                _fragmentColliders[i] = _fragments[i].GetComponent<Collider>();
            }

            // 천은 구 하나를 바닥 삼아 닿는다. 석주를 늘이거나 줄여도 구는 커지지 않도록 석주 밖으로 떼어 낸다.
            // 구가 석주의 비균일 크기를 물려받으면 반지름이 부풀어 천을 밀어 내 멀리 날려 버린다
            Transform floor = _clothFloor.transform;
            floor.SetParent(null, true);
            floor.localScale = Vector3.one;
            _clothFloorHome = floor.position;
            ClothSphereColliderPair[] pair = { new ClothSphereColliderPair(_clothFloor) };
            for (int i = 0; i < _cloths.Length; i++)
            {
                _cloths[i].sphereColliders = pair;
            }

            BuildFadeSlots();
            _fragmentRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            KillFade();

            if (_clothFloor != null)
            {
                Destroy(_clothFloor.gameObject);
            }

            if (_fadeInstances == null)
            {
                return;
            }

            for (int i = 0; i < _fadeInstances.Length; i++)
            {
                Destroy(_fadeInstances[i]);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        [ContextMenu("Shatter Test")]
        public void Shatter()
        {
            Shatter(transform.TransformPoint(_defaultImpactLocal));
        }

        /// <summary> 파괴 신호를 받아 호출한다. 충격점에서 바깥으로 파편이 튄다 </summary>
        public void Shatter(Vector3 impactPoint)
        {
            // 전용 서버 빌드는 화면이 없으므로 파편과 물리를 만들지 않는다
            if (_isShattered || Application.isBatchMode)
            {
                return;
            }

            _isShattered = true;
            SetIntact(false);
            _fragmentRoot.SetActive(true);
            PlaceClothFloor();

            // 껐다 켜면 천이 처음 모양에서 다시 시작한다
            for (int i = 0; i < _cloths.Length; i++)
            {
                _cloths[i].enabled = false;
                _cloths[i].enabled = true;
            }

            for (int i = 0; i < _fragments.Length; i++)
            {
                _fragmentColliders[i].enabled = true;
                _fragments[i].isKinematic = false;
                _fragments[i].AddExplosionForce(_blastSpeed, impactPoint, _blastRadius, _upwardModifier, ForceMode.VelocityChange);
                _fragments[i].AddTorque(Random.onUnitSphere * _spinSpeed, ForceMode.VelocityChange);
            }

            _vanishRoutine = StartCoroutine(CoVanish());
        }

        /// <summary> 파편을 처음 자리로 되돌리고 온전한 석주를 다시 보인다. 시험용이다 </summary>
        [ContextMenu("Reset Test")]
        public void ResetPillar()
        {
            if (_vanishRoutine != null)
            {
                StopCoroutine(_vanishRoutine);
                _vanishRoutine = null;
            }

            KillFade();
            SwapMaterials(_solidSlots);

            for (int i = 0; i < _fragments.Length; i++)
            {
                _fragments[i].isKinematic = true;
                _fragmentColliders[i].enabled = false;
                _fragments[i].transform.SetLocalPositionAndRotation(_homePositions[i], _homeRotations[i]);
            }

            _clothFloor.transform.position = _clothFloorHome;
            _fragmentRoot.SetActive(false);
            SetIntact(true);
            _isShattered = false;
        }

        /// <summary> 파괴 뒤 잠시 두었다가 파편을 서서히 투명하게 만들고 다 사라지면 석주를 지운다 </summary>
        private IEnumerator CoVanish()
        {
            yield return _vanishWait;

            // 투명해지는 동안 물리가 간섭하지 않게 멈춘다
            for (int i = 0; i < _fragments.Length; i++)
            {
                _fragments[i].isKinematic = true;
                _fragmentColliders[i].enabled = false;
            }

            SwapMaterials(_fadeSlots);
            _alpha = 1f;
            ApplyAlpha();
            _fadeTween = DOTween.To(() => _alpha, value => { _alpha = value; ApplyAlpha(); }, 0f, _fadeSeconds)
                .SetEase(Ease.Linear)
                .OnComplete(FinishVanish);

            _vanishRoutine = null;
        }

        // 다 투명해지면 석주 오브젝트를 통째로 지운다. 천 바닥 구와 투명 재질 복사본은 OnDestroy에서 함께 정리한다
        private void FinishVanish()
        {
            _fadeTween = null;
            Destroy(gameObject);
        }

        /// <summary> 파편 렌더러마다 불투명 재질 배열과 반투명 재질 배열을 미리 만들어 둔다 </summary>
        private void BuildFadeSlots()
        {
            _fadeInstances  = new Material[_fadeMaterials.Length];
            _fadeBaseColors = new Color[_fadeMaterials.Length];
            for (int i = 0; i < _fadeMaterials.Length; i++)
            {
                _fadeInstances[i]  = new Material(_fadeMaterials[i]);
                _fadeBaseColors[i] = _fadeMaterials[i].GetColor(BASE_COLOR_ID);
            }

            _fragmentRenderers = _fragmentRoot.GetComponentsInChildren<Renderer>(true);
            _solidSlots        = new Material[_fragmentRenderers.Length][];
            _fadeSlots         = new Material[_fragmentRenderers.Length][];

            for (int i = 0; i < _fragmentRenderers.Length; i++)
            {
                Material[] solid = _fragmentRenderers[i].sharedMaterials;
                Material[] fade  = new Material[solid.Length];
                for (int slot = 0; slot < solid.Length; slot++)
                {
                    int pair = System.Array.IndexOf(_solidMaterials, solid[slot]);
                    fade[slot] = pair >= 0 ? _fadeInstances[pair] : solid[slot];
                }

                _solidSlots[i] = solid;
                _fadeSlots[i]  = fade;
            }
        }

        private void SwapMaterials(Material[][] slots)
        {
            for (int i = 0; i < _fragmentRenderers.Length; i++)
            {
                _fragmentRenderers[i].sharedMaterials = slots[i];
            }
        }

        private void ApplyAlpha()
        {
            for (int i = 0; i < _fadeInstances.Length; i++)
            {
                Color color = _fadeBaseColors[i];
                color.a = _alpha;
                _fadeInstances[i].SetColor(BASE_COLOR_ID, color);
            }
        }

        private void PlaceClothFloor()
        {
            Vector3 position = transform.position;
            _clothFloor.transform.position = new Vector3(position.x, position.y - _clothFloor.radius, position.z);
        }

        private void KillFade()
        {
            _fadeTween?.Kill();
            _fadeTween = null;
        }

        private void SetIntact(bool visible)
        {
            for (int i = 0; i < _intactObjects.Length; i++)
            {
                _intactObjects[i].SetActive(visible);
            }

            _intactCollider.enabled = visible;
        }
    }
}
