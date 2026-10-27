using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vid2Sim.Interaction;

namespace Vid2Sim
{
    /// <summary>
    /// Spawns one object per part: primitive by interaction, scaled by size_hint_m, textured from the
    /// bundle crop or Muse Image texture, with the interaction backend's components attached. Parts sit on
    /// a bench 0.7 m in front of the seated user, in the order they appear in the video, inside a 60 cm reach box.
    /// </summary>
    public class PartFactory : MonoBehaviour
    {
        [Header("Bench layout (seated)")]
        public float benchDistance = 0.7f;
        public float benchHeight = 0.75f;
        public float reachWidth = 0.6f;
        public float minColliderSize = 0.04f;
        public Material baseMaterial;

        public IInteractionBackend Backend { get; private set; }
        public readonly Dictionary<string, PartBehaviour> Spawned = new();
        Transform _bench;

        void Awake() { Backend = InteractionBackends.Create(); }

        public IEnumerator Spawn(ProcedureSpec spec, SpecLoader loader, Transform origin)
        {
            Clear();
            _bench = new GameObject("Bench").transform;
            _bench.SetParent(transform, false);
            _bench.position = origin.position + origin.forward * benchDistance + Vector3.up * benchHeight;
            _bench.rotation = Quaternion.LookRotation(Flat(origin.forward), Vector3.up);

            var top = GameObject.CreatePrimitive(PrimitiveType.Cube);
            top.name = "BenchTop";
            top.transform.SetParent(_bench, false);
            top.transform.localPosition = new Vector3(0f, -0.03f, 0f);
            top.transform.localScale = new Vector3(reachWidth + 0.2f, 0.04f, 0.4f);
            top.GetComponent<Renderer>().material = NewMaterial(new Color(0.25f, 0.25f, 0.27f));
            Destroy(top.GetComponent<Collider>());

            var order = OrderOfAppearance(spec);
            int n = order.Count;
            for (int i = 0; i < n; i++)
            {
                var part = spec.FindPart(order[i]);
                float x = n == 1 ? 0f : Mathf.Lerp(-reachWidth / 2f, reachWidth / 2f, i / (float)(n - 1));
                var go = CreatePrimitiveFor(part);
                go.name = $"Part:{part.id}";
                go.transform.SetParent(_bench, false);
                float size = Mathf.Max(part.size_hint_m, minColliderSize);
                go.transform.localScale = ScaleFor(part, size);
                go.transform.localPosition = new Vector3(x, go.transform.localScale.y / 2f, 0f);
                go.transform.localRotation = Quaternion.identity;
                EnsureCollider(go, size);

                var pb = go.AddComponent<PartBehaviour>();
                pb.Init(part);
                bool needsRotate = UsesAction(spec, part.id, Actions.TurnCw) || UsesAction(spec, part.id, Actions.TurnCcw);
                Backend.Attach(pb, go, part.interaction, needsRotate);
                if (UsesAction(spec, part.id, Actions.Place)) pb.SnapZone = CreateSnapZone(go, part);

                var rend = go.GetComponent<Renderer>();
                rend.material = NewMaterial(ColorFor(part));
                Texture2D tex = null;
                yield return loader.LoadTexture(part.id, t => tex = t);
                if (tex != null) { rend.material.mainTexture = tex; rend.material.color = Color.white; }

                Spawned[part.id] = pb;
            }
            Debug.Log($"[PartFactory] spawned {Spawned.Count} parts with {Backend.Name}");
        }

        public void Clear()
        {
            Spawned.Clear();
            if (_bench) Destroy(_bench.gameObject);
        }

        public void ResetPoses()
        {
            foreach (var p in Spawned.Values) p.ResetPose();
        }

        static List<string> OrderOfAppearance(ProcedureSpec spec)
        {
            var seen = new List<string>();
            foreach (var s in spec.steps) if (!seen.Contains(s.part_id)) seen.Add(s.part_id);
            foreach (var p in spec.parts) if (!seen.Contains(p.id)) seen.Add(p.id);
            return seen;
        }

