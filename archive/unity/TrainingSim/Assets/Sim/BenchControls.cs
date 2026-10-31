using UnityEngine;

namespace Vid2Sim
{
    /// <summary>
    /// Pressable blocks on the bench that are not parts (verdict buttons, the exam switch): press parts through
    /// the interaction backend, registered in PartFactory.Controls so they never count as wrong-part touches.
    /// </summary>
    public static class BenchControls
    {
        public static readonly Vector3 Size = new(0.09f, 0.025f, 0.06f);

        public static PartBehaviour Button(PartFactory factory, Transform bench, string id, string label, Color color, Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Control:{id}";
            go.transform.SetParent(bench, false);
            go.transform.localScale = Size;
            go.transform.localPosition = pos + Vector3.up * Size.y / 2f;
            go.GetComponent<Renderer>().material = ShapeKit.Material(color);
            var pb = go.AddComponent<PartBehaviour>();
            pb.Init(new Part { id = id, name = label, prompt_phrase = label, interaction = "press", size_hint_m = Size.x });
            factory.Backend.Attach(pb, go, "press", false);
            factory.Controls[id] = pb;
            var lbl = PartLabel.Create(pb, 0.05f);
            lbl.name = $"Label:{id}";
            return pb;
        }

        public static void Recolor(PartBehaviour button, Color color)
        {
            if (button) button.GetComponent<Renderer>().material.color = color;
        }

        public static void Relabel(PartBehaviour button, string text)
        {
            if (!button) return;
            var label = button.transform.parent ? button.transform.parent.Find($"Label:{button.Part.id}") : null;
            var pl = label ? label.GetComponent<PartLabel>() : null;
            if (pl && pl.Text) pl.Text.text = text;
        }
    }
}
