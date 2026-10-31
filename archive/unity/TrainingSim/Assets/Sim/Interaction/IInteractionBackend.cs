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
        /// <summary>Tests and the XR Simulator set this to force a backend regardless of installed packages.</summary>
        public static IInteractionBackend Override;

        public static IInteractionBackend Create()
        {
            if (Override != null) return Override;
#if UNITY_WEBGL
            return new SimpleInteractionBackend();   // browser output: mouse-first, same engine
#elif META_INTERACTION_SDK
            return new MetaInteractionBackend();
#else
            return new SimpleInteractionBackend();
#endif
        }
    }
}