        static bool UsesAction(ProcedureSpec spec, string partId, string action)
        {
            foreach (var s in spec.steps) if (s.part_id == partId && s.action == action) return true;
            return false;
        }

        static GameObject CreatePrimitiveFor(Part part) => part.interaction switch
        {
            "press" => GameObject.CreatePrimitive(PrimitiveType.Cube),
            "turn" => GameObject.CreatePrimitive(PrimitiveType.Cylinder),
            "inspect" => GameObject.CreatePrimitive(PrimitiveType.Sphere),
            _ => GameObject.CreatePrimitive(PrimitiveType.Cube),
        };

        static Vector3 ScaleFor(Part part, float size)
        {
            // keep the box's aspect for cubes so a long filter reads as long
            float aspect = 1f;
            if (part.box != null && part.box.Length == 4)
            {
                float w = Mathf.Max(1, part.box[2] - part.box[0] + 1), h = Mathf.Max(1, part.box[3] - part.box[1] + 1);
                aspect = Mathf.Clamp(w / h, 0.4f, 2.5f);
            }
            return part.interaction switch
            {
                "press" => new Vector3(size, size * 0.6f, size * 0.6f),
                "turn" => new Vector3(size, size * 0.3f, size),              // cylinder: knob/cap lying flat
                _ => aspect >= 1f ? new Vector3(size, size / aspect, size * 0.6f) : new Vector3(size * aspect, size, size * 0.6f),
            };
        }

        void EnsureCollider(GameObject go, float size)
        {
            var c = go.GetComponent<Collider>();
            if (c == null) c = go.AddComponent<BoxCollider>();
            // grow tiny parts to the hand-tracking floor without changing their look
            if (c is BoxCollider bc)
            {
                var world = Vector3.Scale(bc.size, go.transform.lossyScale);
                var grow = Vector3.one;
                for (int i = 0; i < 3; i++) if (world[i] < minColliderSize) grow[i] = minColliderSize / Mathf.Max(world[i], 1e-4f);
                bc.size = Vector3.Scale(bc.size, grow);
            }
        }

        Transform CreateSnapZone(GameObject part, Part p)
        {
            var zone = GameObject.CreatePrimitive(PrimitiveType.Cube);
            zone.name = $"SnapZone:{p.id}";
            zone.transform.SetParent(_bench, false);
            zone.transform.localScale = part.transform.localScale * 1.15f;
            zone.transform.localPosition = part.transform.localPosition + new Vector3(0f, 0f, -0.14f);
            Destroy(zone.GetComponent<Collider>());
            zone.GetComponent<Renderer>().material = NewZoneMaterial(new Color(0.2f, 0.8f, 0.4f, 0.35f));
            return zone.transform;
        }

        // Shaders found only by name are stripped from player builds (WebGL and Android alike); the material
        // assets in Assets/Sim/Resources keep the Standard variants in the build. Shader.Find is the editor fallback.
        Material NewMaterial(Color c)
        {
            var src = baseMaterial ? baseMaterial : Resources.Load<Material>("Vid2SimPart");
            var m = src ? new Material(src) : new Material(Shader.Find("Standard"));
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }

        Material NewZoneMaterial(Color c)
        {
            var src = Resources.Load<Material>("Vid2SimZone");
            if (src) { var z = new Material(src); z.color = c; return z; }
            var m = NewMaterial(c);
            m.SetFloat("_Mode", 2f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = 3000;
            return m;
        }

        static Color ColorFor(Part part) => part.interaction switch
        {
            "press" => new Color(0.85f, 0.2f, 0.2f),
            "turn" => new Color(0.8f, 0.65f, 0.2f),
            "inspect" => new Color(0.3f, 0.6f, 0.9f),
            _ => new Color(0.75f, 0.75f, 0.75f),
        };

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 1e-4f ? Vector3.forward : v.normalized; }
    }
}
