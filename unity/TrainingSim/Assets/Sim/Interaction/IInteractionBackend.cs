using UnityEngine;

namespace Vid2Sim.Interaction
{
    /// <summary>Attaches the right interaction components to a spawned part and wires them to PartBehaviour.</summary>
    public interface IInteractionBackend
    {
        string Name { get; }
        void Attach(PartBehaviour part, GameObject go, string interaction, bool needsRotate);
    }

    public static class InteractionBackends
    {
        public static IInteractionBackend Create()
        {
#if META_INTERACTION_SDK
            return new MetaInteractionBackend();
#else
            return new SimpleInteractionBackend();
#endif
        }
    }
}
