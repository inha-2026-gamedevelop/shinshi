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

        [Header("온전한 석주")]
        [SerializeField] private GameObject[] _intactObjects;
        [SerializeField] private Collider     _intactCollider;

        [Header("파편")]
        [SerializeField] private GameObject _fragmentRoot;
        [SerializeField] private Rigidbody[] _fragments;

        [Header("천")]
        [SerializeField] private Cloth[]        _cloths;
        [SerializeField] private SphereCollider _clothFloor;

        [Header("연출")]
        [SerializeField] private Vector3 _defaultImpactLocal = new Vector3(0f, 1.2f, 0f);
        [SerializeField] private float   _blastSpeed         = 7f;
        [SerializeField] private float   _blastRadius        = 8f;
        [SerializeField] private float   _upwardModifier     = 0.8f;
        [SerializeField] private float   _spinSpeed          = 4f;
        [SerializeField] private float   _vanishDelay        = 2f;
        [SerializeField] private float   _sinkSeconds        = 1.5f;
        [SerializeField] private float   _sinkDepth          = 2.5f;

        private Vector3[]    _homePositions;
        private Quaternion[] _homeRotations;
        private Collider[]   _fragmentColliders;
        private Tween[]      _sinkTweens;
        private Tween        _clothFloorTween;
        private Vector3      _clothFloorHome;
        private WaitForSeconds _vanishWait;
        private WaitForSeconds _sinkWait;
        private Coroutine    _vanishRoutine;
        private bool         _isShattered;

        public bool IsShattered => _isShattered;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            int count = _fragments.Length;
            _homePositions    = new Vector3[count];
            _homeRotations    = new Quaternion[count];
            _fragmentColliders = new Collider[count];
            _sinkTweens       = new Tween[count];
            _vanishWait       = new WaitForSeconds(_vanishDelay);
            _sinkWait         = new WaitForSeconds(_sinkSeconds);

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

            _fragmentRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            KillSink();

            if (_clothFloor != null)
            {
                Destroy(_clothFloor.gameObject);
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

            KillSink();

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

        /// <summary> 파괴 뒤 잠시 두었다가 파편을 바닥 아래로 가라앉혀 지운다 </summary>
        private IEnumerator CoVanish()
        {
            yield return _vanishWait;

            // 가라앉는 동안 물리가 간섭하지 않게 멈춘다
            for (int i = 0; i < _fragments.Length; i++)
            {
                _fragments[i].isKinematic = true;
                _fragmentColliders[i].enabled = false;
                _sinkTweens[i] = _fragments[i].transform
                    .DOMoveY(_fragments[i].position.y - _sinkDepth, _sinkSeconds)
                    .SetEase(Ease.InQuad);
            }

            _clothFloorTween = _clothFloor.transform.DOMoveY(_clothFloor.transform.position.y - _sinkDepth, _sinkSeconds).SetEase(Ease.InQuad);

            yield return _sinkWait;

            _fragmentRoot.SetActive(false);
            _vanishRoutine = null;
        }

        private void PlaceClothFloor()
        {
            Vector3 position = transform.position;
            _clothFloor.transform.position = new Vector3(position.x, position.y - _clothFloor.radius, position.z);
        }

        private void KillSink()
        {
            _clothFloorTween?.Kill();
            _clothFloorTween = null;

            if (_sinkTweens == null)
            {
                return;
            }

            for (int i = 0; i < _sinkTweens.Length; i++)
            {
                _sinkTweens[i]?.Kill();
                _sinkTweens[i] = null;
            }
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
