using Godot;
using UltraSim.Client.Runtime;

namespace Client.Godot.Prefabs.Mobiles
{
    [GlobalClass]
    public partial class CharacterModelPresenter : Node
    {
        public CharacterRoot? Character { get; private set; }
        public MobileDefinition? Definition { get; private set; }
        public float FacingYawDegrees { get; private set; }
        public CharacterLocomotionMode LocomotionMode { get; private set; } = CharacterLocomotionMode.Idle;
        public float LocomotionSpeedNormalized { get; private set; }
        public AnimationCue? LastAnimationCue { get; private set; }

        public void Attach(CharacterRoot character, MobileDefinition definition)
        {
            Character = character;
            Definition = definition;
            OnAttached(character, definition);
        }

        public void Detach()
        {
            if (Character != null || Definition != null)
            {
                OnDetaching();
            }

            Character = null;
            Definition = null;
            FacingYawDegrees = 0.0f;
            LocomotionMode = CharacterLocomotionMode.Idle;
            LocomotionSpeedNormalized = 0.0f;
            LastAnimationCue = null;
        }

        public void SetFacingYawDegrees(float facingYawDegrees)
        {
            FacingYawDegrees = facingYawDegrees;
            OnFacingYawChanged(facingYawDegrees);
        }

        public void SetLocomotion(CharacterLocomotionMode locomotionMode, float locomotionSpeedNormalized)
        {
            LocomotionMode = locomotionMode;
            LocomotionSpeedNormalized = locomotionSpeedNormalized;
            OnLocomotionChanged(locomotionMode, locomotionSpeedNormalized);
        }

        public void PlayAnimation(AnimationCue cue)
        {
            LastAnimationCue = cue;
            OnAnimationRequested(cue);
        }

        protected virtual void OnAttached(CharacterRoot character, MobileDefinition definition)
        {
        }

        protected virtual void OnDetaching()
        {
        }

        protected virtual void OnFacingYawChanged(float facingYawDegrees)
        {
        }

        protected virtual void OnLocomotionChanged(CharacterLocomotionMode locomotionMode, float locomotionSpeedNormalized)
        {
        }

        protected virtual void OnAnimationRequested(AnimationCue cue)
        {
        }
    }
}
