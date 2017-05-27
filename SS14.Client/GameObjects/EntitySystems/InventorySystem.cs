using SS14.Shared.GameObjects;
using SS14.Shared.GameObjects.System;

namespace SS14.Client.GameObjects.EntitySystems
{
    internal class InventorySystem : EntitySystem
    {
        new private float updateFrequency = 0.1f;

        public InventorySystem(EntityManager em, EntitySystemManager esm)
            : base(em, esm)
        {
            EntityQuery = new EntityQuery();
        }

        public override void Update(float frameTime)
        {

        }
    }
}