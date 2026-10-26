using System.Collections.Generic;

namespace Vid2Sim
{
    /// <summary>Mirror of backend/pipeline/validate.py. A spec that fails never reaches PartFactory.</summary>
    public static class SpecValidator
    {
        public static List<string> Validate(ProcedureSpec s)
        {
            var errors = new List<string>();
            if (s == null) { errors.Add("spec is null"); return errors; }
            if (s.schema_version != "1.0") errors.Add($"schema_version '{s.schema_version}' is not 1.0");
            if (s.procedure == null || string.IsNullOrEmpty(s.procedure.id)) errors.Add("procedure.id missing");
            if (s.parts == null) errors.Add("parts missing");
            if (s.steps == null || s.steps.Length == 0) errors.Add("steps missing");
            if (errors.Count > 0) return errors;

            var ids = new HashSet<string>();
            for (int i = 0; i < s.parts.Length; i++)
            {
                var p = s.parts[i];
                if (string.IsNullOrEmpty(p.id)) errors.Add($"parts[{i}].id missing");
                else if (!ids.Add(p.id)) errors.Add($"parts[{i}].id '{p.id}' duplicated");
                if (p.box == null || p.box.Length != 4) errors.Add($"parts[{i}].box must have 4 ints");
                if (p.size_hint_m <= 0f) errors.Add($"parts[{i}].size_hint_m must be > 0");
                if (p.interaction != "press" && p.interaction != "grab" && p.interaction != "turn" && p.interaction != "inspect")
                    errors.Add($"parts[{i}].interaction '{p.interaction}' unknown");
            }
            int lastOrder = 0;
            for (int i = 0; i < s.steps.Length; i++)
            {
                var st = s.steps[i];
                if (st.order <= lastOrder) errors.Add($"steps[{i}].order must be strictly increasing");
                lastOrder = st.order;
                if (!ids.Contains(st.part_id)) errors.Add($"steps[{i}].part_id '{st.part_id}' unknown");
                if (!Actions.IsKnown(st.action)) errors.Add($"steps[{i}].action '{st.action}' not in engine vocabulary");
                if (st.end_frame < st.start_frame) errors.Add($"steps[{i}].end_frame before start_frame");
            }
            return errors;
        }
    }
}
