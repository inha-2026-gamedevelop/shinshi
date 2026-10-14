// Unity
using UnityEngine;

namespace Shinshi.Art
{
    public enum EWolbaekWarpKind
    {
        Breakup,
        Reform,
        Landing
    }

    [CreateAssetMenu(menuName = "Shinshi/VFX/Wolbaek Warp", fileName = "SO_WolbaekWarp")]
    public sealed class WolbaekWarpSettingsSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/
        [SerializeField, Min(0.001f)] private float _bodySeconds = 0.95f;
        [SerializeField, Min(0.001f)] private float _impactSeconds = 0.65f;
        [SerializeField, Min(0.001f)] private float _flowCycle = 1.5f;
        [SerializeField, Min(0f)] private float _petalDrift = 0.65f;
        [SerializeField, Range(0f, 1f)] private float _mistOpacity = 0.8f;
        [SerializeField, Min(0f)] private float _confirmationGrace = 0.5f;
        [SerializeField, Min(0f)] private float _residualSeconds = 1.4f;

        /****************************************
        *              Properties
        ****************************************/
        public float BodySeconds => _bodySeconds;
        public float ImpactSeconds => _impactSeconds;
        public float FlowCycle => _flowCycle;
        public float PetalDrift => _petalDrift;
        public float MistOpacity => _mistOpacity;
        public float ConfirmationGrace => _confirmationGrace;
        public float ResidualSeconds => _residualSeconds;
    }
}
