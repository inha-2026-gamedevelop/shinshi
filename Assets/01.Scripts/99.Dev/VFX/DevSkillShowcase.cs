#if UNITY_EDITOR || DEVELOPMENT_BUILD
// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// 서드파티
using TMPro;

namespace Shinshi.Dev
{
    /// <summary> 실제 클립과 게임용 VFX를 원속도로 비교하는 전용 시연. 전투 판정과 네트워크 상태는 만들지 않는다 </summary>
    public sealed class DevSkillShowcase : MonoBehaviour
    {
        [Serializable]
        public sealed class Cue
        {
            public GameObject Prefab;
            public float Time;
            public float Life = 1f;
            public float Speed;
            public float Range;
            public float Angle;
        }

        [Serializable]
        public sealed class Entry
        {
            public string Title;
            public string Note;
            public int ActorIndex;
            public AnimationClip[] Clips;
            public Cue[] Cues;
            public float MoveDistance;
            public float MoveStart;
            public float MoveDuration = 1f;
        }

        private const float ENTRY_SECONDS = 7f;
        private const float MOTION_START = 0.6f;
        private const float TRAIL_SECONDS = 0.12f;
        private const float LAST_FRAME_MARGIN = 0.001f;

        [SerializeField] private Camera _camera;
        [SerializeField] private Animator[] _actors;
        [SerializeField] private Entry[] _entries;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _subtitle;
        [SerializeField] private TMP_Text _phase;

        private PlayableGraph _graph;
        private AnimationClipPlayable _playable;
        private AnimationClip _clip;
        private int _entryIndex = -1;
        private bool _manual;
        private float _clock;
        private GameObject[] _effects;
        private ParticleSystem[][] _particles;
        private TrailRenderer[][] _trails;
        private readonly List<SkinnedMeshRenderer> _captureSkins = new List<SkinnedMeshRenderer>();
        private readonly List<GameObject> _captureObjects = new List<GameObject>();
        private readonly List<Mesh> _captureMeshes = new List<Mesh>();
        private ParentConstraint[] _captureConstraints;

        public Camera ShowCamera => _camera;
        public float Duration => ((_entries == null) ? 0 : _entries.Length) * ENTRY_SECONDS;
        public Entry[] Entries => _entries;

        private void Update()
        {
            if (_manual || (Duration <= 0f))
            {
                return;
            }

            _clock = (_clock + Time.deltaTime) % Duration;
            EvaluateFrame(_clock, Time.deltaTime);
        }

        private void OnDisable()
        {
            ClearPresentation();
        }

        public void Configure(Camera camera, Animator[] actors, Entry[] entries, TMP_Text title, TMP_Text subtitle, TMP_Text phase)
        {
            _camera = camera;
            _actors = actors;
            _entries = entries;
            _title = title;
            _subtitle = subtitle;
            _phase = phase;
        }

        public void BeginManual()
        {
            _manual = true;
            ClearPresentation();
            _captureConstraints = GetComponentsInChildren<ParentConstraint>(true);
            foreach (Animator actor in _actors)
            {
                foreach (SkinnedMeshRenderer skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin.enabled) { continue; }
                    GameObject proxy = new GameObject("CaptureSkin", typeof(MeshFilter), typeof(MeshRenderer));
                    proxy.layer = gameObject.layer;
                    proxy.transform.SetParent(skin.transform, false);
                    Mesh mesh = new Mesh();
                    proxy.GetComponent<MeshFilter>().sharedMesh = mesh;
                    proxy.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                    _captureSkins.Add(skin);
                    _captureObjects.Add(proxy);
                    _captureMeshes.Add(mesh);
                    skin.enabled = false;
                }
            }
        }

        public void EndManual()
        {
            ClearPresentation();
            for (int i = 0; i < _captureSkins.Count; ++i)
            {
                _captureSkins[i].enabled = true;
                DestroyImmediate(_captureObjects[i]);
                DestroyImmediate(_captureMeshes[i]);
            }
            _captureSkins.Clear();
            _captureObjects.Clear();
            _captureMeshes.Clear();
            _manual = false;
            _clock = 0f;
        }

