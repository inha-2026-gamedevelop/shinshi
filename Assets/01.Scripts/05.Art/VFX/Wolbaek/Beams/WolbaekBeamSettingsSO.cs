// Unity
using UnityEngine;

namespace Shinshi.Art
{
    public enum EWolbaekBeamKind
    {
        Release,
        Sustain,
        Star,
        Grid
    }

    [CreateAssetMenu(menuName = "Shinshi/VFX/Wolbaek Beam", fileName = "SO_WolbaekBeam")]
    public sealed class WolbaekBeamSettingsSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField, Min(0.001f)] private float _appearSeconds     = 0.12f;
        [SerializeField, Min(0.001f)] private float _fadeSeconds       = 0.16f;
        [SerializeField, Min(0f)]     private float _confirmationGrace = 1f;
        [SerializeField, Min(0.001f)] private float _flowCycle         = 1.1f;
        [SerializeField, Min(0.001f)] private float _height            = 0.55f;
        [SerializeField, Range(0f, 1f)] private float _veilOpacity     = 0.5f;
        [SerializeField, Range(0f, 1f)] private float _coreOpacity     = 0.45f;
        [SerializeField, Min(0.001f)] private float _previewWarning    = 1.2f;
        [SerializeField, Min(0.001f)] private float _previewActive     = 1.4f;
        [SerializeField, Min(0.001f)] private float _previewWidth      = 0.65f;
        [SerializeField, Min(0.001f)] private float _previewLength     = 6f;

        /****************************************
        *              Properties
        ****************************************/

        public float AppearSeconds     => _appearSeconds;
        public float FadeSeconds       => _fadeSeconds;
        public float ConfirmationGrace => _confirmationGrace;
        public float FlowCycle         => _flowCycle;
        public float Height            => _height;
        public float VeilOpacity       => _veilOpacity;
        public float CoreOpacity       => _coreOpacity;
        public float PreviewWarning    => _previewWarning;
        public float PreviewActive     => _previewActive;
        public float PreviewWidth      => _previewWidth;
        public float PreviewLength     => _previewLength;
    }

    public readonly struct WolbaekBeamShape
    {
        /****************************************
        *              Properties
        ****************************************/

        public Vector3 Start       { get; }
        public Vector3 End         { get; }
        public float Width         { get; }
        public Vector3 ArenaCenter { get; }
        public float ArenaRadius   { get; }

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekBeamShape(Vector3 start, Vector3 end, float width,
            Vector3 arenaCenter = default, float arenaRadius = 0f)
        {
            Start       = start;
            End         = end;
            Width       = width;
            ArenaCenter = arenaCenter;
            ArenaRadius = arenaRadius;
        }
    }
}
