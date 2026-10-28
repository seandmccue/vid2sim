using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vid2Sim
{
    /// <summary>
    /// A stylised low-poly model of one part, assembled from primitives by generated code
    /// (Assets/Generated/Shapes, written by Muse Code from the part's name, function and size,
    /// compile-gated by the pipeline). Build() adds children under root; the factory sizes the
    /// collider from what was built.
    /// </summary>
    public interface IPartShape
    {
        /// <param name="root">Empty GameObject at the part's spawn pose; add primitives as children.</param>
        /// <param name="size">Longest side of the part in metres (size_hint_m). Keep the model inside it.</param>
        /// <param name="accent">Palette colour for the part's interaction type; use it for the main body or a highlight.</param>
        void Build(GameObject root, float size, Color accent);
    }

    /// <summary>Marks a generated shape with the procedure and part it models.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class PartShapeAttribute : Attribute
    {
        public readonly string ProcedureId, PartId;
        public PartShapeAttribute(string procedureId, string partId) { ProcedureId = procedureId; PartId = partId; }
    }

    /// <summary>
    /// What generated shapes call: primitives with the project's lit material (so nothing is stripped from
    /// player builds), no colliders, a small pastel palette. Positions and sizes are in metres, local to root.
    /// </summary>
    public static class ShapeKit
    {
        public static readonly Color Steel = new(0.72f, 0.75f, 0.80f);
        public static readonly Color DarkSteel = new(0.35f, 0.38f, 0.44f);
        public static readonly Color Rubber = new(0.18f, 0.19f, 0.22f);
        public static readonly Color Yellow = new(0.98f, 0.78f, 0.22f);
        public static readonly Color Red = new(0.90f, 0.32f, 0.28f);
        public static readonly Color Blue = new(0.36f, 0.58f, 0.92f);
        public static readonly Color Green = new(0.36f, 0.72f, 0.48f);
        public static readonly Color Cream = new(0.94f, 0.92f, 0.86f);
        public static readonly Color Black = new(0.12f, 0.12f, 0.14f);

        public static GameObject Box(GameObject root, Vector3 centre, Vector3 size, Color color, Vector3? euler = null)
            => Add(root, PrimitiveType.Cube, centre, size, color, euler);

        /// <summary>Cylinder with its axis along local Y; height is the full length, diameter the width.</summary>
        public static GameObject Cylinder(GameObject root, Vector3 centre, float diameter, float height, Color color, Vector3? euler = null)
            => Add(root, PrimitiveType.Cylinder, centre, new Vector3(diameter, height / 2f, diameter), color, euler);

        public static GameObject Sphere(GameObject root, Vector3 centre, float diameter, Color color)
            => Add(root, PrimitiveType.Sphere, centre, Vector3.one * diameter, color, null);

        /// <summary>Capsule along local Y; height is the full length including the rounded ends.</summary>
        public static GameObject Capsule(GameObject root, Vector3 centre, float diameter, float height, Color color, Vector3? euler = null)
            => Add(root, PrimitiveType.Capsule, centre, new Vector3(diameter, Mathf.Max(height, diameter) / 2f, diameter), color, euler);

        static GameObject Add(GameObject root, PrimitiveType type, Vector3 centre, Vector3 scale, Color color, Vector3? euler)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = type.ToString();
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = centre;
            go.transform.localScale = scale;
            go.transform.localRotation = euler.HasValue ? Quaternion.Euler(euler.Value) : Quaternion.identity;
            var c = go.GetComponent<Collider>();
            if (c) UnityEngine.Object.Destroy(c);
            go.GetComponent<Renderer>().material = Material(color);
            return go;
        }

        public static Material Material(Color color)
        {
            var src = Resources.Load<Material>("Vid2SimPart");
            var m = src ? new Material(src) : new Material(Shader.Find("Standard"));
            m.color = color;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.25f);
            return m;
        }

        /// <summary>Generated shape for a part, if one compiled into this build.</summary>
        public static IPartShape Find(string procedureId, string partId)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
                foreach (var t in types)
                {
                    if (t == null || !typeof(IPartShape).IsAssignableFrom(t) || t.IsInterface || t.IsAbstract) continue;
                    var attr = (PartShapeAttribute)Attribute.GetCustomAttribute(t, typeof(PartShapeAttribute));
                    if (attr == null || attr.PartId != partId) continue;
                    if (attr.ProcedureId == procedureId || attr.ProcedureId == "*")
                        return (IPartShape)Activator.CreateInstance(t);
                }
            }
            return null;
        }

        /// <summary>Local-space bounds of everything built under root (root's own scale is 1).</summary>
        public static Bounds LocalBounds(GameObject root)
        {
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                var wb = r.bounds;
                var min = root.transform.InverseTransformPoint(wb.min);
                var max = root.transform.InverseTransformPoint(wb.max);
                var lb = new Bounds((min + max) / 2f, Vector3.Max(max - min, (min - max)));
                if (!any) { b = lb; any = true; } else b.Encapsulate(lb);
            }
            return b;
        }
    }

    /// <summary>Translucent copies of a part for the demo ghost and the snap zone, whatever kind of part it is.</summary>
    public static class PartVisuals
    {
        public static GameObject CloneTranslucent(GameObject source, string name, Color tint)
        {
            var g = UnityEngine.Object.Instantiate(source);
            g.name = name;
            foreach (var mb in g.GetComponentsInChildren<MonoBehaviour>()) UnityEngine.Object.DestroyImmediate(mb);
            foreach (var c in g.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(c);
            foreach (var rb in g.GetComponentsInChildren<Rigidbody>()) UnityEngine.Object.DestroyImmediate(rb);
            var zone = Resources.Load<Material>("Vid2SimZone");
            foreach (var r in g.GetComponentsInChildren<Renderer>())
            {
                var m = zone ? new Material(zone) : new Material(Shader.Find("Standard"));
                m.color = tint;
                var tex = r.sharedMaterial ? r.sharedMaterial.mainTexture : null;
                if (tex) m.mainTexture = tex;   // a photo card keeps its picture
                r.material = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return g;
        }
    }
}
