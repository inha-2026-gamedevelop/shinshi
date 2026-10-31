// Unity
using UnityEngine;

// Third Party
using DG.Tweening;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Dev
{
    /// <summary> 첫 착지 먼지와 단계적으로 번지는 지면 혈흔을 풀에서 재생한다 </summary>
    public sealed class DevCorpseLandingFx : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int POOL_SIZE = 12;
        private const float DUST_GRAVITY = -0.08f;
        private const float DUST_CONE_ANGLE = 78f;
        private const float DUST_CONE_RADIUS = 0.55f;
        private const float DUST_ALPHA_PEAK_TIME = 0.08f;
        private const float DUST_START_SCALE = 0.6f;
        private const float DUST_END_SCALE = 1.8f;
        private const float BLOOD_START_SCALE = 0.4f;
        private const float BLOOD_MIDDLE_SCALE = 0.7f;
        private const float BLOOD_MIDDLE_TIME_RATIO = 0.35f;
        private const float REFERENCE_IMPACT_SPEED = 6f;
        private const float MIN_IMPACT_SCALE = 0.75f;
        private const float MAX_IMPACT_SCALE = 1.25f;
        private const float SURFACE_OFFSET = 0.04f;

        [SerializeField] private Material _dustMaterial;
        [SerializeField, Min(0f)] private float _armDelay = 0.1f;
        [SerializeField, Min(0f)] private float _minimumImpactSpeed = 1.6f;
        [SerializeField, Min(1)] private int _dustCount = 16;
        [SerializeField] private Vector2 _dustSize = new Vector2(0.65f, 1.25f);
        [SerializeField] private Vector2 _dustSpeed = new Vector2(1f, 2.6f);
        [SerializeField] private Vector2 _dustLifetime = new Vector2(0.25f, 0.5f);
        [SerializeField] private Color _dustColor = new Color(0.57f, 0.49f, 0.38f, 0.32f);
        [SerializeField, Min(0.01f)] private float _spreadSeconds = 0.18f;
        [SerializeField, Min(0f)] private float _bloodSize = 2.1f;

        private HitFeedbackBlood _blood;
        private readonly Slot[] _slots = new Slot[POOL_SIZE];
        private int _next = 0;

        public float ArmDelay => _armDelay;
        public float MinimumImpactSpeed => _minimumImpactSpeed;
        public int PlayedCount { get; private set; }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            for (int i = 0; i < _slots.Length; ++i)
            {
                Slot slot = new Slot();
                GameObject child = new GameObject("LandingDust_" + i);
                child.transform.SetParent(transform, false);
                slot.Dust = child.AddComponent<ParticleSystem>();
                slot.Dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule main = slot.Dust.main;
                main.loop = false;
                main.playOnAwake = false;
                main.maxParticles = _dustCount;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(_dustLifetime.x, _dustLifetime.y);
                main.startSpeed = new ParticleSystem.MinMaxCurve(_dustSpeed.x, _dustSpeed.y);
                main.startSize = new ParticleSystem.MinMaxCurve(_dustSize.x, _dustSize.y);
                main.startColor = _dustColor;
                main.gravityModifier = DUST_GRAVITY;
                ParticleSystem.EmissionModule emission = slot.Dust.emission;
                emission.enabled = false;
                ParticleSystem.ShapeModule shape = slot.Dust.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = DUST_CONE_ANGLE;
                shape.radius = DUST_CONE_RADIUS;
                ParticleSystem.ColorOverLifetimeModule colors = slot.Dust.colorOverLifetime;
                colors.enabled = true;
                Gradient fade = new Gradient();
                fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, DUST_ALPHA_PEAK_TIME), new GradientAlphaKey(0f, 1f) });
                colors.color = fade;
                ParticleSystem.SizeOverLifetimeModule size = slot.Dust.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, DUST_START_SCALE, 1f, DUST_END_SCALE));
                child.GetComponent<ParticleSystemRenderer>().sharedMaterial = _dustMaterial;
                slot.Spread = DOTween.Sequence()
                    .AppendCallback(() => Stamp(slot, BLOOD_START_SCALE))
                    .AppendInterval(_spreadSeconds * BLOOD_MIDDLE_TIME_RATIO)
                    .AppendCallback(() => Stamp(slot, BLOOD_MIDDLE_SCALE))
                    .AppendInterval(_spreadSeconds * (1f - BLOOD_MIDDLE_TIME_RATIO))
                    .AppendCallback(() => Stamp(slot, 1f))
                    .SetAutoKill(false).Pause().SetLink(gameObject);
                _slots[i] = slot;
            }
        }

        private void OnDisable() { StopAll(); }

        private void OnDestroy()
        {
            foreach (Slot slot in _slots)
            {
                slot?.Spread?.Kill();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Init(HitFeedbackBlood blood) { _blood = blood; }

        public void Play(Vector3 point, Vector3 normal, float impactSpeed)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }
            Slot slot = _slots[_next];
            _next = (_next + 1) % _slots.Length;
            slot.Spread.Pause();
            slot.Point = point;
            slot.Size = _bloodSize * Mathf.Clamp(impactSpeed / REFERENCE_IMPACT_SPEED, MIN_IMPACT_SCALE, MAX_IMPACT_SCALE);
            slot.Dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            slot.Dust.transform.SetPositionAndRotation(point + (normal * SURFACE_OFFSET), Quaternion.LookRotation(normal));
            slot.Dust.Play();
            slot.Dust.Emit(_dustCount);
            slot.Spread.Restart();
            ++PlayedCount;
        }

        public void StopAll()
        {
            foreach (Slot slot in _slots)
            {
                if (slot != null)
                {
                    slot.Spread.Pause();
                    slot.Dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        private void Stamp(Slot slot, float scale)
        {
            if (_blood != null)
            {
                _blood.StampLandingBlood(slot.Point, slot.Size * scale);
            }
        }

        private sealed class Slot
        {
            public ParticleSystem Dust;
            public Sequence Spread;
            public Vector3 Point;
            public float Size;
        }
    }
}
