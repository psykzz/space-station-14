using SS14.Shared;
using SS14.Shared.GameObjects;
using SS14.Shared.GameObjects.System;

namespace SS14.Server.GameObjects.EntitySystems
{
    internal class InputSystem : EntitySystem
    {

        new private float updateFrequency = 0.1f;

        public InputSystem(EntityManager em, EntitySystemManager esm)
            : base(em, esm)
        {
            EntityQuery = new EntityQuery();
            EntityQuery.OneSet.Add(typeof (KeyBindingInputComponent));
        }

        private void UpdateAnimationState(Entity entity) {
            var inputs = entity.GetComponent<KeyBindingInputComponent>(ComponentFamily.Input);

            //Animation setting
            if (entity.GetComponent(ComponentFamily.Renderable) is AnimatedSpriteComponent)
            {
                var animation = entity.GetComponent<AnimatedSpriteComponent>(ComponentFamily.Renderable);
                if (inputs.GetKeyState(BoundKeyFunctions.MoveRight) ||
                    inputs.GetKeyState(BoundKeyFunctions.MoveDown) ||
                    inputs.GetKeyState(BoundKeyFunctions.MoveLeft) ||
                    inputs.GetKeyState(BoundKeyFunctions.MoveUp))
                {
                    if (inputs.GetKeyState(BoundKeyFunctions.Run))
                    {
                        animation.SetAnimationState("run");
                    }
                    else
                    {
                        animation.SetAnimationState("walk");
                    }
                }
                else
                {
                    animation.SetAnimationState("idle");
                }
            }
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);
            if (timeSinceLastUpdate > updateFrequency)
            {
                return;
            }
            else
            {
                timeSinceLastUpdate = 0.0f;
            }

            var entities = EntityManager.GetEntities(EntityQuery);
            foreach(var entity in entities)
            {
                UpdateAnimationState(entity);
            }
        }
    }
}
