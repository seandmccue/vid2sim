#if META_INTERACTION_SDK
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Surfaces;
using UnityEngine;

namespace Vid2Sim.Interaction
{
    /// <summary>
    /// Meta XR Interaction SDK wiring, hands-first. press -> PokeInteractable on the part's top face;
    /// turn -> HandGrabInteractable + OneGrabRotateTransformer about the part's up axis; everything else ->
    /// HandGrabInteractable with the default free transformer. Controllers also work via the Grab interactors
    /// on the rig but are never required. Every collider is >= 4 cm so hand tracking lands.
    /// Verified against com.meta.xr.sdk.interaction 207.0.1 (Inject* signatures are version-stable since v59).
    /// </summary>
    public class MetaInteractionBackend : IInteractionBackend
    {
        public string Name => "Meta Interaction SDK";

        public void Attach(PartBehaviour part, GameObject go, string interaction, bool needsRotate)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;

            if (interaction == "press")
            {
                AttachPoke(part, go);
                return;
            }
            AttachGrab(part, go, rb, needsRotate || interaction == "turn");
        }

        void AttachGrab(PartBehaviour part, GameObject go, Rigidbody rb, bool rotateOnly)
        {
            var grabbable = go.AddComponent<Grabbable>();
            grabbable.InjectOptionalRigidbody(rb);
            if (rotateOnly)
            {
                // knob / cap lying flat on the bench: rotate about its own up axis, pivot at its centre
                var rot = go.AddComponent<OneGrabRotateTransformer>();
                rot.InjectOptionalPivotTransform(go.transform);
                rot.InjectOptionalRotationAxis(OneGrabRotateTransformer.Axis.Up);
                grabbable.InjectOptionalOneGrabTransformer(rot);
            }
            var hg = go.AddComponent<HandGrabInteractable>();
            hg.InjectRigidbody(rb);
            hg.InjectOptionalPointableElement(grabbable);
            hg.InjectSupportedGrabTypes(GrabTypeFlags.All);

            grabbable.WhenPointerEventRaised += evt =>
            {
                switch (evt.Type)
                {
                    case PointerEventType.Hover: part.NotifyHover(true); break;
                    case PointerEventType.Unhover: part.NotifyHover(false); break;
                    case PointerEventType.Select: part.NotifySelect(); break;
                    case PointerEventType.Unselect:
                    case PointerEventType.Cancel: part.NotifyUnselect(); break;
                }
            };
        }

        void AttachPoke(PartBehaviour part, GameObject go)
        {
            // Button face is the top (+Y) of the primitive: the bench faces away from the seated user, so a
            // front (+Z) face would point the wrong way. PlaneSurface's normal is its local -Z (NormalFacing.Backward),
            // so aim the surface's +Z down the part's -Y. Sizes are in the part's unit-cube space, scaled by the part.
            var surfaceGo = new GameObject("PokeSurface");
            surfaceGo.transform.SetParent(go.transform, false);
            surfaceGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            surfaceGo.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var plane = surfaceGo.AddComponent<PlaneSurface>();
            var bounds = surfaceGo.AddComponent<BoundsClipper>();
            bounds.Size = new Vector3(1f, 1f, 0.1f);
            var clipped = surfaceGo.AddComponent<ClippedPlaneSurface>();
            clipped.InjectAllClippedPlaneSurface(plane, new List<IBoundsClipper> { bounds });

            var poke = go.AddComponent<PokeInteractable>();
            poke.InjectAllPokeInteractable(clipped);
            poke.WhenStateChanged += args =>
            {
                if (args.NewState == InteractableState.Hover) part.NotifyHover(true);
                else if (args.PreviousState == InteractableState.Hover && args.NewState == InteractableState.Normal) part.NotifyHover(false);
                if (args.NewState == InteractableState.Select) part.NotifySelect();
                else if (args.PreviousState == InteractableState.Select) part.NotifyUnselect();
            };
        }
    }
}
#endif
