﻿using SS14.Client.Interfaces.Resource;
using SS14.Shared.GameObjects;
using SS14.Shared.GameObjects.System;
using SS14.Shared.IoC;

namespace SS14.Client.GameObjects.EntitySystems
{
    internal class ParticleSystem : EntitySystem
    {
        new private float updateFrequency = 0.1f;

        public ParticleSystem(EntityManager em, EntitySystemManager esm)
            : base(em, esm)
        {
            EntityQuery = new EntityQuery();
            EntityQuery.OneSet.Add(typeof(ParticleSystemComponent));
        }

        public void AddParticleSystem(Entity ent, string systemName)
        {
            ParticleSettings settings = IoCManager.Resolve<IResourceManager>().GetParticles(systemName);
            if (settings != null)
            {
                //Add it.
            }
        }

        public override void RegisterMessageTypes()
        {
            //EntitySystemManager.RegisterMessageType<>();
            base.RegisterMessageTypes();
        }

        public override void Update(float frameTime)
        {}

        public override void HandleNetMessage(EntitySystemMessage sysMsg)
        {
            base.HandleNetMessage(sysMsg);
        }
    }
}
