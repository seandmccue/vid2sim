// Data model mirroring backend/schema/procedure.schema.json 1.0. Field names are the wire names;
// JsonUtility maps them 1:1. Do not rename without versioning the schema.
using System;

namespace Vid2Sim
{
    public static class Actions
    {
        public const string Press = "press", Grab = "grab", Pull = "pull", Lift = "lift", Place = "place",
            TurnCw = "turn_cw", TurnCcw = "turn_ccw", Inspect = "inspect";
        public static readonly string[] Vocabulary = { Press, Grab, Pull, Lift, Place, TurnCw, TurnCcw, Inspect };
        public static bool IsKnown(string a) => Array.IndexOf(Vocabulary, a) >= 0;
    }

    [Serializable]
    public class ProcedureInfo
    {
        public string id;
        public string title;
        public string source_video;
        public float fps;
        public int width;
        public int height;
        public float duration_s;
    }

    [Serializable]
    public class Part
    {
        public string id;
        public string name;
        public string prompt_phrase;
        public string interaction;   // press | grab | turn | inspect
        public int keyframe;
        public int[] box;            // x1,y1,x2,y2 inclusive, source pixels
        public float size_hint_m;
        public float confidence;
        public string source;        // concept | box | manual
    }

    [Serializable]
    public class Step
    {
        public int order;
        public string part_id;
        public string action;
        public string custom_action; // optional; empty when absent
        public string instruction;
        public int start_frame;
        public int end_frame;
        public string check;
        public string failure;
        public bool safety;
        public string coach;

        public bool HasCustomAction => !string.IsNullOrEmpty(custom_action);
    }

    [Serializable]
    public class ProcedureSpec
    {
        public string schema_version;
        public ProcedureInfo procedure;
        public Part[] parts;
        public Step[] steps;

        public Part FindPart(string id)
        {
            if (parts == null) return null;
            foreach (var p in parts) if (p.id == id) return p;
            return null;
        }
    }

    [Serializable]
    public class ProcedureIndex
    {
        public string[] procedures;
    }
}
