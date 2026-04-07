using System;
using Animancer;
using UnityEngine;

namespace GameLogic
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class SkillGraphAnimancerPlayer : MonoBehaviour
    {
        [SerializeField]
        private Animator _animator;

        [SerializeField]
        private AnimancerComponent _animancer;

        [SerializeField]
        private AnimationClip _clip;

        [SerializeField]
        [Min(0f)]
        private float _fadeDuration = 0.25f;

        public bool HasConfiguredClip => _clip != null;

        private void Reset()
        {
            CacheComponents(true);
        }

        private void Awake()
        {
            CacheComponents(false);
        }

        public void Play(float speed = 1f)
        {
            if (_clip == null)
                throw new InvalidOperationException($"{nameof(SkillGraphAnimancerPlayer)} on '{name}' is missing an AnimationClip.");

            CacheComponents(true);
            ConfigureAnimancer();

            AnimancerState state = _fadeDuration > 0f
                ? _animancer.Play(_clip, _fadeDuration)
                : _animancer.Play(_clip);

            if (state != null)
            {
                state.Time = 0f;
                state.Speed = speed;
            }
        }

        private void CacheComponents(bool createAnimancerIfMissing)
        {
            if (_animator == null)
                TryGetComponent(out _animator);

            if (_animancer == null)
                _animancer = GetComponent<AnimancerComponent>();

            if (createAnimancerIfMissing && _animancer == null)
                _animancer = gameObject.AddComponent<AnimancerComponent>();
        }

        private void ConfigureAnimancer()
        {
            if (_animator == null)
                throw new InvalidOperationException($"{nameof(SkillGraphAnimancerPlayer)} on '{name}' is missing an Animator.");

            if (_animancer == null)
                throw new InvalidOperationException($"{nameof(SkillGraphAnimancerPlayer)} on '{name}' is missing an AnimancerComponent.");

            if (_animator.runtimeAnimatorController != null)
                _animator.runtimeAnimatorController = null;

            if (_animancer.Animator != _animator)
                _animancer.Animator = _animator;
        }
    }
}
