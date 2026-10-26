// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 99.ThirdParty는 Git에서 제외되어 프리팹 참조가 팀원 컴퓨터에서 비어 있을 수 있다. 슬롯이 비면 아무것도 그리지 않고 false를 돌려준다
    // 임팩트 프리팹은 Animator가 한 번 재생하고 렌더러만 끈다. 이 뷰가 풀에 돌려놓고, 재사용은 껐다 켜서 처음부터 다시 재생한다
    // 이 뷰는 피격을 판정하지 않는다. 서버가 확정한 WolbaekPlayerHit만 받아 그린다
    /// <summary> 월백 공격에 맞은 플레이어 위치에 피격 임팩트를 터뜨리고 풀에 돌려놓는다 </summary>
    [DisallowMultipleComponent]
    public sealed class WolbaekHitImpactView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private sealed class Active
        {
            public GameObject Instance;
            public float      EndTime;
        }

        [Header("임팩트")]
        [Tooltip("피격 임팩트 프리팹, 비어 있으면 아무것도 그리지 않는다")]
        [SerializeField] private GameObject _impactPrefab;

        [Tooltip("프리팹 기본 크기(지름 약 1.7m)에 곱하는 배율")]
        [SerializeField, Min(0.1f)] private float _scale = 1f;

        [Tooltip("재생이 끝나 풀로 돌려보낼 때까지의 시간(초), 프리팹 재생 길이보다 길게 둔다")]
        [SerializeField, Min(0.05f)] private float _lifetimeSeconds = 0.4f;

        [Header("한도")]
        [Tooltip("동시에 띄울 수 있는 임팩트 수, 넘으면 새 피격은 그리지 않는다")]
        [SerializeField, Min(1)] private int _maxActive = 8;

        [Tooltip("이 시간(초)보다 오래된 서버 피격은 늦게 도착한 것으로 보고 그리지 않는다")]
        [SerializeField, Min(0.05f)] private float _maxAgeSeconds = 0.5f;

        private readonly List<Active>      _active = new List<Active>();
        private readonly Stack<GameObject> _idle   = new Stack<GameObject>();
        private readonly List<GameObject>  _all    = new List<GameObject>();

        /****************************************
        *              Properties
        ****************************************/

        public int ActiveCount => _active.Count;

        public int IdleCount => _idle.Count;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            float now = Time.time;
            for (int i = _active.Count - 1; i >= 0; --i)
            {
                if (now < _active[i].EndTime)
                {
                    continue;
                }

                Release(_active[i]);
                _active.RemoveAt(i);
            }
        }

        private void OnDisable()
        {
            ClearAll();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _all.Count; ++i)
            {
                if (_all[i] != null)
                {
                    Destroy(_all[i]);
                }
            }

            _all.Clear();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 피격 임팩트를 터뜨린다. 그릴 수 없으면 false이고 아무것도 바꾸지 않는다 </summary>
        public bool Play(in WolbaekPlayerHit hit, double now)
        {
            if (IsServerBuild || (_impactPrefab == null) || (_active.Count >= _maxActive)
                || (!WolbaekHitImpactRules.IsFresh(hit.ServerTime, now, _maxAgeSeconds)))
            {
                return false;
            }

            GameObject instance = (_idle.Count > 0) ? _idle.Pop() : Create();
            Transform  t        = instance.transform;
            t.SetPositionAndRotation(hit.Position, WolbaekHitImpactRules.ResolveRotation(hit.Direction));
            t.localScale = Vector3.one * _scale;
            instance.SetActive(true);

            _active.Add(new Active { Instance = instance, EndTime = Time.time + _lifetimeSeconds });
            return true;
        }

        public void ClearAll()
        {
            for (int i = _active.Count - 1; i >= 0; --i)
            {
                Release(_active[i]);
            }

            _active.Clear();
        }

        // 부모를 두지 않는다. 보스가 움직이거나 도는 동안에도 임팩트는 맞은 자리에 남아야 한다
        private GameObject Create()
        {
            GameObject instance = Instantiate(_impactPrefab);
            instance.SetActive(false);
            _all.Add(instance);
            return instance;
        }

        private void Release(Active active)
        {
            active.Instance.SetActive(false);
            _idle.Push(active.Instance);
        }
    }
}
