// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 일반 적 시체의 뼈에 런타임으로 리지드바디, 캡슐 충돌체, 조인트를 붙여 팔다리가 늘어지게 하는 래그돌 </summary>
    public sealed class EnemyCorpseRagdoll : MonoBehaviour
    {
        // 모델에 래그돌이 없어 스킨드 메시의 뼈 계층에서 자동으로 만든다. 뼈마다 가장 먼 자식 뼈까지를 한 마디로 보고,
        // 너무 짧은 뼈(손가락, 꼬리 끝)는 물리를 달지 않아 부모 마디를 따라가게 한다.
        // 시체는 어떤 판정에도 참여하지 않는다. 뼈를 Ignore Raycast 레이어에 두어 클라이언트 조준과 카메라 레이캐스트에 잡히지 않게 하고,
        // 같은 레이어(다른 시체와 적의 분리용 충돌체)와는 부딪히지 않게 한다. 지면은 Default와 Obstacle이라 그대로 부딪힌다.
        // 일정 시간이 지나거나 모두 잠들면 키네마틱으로 굳혀 물리 비용을 없앤다

        /****************************************
        *                Fields
        ****************************************/

        private const float MIN_BONE_LENGTH   = 0.15f; // m, 이보다 짧은 마디에는 물리를 달지 않는다
        private const float RADIUS_RATIO      = 0.22f; // 마디 길이 대비 캡슐 반지름
        private const float MIN_RADIUS        = 0.05f; // m
        private const float MAX_RADIUS        = 0.16f; // m
        private const float MASS_PER_METER    = 18f;   // kg/m
        private const float MIN_MASS          = 0.5f;  // kg
        private const float LINEAR_DRAG       = 0.15f;
        private const float ANGULAR_DRAG      = 1.5f;
        private const float MAX_DEPENETRATION = 2f;    // m/s
        private const float FLY_SPEED         = 8f;    // m/s, 수평
        private const float FLY_UP_SPEED      = 5f;    // m/s
        private const float SPIN_SPEED        = 5f;    // rad/s
        private const float TWIST_LIMIT       = 30f;   // 도
        private const float SWING_LIMIT       = 45f;   // 도
        private const float FREEZE_SECONDS    = 3f;    // 이 뒤에는 물리를 끈다
        private const float SLEEP_CHECK_START = 1f;    // 이 뒤부터 모두 잠들었는지 본다
        private const float HALF              = 0.5f;
        private const float MIN_DIRECTION_SQR = 0.0001f;
        private const string SKIP_LAYER_NAME  = "Ignore Raycast";

        private readonly List<Rigidbody> _bodies = new List<Rigidbody>();

        private float _elapsed;
        private bool  _frozen;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if (_frozen)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            if ((_elapsed >= FREEZE_SECONDS) || ((_elapsed >= SLEEP_CHECK_START) && AllSleeping()))
            {
                Freeze();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> owner의 스킨드 메시 뼈에 래그돌을 만들고 away(수평) 쪽으로 날린다. 만들 수 있는 마디가 없으면 false </summary>
        public static bool Build(GameObject owner, Vector3 away)
        {
            SkinnedMeshRenderer skinned = owner.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if ((skinned == null) || (skinned.rootBone == null))
            {
                return false;
            }

            HashSet<Transform> bones = new HashSet<Transform>(skinned.bones);
            bones.Add(skinned.rootBone);

            EnemyCorpseRagdoll ragdoll = owner.AddComponent<EnemyCorpseRagdoll>();
            int layer = LayerMask.NameToLayer(SKIP_LAYER_NAME);
            ragdoll.AddBodies(skinned.rootBone, bones, null, layer);

            if (ragdoll._bodies.Count == 0)
            {
                Destroy(ragdoll);
                return false;
            }

            // 몸이 뼈를 따라 움직이므로 원래 경계 밖으로 나가도 그려야 한다
            skinned.updateWhenOffscreen = true;
            ragdoll.Launch(away);
            return true;
        }

        // 뼈 계층을 내려가며 충분히 긴 마디에만 물리를 단다. parentBody는 가장 가까운 물리가 달린 조상이다
        private void AddBodies(Transform bone, HashSet<Transform> bones, Rigidbody parentBody, int layer)
        {
            List<Transform> children = new List<Transform>();
            CollectChildBones(bone, bones, children);

            Rigidbody body = parentBody;
            if (TryFindTip(bone, children, out Vector3 tipLocal))
            {
                float length = tipLocal.magnitude;
                if (length >= MIN_BONE_LENGTH)
                {
                    body = CreateBody(bone, tipLocal, length, parentBody, layer);
                }
            }

            for (int i = 0; i < children.Count; ++i)
            {
                AddBodies(children[i], bones, body, layer);
            }
        }

        // 스킨 뼈가 아닌 중간 노드는 건너뛰고 가장 가까운 자손 뼈를 모은다
        private static void CollectChildBones(Transform parent, HashSet<Transform> bones, List<Transform> result)
        {
            for (int i = 0; i < parent.childCount; ++i)
            {
                Transform child = parent.GetChild(i);
                if (bones.Contains(child))
                {
                    result.Add(child);
                }
                else
                {
                    CollectChildBones(child, bones, result);
                }
            }
        }

        // 자식 뼈 중 가장 먼 것의 로컬 위치를 마디의 끝으로 본다
        private static bool TryFindTip(Transform bone, List<Transform> children, out Vector3 tipLocal)
        {
            tipLocal = Vector3.zero;
            float far = 0f;
            for (int i = 0; i < children.Count; ++i)
            {
                Vector3 local = bone.InverseTransformPoint(children[i].position);
                if (local.sqrMagnitude > far)
                {
                    far      = local.sqrMagnitude;
                    tipLocal = local;
                }
            }

            return far > MIN_DIRECTION_SQR;
        }

        private Rigidbody CreateBody(Transform bone, Vector3 tipLocal, float length, Rigidbody parentBody, int layer)
        {
            Rigidbody body = bone.gameObject.AddComponent<Rigidbody>();
            body.mass                    = Mathf.Max(MIN_MASS, length * MASS_PER_METER);
            body.linearDamping           = LINEAR_DRAG;
            body.angularDamping          = ANGULAR_DRAG;
            body.maxDepenetrationVelocity = MAX_DEPENETRATION;
            body.interpolation           = RigidbodyInterpolation.None;
            body.collisionDetectionMode  = CollisionDetectionMode.Discrete;

            CapsuleCollider capsule = bone.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = DominantAxis(tipLocal);
            capsule.center    = tipLocal * HALF;
            capsule.height    = length;
            capsule.radius    = Mathf.Clamp(length * RADIUS_RATIO, MIN_RADIUS, MAX_RADIUS);
            if (layer >= 0)
            {
                bone.gameObject.layer  = layer;
                capsule.excludeLayers  = 1 << layer;
            }

            if (parentBody != null)
            {
                CharacterJoint joint = bone.gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody      = parentBody;
                joint.anchor             = Vector3.zero;
                joint.axis               = tipLocal.normalized;
                joint.enableProjection   = true;
                joint.enablePreprocessing = false;
                joint.lowTwistLimit      = new SoftJointLimit { limit = -TWIST_LIMIT };
                joint.highTwistLimit     = new SoftJointLimit { limit = TWIST_LIMIT };
                joint.swing1Limit        = new SoftJointLimit { limit = SWING_LIMIT };
                joint.swing2Limit        = new SoftJointLimit { limit = SWING_LIMIT };
            }

            _bodies.Add(body);
            return body;
        }

        // 캡슐이 길게 뻗는 로컬 축: 0=X, 1=Y, 2=Z
        private static int DominantAxis(Vector3 direction)
        {
            float x = Mathf.Abs(direction.x);
            float y = Mathf.Abs(direction.y);
            float z = Mathf.Abs(direction.z);
            if ((x >= y) && (x >= z))
            {
                return 0;
            }

            return (y >= z) ? 1 : 2;
        }

        private void Launch(Vector3 away)
        {
            away.y = 0f;
            away   = (away.sqrMagnitude > MIN_DIRECTION_SQR) ? away.normalized : (-transform.forward);

            Vector3 velocity = (away * FLY_SPEED) + (Vector3.up * FLY_UP_SPEED);
            for (int i = 0; i < _bodies.Count; ++i)
            {
                _bodies[i].linearVelocity  = velocity;
                _bodies[i].angularVelocity = Random.onUnitSphere * SPIN_SPEED;
            }
        }

        private bool AllSleeping()
        {
            for (int i = 0; i < _bodies.Count; ++i)
            {
                if (!_bodies[i].IsSleeping())
                {
                    return false;
                }
            }

            return true;
        }

        // 굳은 자세를 유지한 채 물리 비용을 없앤다
        private void Freeze()
        {
            _frozen = true;
            for (int i = 0; i < _bodies.Count; ++i)
            {
                if (_bodies[i] != null)
                {
                    _bodies[i].isKinematic = true;
                }
            }
        }
    }
}
