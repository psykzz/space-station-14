using SS14.Shared.GameObjects;
using SS14.Shared.GameObjects.System;
using SS14.Shared.Maths;

namespace SS14.Server.GameObjects.EntitySystems
{
    internal class PhysicsSystem : EntitySystem
    {

        new private float updateFrequency = 0.1f;

        public PhysicsSystem(EntityManager em, EntitySystemManager esm)
            : base(em, esm)
        {
            EntityQuery = new EntityQuery();
            EntityQuery.AllSet.Add(typeof(PhysicsComponent));
            EntityQuery.AllSet.Add(typeof(VelocityComponent));
            EntityQuery.AllSet.Add(typeof(TransformComponent));
            EntityQuery.Exclusionset.Add(typeof(SlaveMoverComponent));
            EntityQuery.Exclusionset.Add(typeof(PlayerInputMoverComponent));
        }

        private void UpdateEntityPhysics(Entity entity, float frameTime)
        {
            //GasEffect(entity, frametime);

            var transform = entity.GetComponent<TransformComponent>(ComponentFamily.Transform);
            var velocity = entity.GetComponent<VelocityComponent>(ComponentFamily.Velocity);

            if (velocity.Velocity.LengthSquared() < 0.00001f)
                return;
            //Decelerate
            velocity.Velocity -= (velocity.Velocity * (frameTime * 0.01f));

            var movement = velocity.Velocity * frameTime;
            //Apply velocity
            transform.Position += movement;
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
                UpdateEntityPhysics(entity, frameTime);
            }
        }

        //private void GasEffect(Entity entity, float frameTime)
        //{
        //    var transform = entity.GetComponent<TransformComponent>(ComponentFamily.Transform);
        //    var physics = entity.GetComponent<PhysicsComponent>(ComponentFamily.Physics);
        //    ITile t =
        //        IoCManager.Resolve<IMapManager>().GetFloorAt(transform.Position);
        //    if (t == null)
        //        return;
        //    var gasVel = t.GasCell.GasVelocity;
        //    if (gasVel.Abs() > physics.Mass) // Stop tiny wobbles
        //    {
        //        transform.Position = new Vector2(transform.X + (gasVel.X * frameTime), transform.Y + (gasVel.Y * frameTime));
        //    }
        //}
    }
}