        public void EvaluateFrame(float time, float delta)
        {
            if ((Duration <= 0f) || (_actors == null))
            {
                return;
            }

            float bounded = Mathf.Clamp(time, 0f, Duration - LAST_FRAME_MARGIN);
            int index = Mathf.FloorToInt(bounded / ENTRY_SECONDS);
            if (_entryIndex != index)
            {
                SelectEntry(index);
            }

            Entry entry = _entries[index];
            float local = bounded - (index * ENTRY_SECONDS);
            float motionTime = Mathf.Max(0f, local - MOTION_START);
            Animator actor = _actors[entry.ActorIndex];
            SampleMotion(actor, entry.Clips, motionTime);
            actor.transform.localPosition = OffsetAt(entry, motionTime);
            actor.transform.localRotation = Quaternion.identity;
            if (_manual && (_captureConstraints != null))
            {
                foreach (ParentConstraint constraint in _captureConstraints)
                {
                    if ((constraint == null) || !constraint.gameObject.activeInHierarchy || constraint.sourceCount != 1) { continue; }
                    Transform source = constraint.GetSource(0).sourceTransform;
                    constraint.transform.SetPositionAndRotation(source.TransformPoint(constraint.GetTranslationOffset(0)), source.rotation * Quaternion.Euler(constraint.GetRotationOffset(0)));
                }
            }
            // EditMode 렌더 요청은 스킨 갱신보다 먼저 실행될 수 있어 현재 뼈대 포즈를 명시적으로 굽는다.
            for (int i = 0; i < _captureSkins.Count; ++i)
            {
                if (_captureSkins[i].gameObject.activeInHierarchy) { _captureSkins[i].BakeMesh(_captureMeshes[i]); }
            }
            float firstCue = float.MaxValue;
            for (int i = 0; i < entry.Cues.Length; ++i)
            {
                Cue cue = entry.Cues[i];
                firstCue = Mathf.Min(firstCue, cue.Time);
                float elapsed = local - MOTION_START - cue.Time;
                bool active = (elapsed >= 0f) && (elapsed < cue.Life);
                if (_effects[i] == null)
                {
                    continue;
                }

                _effects[i].SetActive(active);
                if (!active)
                {
                    continue;
                }

                Vector3 direction = Quaternion.Euler(0f, cue.Angle, 0f) * Vector3.forward;
                float distance = (cue.Speed > 0f) ? Mathf.Min(cue.Range, elapsed * cue.Speed) : 0f;
                _effects[i].transform.SetPositionAndRotation(OffsetAt(entry, cue.Time) + (direction * distance), Quaternion.LookRotation(direction));
                for (int p = 0; p < _particles[i].Length; ++p)
                {
                    ParticleSystem particle = _particles[i][p];
                    if (particle.gameObject.activeInHierarchy)
                    {
                        particle.Simulate(elapsed, false, true, true);
                    }
                }

                foreach (TrailRenderer trail in _trails[i])
                {
                    trail.Clear();
                    trail.emitting = false;
                    float length = Mathf.Min(distance, cue.Speed * TRAIL_SECONDS);
                    if (length > 0f)
                    {
                        trail.AddPosition(trail.transform.position - (direction * length));
                        trail.AddPosition(trail.transform.position);
                    }
                }
            }

            _title.text = entry.Title;
            _subtitle.text = entry.Note;
            string phase = (local < MOTION_START) ? "준비" : (motionTime < firstCue) ? "동작 준비" : "모션 + VFX 시연";
            _phase.text = string.Format("{0:00} / {1:00}     {2}     {3:0.0} / {4:0.0} s", index + 1, _entries.Length, phase, local, ENTRY_SECONDS);
        }

        private void SelectEntry(int index)
        {
            ClearPresentation();
            _entryIndex = index;
            Entry entry = _entries[index];
            for (int i = 0; i < _actors.Length; ++i)
            {
                _actors[i].gameObject.SetActive(i == entry.ActorIndex);
                _actors[i].transform.localPosition = Vector3.zero;
            }

            _effects = new GameObject[entry.Cues.Length];
            _particles = new ParticleSystem[entry.Cues.Length][];
            _trails = new TrailRenderer[entry.Cues.Length][];
            for (int i = 0; i < entry.Cues.Length; ++i)
            {
                if (entry.Cues[i].Prefab == null)
                {
                    continue;
                }

                _effects[i] = Instantiate(entry.Cues[i].Prefab, transform);
                _effects[i].name = "ShowcaseEffect_" + i;
                foreach (Transform child in _effects[i].GetComponentsInChildren<Transform>(true))
                {
                    child.gameObject.layer = gameObject.layer;
                }
                _effects[i].SetActive(false);
                _particles[i] = _effects[i].GetComponentsInChildren<ParticleSystem>(true);
                _trails[i] = _effects[i].GetComponentsInChildren<TrailRenderer>(true);
            }
        }

        private void SampleMotion(Animator actor, AnimationClip[] clips, float time)
        {
            if ((clips == null) || (clips.Length == 0))
            {
                return;
            }

            AnimationClip selected = clips[clips.Length - 1];
            float position = time;
            foreach (AnimationClip candidate in clips)
            {
                selected = candidate;
                if (position < candidate.length)
                {
                    break;
                }

                if (candidate != clips[clips.Length - 1])
                {
                    position -= candidate.length;
                }
            }

            position = Mathf.Clamp(position, 0f, selected.length - LAST_FRAME_MARGIN);
            if (_clip != selected)
            {
                if (_graph.IsValid())
                {
                    _graph.Destroy();
                }

                _graph = PlayableGraph.Create("SkillShowcase");
                _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                _playable = AnimationClipPlayable.Create(_graph, selected);
                _playable.SetApplyFootIK(false);
                AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Actor", actor);
                output.SetSourcePlayable(_playable);
                _graph.Play();
                _clip = selected;
            }

            _playable.SetTime(position);
            _graph.Evaluate(0f);
        }

        private static Vector3 OffsetAt(Entry entry, float time)
        {
            float fraction = Mathf.Clamp01((time - entry.MoveStart) / Mathf.Max(LAST_FRAME_MARGIN, entry.MoveDuration));
            return Vector3.forward * (fraction * entry.MoveDistance);
        }

        private void ClearPresentation()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }

            _clip = null;
            _entryIndex = -1;
            if (_effects != null)
            {
                foreach (GameObject effect in _effects)
                {
                    if (effect == null)
                    {
                        continue;
                    }

                    if (Application.isPlaying)
                    {
                        Destroy(effect);
                    }
                    else
                    {
                        DestroyImmediate(effect);
                    }
                }
            }

            _effects = null;
        }
    }
}
#endif
