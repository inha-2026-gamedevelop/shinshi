#if UNITY_EDITOR || DEVELOPMENT_BUILD
// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Dev
{
    /// <summary> 서버 없이 75% 부적진 뷰를 직접 구동해 플레이어 머리 위 배정 문양을 확인한다 </summary>
    public sealed class WolbaekTalismanMarkPreview : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_PLAYERS      = 4;
        private const float DIRECTION_DIST = 8f;
        private const float STRUCT_RADIUS  = 2f;
        private const float MOVE_SPEED     = 6f;
        private const float HOLD_SECONDS   = 600f;

        [SerializeField] private GameObject _bossPrefab = null;
        [SerializeField] private GameObject _bodyPrefab = null; // 비우면 캡슐을 쓴다, 실제 모델 키로 확인할 때 넣는다

        private readonly List<Transform> _bodies = new List<Transform>();
        private WolbaekTalismanView _view;
        private GameObject _boss;
        private int _playerCount = 1;
        private int _cast;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if (_bodies.Count == 0 || _bodies[0] == null)
            {
                return;
            }

            Vector3 move = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            _bodies[0].position += move.normalized * (MOVE_SPEED * Time.deltaTime);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10f, 10f, 320f, 220f), GUI.skin.box);
            GUILayout.Label("75% 배정 문양 확인 (방향키/WASD로 1번 이동)");
            GUILayout.Label("인원 " + _playerCount);
            _playerCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(_playerCount, 1, MAX_PLAYERS));
            if (GUILayout.Button("시작"))
            {
                Begin();
            }
            if (GUILayout.Button("종료"))
            {
                End();
            }
            GUILayout.EndArea();
        }

        private void OnDisable()
        {
            End();
        }

        /****************************************
        *               Methods
        ****************************************/

        private void Begin()
        {
            End();
            if (_bossPrefab == null)
            {
                Debug.LogWarning("[WolbaekTalismanMarkPreview] 보스 프리팹이 비어 있다");
                return;
            }

            _boss = Instantiate(_bossPrefab, transform.position, Quaternion.identity);
            _view = _boss.GetComponentInChildren<WolbaekTalismanView>(true);
            if (_view == null)
            {
                Debug.LogWarning("[WolbaekTalismanMarkPreview] 보스 프리팹에 WolbaekTalismanView가 없다");
                return;
            }

            _view.SetServerClock(() => Time.timeAsDouble);
            var structures = new List<WolbaekTalismanStructure>(_playerCount);
            for (int i = 0; i < _playerCount; ++i)
            {
                float angle = i * Mathf.PI * 0.5f;
                Vector3 center = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * DIRECTION_DIST;
                // 색과 문양 번호는 같은 값을 쓴다
                structures.Add(new WolbaekTalismanStructure(i, (EWolbaekTalismanColor)i, i, center, STRUCT_RADIUS, 100 + i));

                GameObject body;
                if (_bodyPrefab != null)
                {
                    body = Instantiate(_bodyPrefab);
                }
                else
                {
                    body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    Destroy(body.GetComponent<Collider>());
                }
                body.name = "PreviewPlayer" + i;
                body.transform.position = center + (Vector3.back * 2f);
                _bodies.Add(body.transform);
                _view.SetMemberAnchor(100 + i, body.transform);
            }

            double now = Time.timeAsDouble;
            var start = new WolbaekTalismanStart(1, ++_cast, now, now, HOLD_SECONDS, transform.position, structures);
            _view.StartFormation(in start, now);
        }

        private void End()
        {
            if (_view != null)
            {
                _view.Clear();
            }
            for (int i = 0; i < _bodies.Count; ++i)
            {
                if (_view != null)
                {
                    _view.SetMemberAnchor(100 + i, null);
                }
                if (_bodies[i] != null)
                {
                    Destroy(_bodies[i].gameObject);
                }
            }
            _bodies.Clear();
            if (_boss != null)
            {
                Destroy(_boss);
            }
            _boss = null;
            _view = null;
        }
    }
}
#endif
