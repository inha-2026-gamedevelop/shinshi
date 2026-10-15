// System
using System.Collections;

// Unity
using UnityEngine;

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

        [Header("연출")]
        [SerializeField] private Vector3 _defaultImpactLocal = new Vector3(0f, 1.2f, 0f);
        [SerializeField] private float   _blastSpeed         = 7f;
        [SerializeField] private float   _blastRadius        = 8f;
        [SerializeField] private float   _upwardModifier     = 0.8f;
        [SerializeField] private float   _spinSpeed          = 4f;
        [SerializeField] private float   _settleSeconds      = 6f;

        private Vector3[]    _homePositions;
        private Quaternion[] _homeRotations;
        private Collider[]   _fragmentColliders;
        private WaitForSeconds _settleWait;
        private Coroutine    _settleRoutine;
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
            _settleWait       = new WaitForSeconds(_settleSeconds);

            for (int i = 0; i < count; i++)
            {
                _homePositions[i]     = _fragments[i].transform.localPosition;
                _homeRotations[i]     = _fragments[i].transform.localRotation;
                _fragmentColliders[i] = _fragments[i].GetComponent<Collider>();
            }

            _fragmentRoot.SetActive(false);
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

            for (int i = 0; i < _fragments.Length; i++)
            {
                _fragmentColliders[i].enabled = true;
                _fragments[i].isKinematic = false;
                _fragments[i].AddExplosionForce(_blastSpeed, impactPoint, _blastRadius, _upwardModifier, ForceMode.VelocityChange);
                _fragments[i].AddTorque(Random.onUnitSphere * _spinSpeed, ForceMode.VelocityChange);
            }

            _settleRoutine = StartCoroutine(CoSettle());
        }

        /// <summary> 파편을 처음 자리로 되돌리고 온전한 석주를 다시 보인다. 시험용이다 </summary>
        [ContextMenu("Reset Test")]
        public void ResetPillar()
        {
            if (_settleRoutine != null)
            {
                StopCoroutine(_settleRoutine);
                _settleRoutine = null;
            }

            for (int i = 0; i < _fragments.Length; i++)
            {
                _fragments[i].isKinematic = true;
                _fragmentColliders[i].enabled = false;
                _fragments[i].transform.SetLocalPositionAndRotation(_homePositions[i], _homeRotations[i]);
            }

            _fragmentRoot.SetActive(false);
            SetIntact(true);
            _isShattered = false;
        }

        private IEnumerator CoSettle()
        {
            yield return _settleWait;

            // 멈춘 파편은 잔해로 남기고 물리 비용만 끈다
            for (int i = 0; i < _fragments.Length; i++)
            {
                _fragments[i].isKinematic = true;
                _fragmentColliders[i].enabled = false;
            }

            _settleRoutine = null;
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
