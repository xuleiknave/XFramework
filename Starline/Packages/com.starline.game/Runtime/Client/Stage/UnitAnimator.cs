using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Starline.Client
{
    /// <summary>
    /// Plays the clips exported with a cartoon model (idle, walk, attack, hit, death; see
    /// tools/models/build_models.py). Idle or walk loops underneath; attack and hit play once
    /// and fade back; death plays once and holds its last frame. Built on the Playables API,
    /// so no AnimatorController asset is needed.
    /// </summary>
    public sealed class UnitAnimator : MonoBehaviour
    {
        private const float FadeSeconds = 0.1f;

        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private readonly Dictionary<string, int> _slots = new Dictionary<string, int>();
        private readonly List<AnimationClipPlayable> _playables = new List<AnimationClipPlayable>();
        private float[] _weights;
        private int _loop = -1;
        private int _once = -1;
        private string _onceName;
        private float _onceEnd;
        private bool _dead;

        /// <summary>Adds an animator to the model, or returns null when the model has no clips (older static models).</summary>
        public static UnitAnimator Attach(GameObject model, IReadOnlyList<AnimationClip> clips)
        {
            if (clips == null || clips.Count == 0)
            {
                return null;
            }
            Animator animator = model.GetComponent<Animator>();
            if (animator == null)
            {
                animator = model.AddComponent<Animator>();
            }
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            var unitAnimator = model.AddComponent<UnitAnimator>();
            unitAnimator.Build(animator, clips);
            return unitAnimator;
        }

        /// <summary>Clips come out of Blender named "&lt;rig&gt;|idle"; this returns "idle".</summary>
        public static string ClipName(string name)
        {
            int bar = name.LastIndexOf('|');
            return bar >= 0 ? name.Substring(bar + 1) : name;
        }

        public bool IsDead => _dead;

        /// <summary>Switches the looping layer between idle and walk.</summary>
        public void SetMoving(bool moving)
        {
            int slot = Slot(moving ? "walk" : "idle");
            if (_dead || slot < 0 || slot == _loop)
            {
                return;
            }
            _loop = slot;
            _playables[slot].SetTime(0);
        }

        /// <summary>
        /// Plays attack, hit or death once and returns its length in seconds (0 when the clip is missing).
        /// A hit never interrupts an attack; nothing interrupts death.
        /// </summary>
        public float Play(string clip)
        {
            int slot = Slot(clip);
            if (_dead || slot < 0)
            {
                return 0f;
            }
            if (clip == "hit" && _onceName == "attack" && Time.time < _onceEnd)
            {
                return 0f;
            }
            AnimationClipPlayable playable = _playables[slot];
            float length = playable.GetAnimationClip().length;
            _once = slot;
            _onceName = clip;
            _onceEnd = Time.time + length;
            playable.SetTime(0);
            playable.SetSpeed(1);
            if (clip == "death")
            {
                _dead = true;
            }
            return length;
        }

        private void Build(Animator animator, IReadOnlyList<AnimationClip> clips)
        {
            _graph = PlayableGraph.Create(name + " animation");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "Unit", animator);
            _mixer = AnimationMixerPlayable.Create(_graph, clips.Count);
            output.SetSourcePlayable(_mixer);
            for (int i = 0; i < clips.Count; i++)
            {
                AnimationClipPlayable playable = AnimationClipPlayable.Create(_graph, clips[i]);
                playable.SetApplyFootIK(false);
                _graph.Connect(playable, 0, _mixer, i);
                _playables.Add(playable);
                _slots[ClipName(clips[i].name)] = i;
            }
            _weights = new float[clips.Count];
            _loop = Slot("idle");
            if (_loop >= 0)
            {
                _weights[_loop] = 1f;
                // Start units at different points of the idle loop so a row of the same unit does not breathe in step.
                _playables[_loop].SetTime(Random.value * _playables[_loop].GetAnimationClip().length);
            }
            ApplyWeights();
            _graph.Play();
        }

        private int Slot(string clip) => _slots.TryGetValue(clip, out int slot) ? slot : -1;

        private void Update()
        {
            if (!_graph.IsValid())
            {
                return;
            }
            if (_loop >= 0)
            {
                AnimationClipPlayable loop = _playables[_loop];
                double length = loop.GetAnimationClip().length;
                if (length > 0 && loop.GetTime() >= length)
                {
                    loop.SetTime(loop.GetTime() % length);
                }
            }
            if (_once >= 0 && !_dead && Time.time >= _onceEnd)
            {
                _once = -1;
                _onceName = null;
            }
            if (_dead && _once >= 0)
            {
                AnimationClipPlayable death = _playables[_once];
                double length = death.GetAnimationClip().length;
                if (death.GetTime() >= length)
                {
                    death.SetTime(length);
                    death.SetSpeed(0);
                }
            }
            int active = _once >= 0 ? _once : _loop;
            float step = Time.deltaTime / FadeSeconds;
            for (int i = 0; i < _weights.Length; i++)
            {
                _weights[i] = Mathf.MoveTowards(_weights[i], i == active ? 1f : 0f, step);
            }
            ApplyWeights();
        }

        private void ApplyWeights()
        {
            for (int i = 0; i < _weights.Length; i++)
            {
                _mixer.SetInputWeight(i, _weights[i]);
            }
        }

        private void OnDestroy()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
        }
    }
}
