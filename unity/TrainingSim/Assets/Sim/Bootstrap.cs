using UnityEngine;

namespace Vid2Sim
{
    /// <summary>
    /// Drop this on one empty GameObject in an otherwise empty scene (with your Meta camera rig and hand
    /// tracking building blocks) and the whole sim assembles itself from spec.json at runtime.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class Bootstrap : MonoBehaviour
    {
        public string procedureId = "";
        public Transform rigOrigin;

        void Awake()
        {
            var loader = gameObject.GetComponent<SpecLoader>() ?? gameObject.AddComponent<SpecLoader>();
            loader.procedureId = procedureId;
            if (!GetComponent<PartFactory>()) gameObject.AddComponent<PartFactory>();
            if (!GetComponent<StepSequencer>()) gameObject.AddComponent<StepSequencer>();
            if (!GetComponent<Scorer>()) gameObject.AddComponent<Scorer>();
            if (!GetComponent<CoachUI>()) gameObject.AddComponent<CoachUI>();
            if (!GetComponent<Speech>()) gameObject.AddComponent<Speech>();
            if (!GetComponent<StepGuide>()) gameObject.AddComponent<StepGuide>();
            var flow = GetComponent<SessionFlow>() ?? gameObject.AddComponent<SessionFlow>();
            if (rigOrigin) flow.origin = rigOrigin;
        }
    }
}
