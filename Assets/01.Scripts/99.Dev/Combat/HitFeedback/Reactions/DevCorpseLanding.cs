// Unity
using UnityEngine;

namespace Shinshi.Dev
{
    /// <summary> 복제된 사체의 실제 첫 지면 충돌만 재생한다 </summary>
    public sealed class DevCorpseLanding : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MIN_GROUND_NORMAL = 0.55f;

        [SerializeField] private DevCorpseLandingFx _fx;
        private bool _played = false;
        private float _armedAt = float.PositiveInfinity;

        public bool HasLanded => _played;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _played = false;
            _armedAt = float.PositiveInfinity;
            Shinshi.AI.EnemyCorpse.RagdollLaunched += BindBones;
        }

        private void OnDisable()
        {
            Shinshi.AI.EnemyCorpse.RagdollLaunched -= BindBones;
        }

        /****************************************
        *                Methods
        ****************************************/

        public void PrepareClone(DevCorpseLandingFx fx) { _fx = fx; }

        public void BeginDirect(DevCorpseLandingFx fx, Collider[] colliders)
        {
            _fx = fx;
            BindBones(colliders);
        }

        private void BindBones(Collider[] colliders)
        {
            if ((_fx == null) || (colliders == null) || (colliders.Length == 0)
                || (!colliders[0].transform.IsChildOf(transform)))
            {
                return;
            }
            _armedAt = Time.time + _fx.ArmDelay;
            foreach (Collider bone in colliders)
            {
                if (!bone.TryGetComponent(out DevCorpseCollisionRelay relay))
                {
                    relay = bone.gameObject.AddComponent<DevCorpseCollisionRelay>();
                }
                relay.Init(this);
            }
        }

        public void OnBoneCollision(Collision collision)
        {
            if ((!isActiveAndEnabled) || (_played) || (_fx == null) || (Time.time < _armedAt)
                || (collision.rigidbody != null) || (collision.collider.isTrigger)
                || (collision.collider is CapsuleCollider) || (collision.collider is CharacterController))
            {
                return;
            }
            for (int i = 0; i < collision.contactCount; ++i)
            {
                ContactPoint contact = collision.GetContact(i);
                float speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
                if ((contact.normal.y < MIN_GROUND_NORMAL) || (speed < _fx.MinimumImpactSpeed))
                {
                    continue;
                }
                _played = true;
                _fx.Play(contact.point, contact.normal, speed);
                return;
            }
        }
    }
}
