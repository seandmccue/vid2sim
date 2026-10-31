using System;

namespace Vid2Sim
{
    /// <summary>Raised by an action detector when the trainee completes an action on a part.</summary>
    public readonly struct StepEvent
    {
        public readonly string PartId;
        public readonly string Action;
        public StepEvent(string partId, string action) { PartId = partId; Action = action; }
        public override string ToString() => $"{Action}({PartId})";
    }

    /// <summary>The contract Muse Code-generated scripts implement for verbs outside the vocabulary.</summary>
    public interface IStepAction
    {
        void Bind(Part part, Step step, Action<StepEvent> emit);
    }
}
