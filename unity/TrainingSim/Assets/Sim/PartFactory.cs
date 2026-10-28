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
        public int rowCapacity = 5;
        public float rowSpacing = 0.22f;
        [Tooltip("Photo cards never get wider than this, whatever the size hint says, so a big panel still fits the bench")]
        public float maxCardSize = 0.22f;
        [Header("Host model (parts on the machine)")]
        [Tooltip("Longest horizontal side of a generated host model once scaled onto the bench")]
        public float hostSize = 0.8f;
        public float hostMaxHeight = 0.7f;
        [Tooltip("A part on the host never gets smaller than this, so a running light is still something you can grab")]
        public float hostedMinPart = 0.06f;
        public float hostedMaxPart = 0.4f;
        public Material baseMaterial;
        static Mesh _cardMesh;

        public IInteractionBackend Backend { get; private set; }
        public readonly Dictionary<string, PartBehaviour> Spawned = new();
        /// <summary>Bench controls that are not parts: the two verdict buttons. Never wrong-part touches.</summary>
        public readonly Dictionary<string, PartBehaviour> Controls = new();
        public VerdictPanel Verdicts { get; private set; }
        /// <summary>The generated machine body the parts sit on, or null when the procedure is loose items on a bench.</summary>
        public Transform Host { get; private set; }
        /// <summary>Metres on the bench per metre on the real machine (1 without a host).</summary>
        public float HostScale { get; private set; } = 1f;
        /// <summary>World bounds of everything on the bench, for camera framing.</summary>
        public Bounds StageBounds { get; private set; }
        Transform _bench;

        void Awake() { Backend = InteractionBackends.Create(); }

        public IEnumerator Spawn(ProcedureSpec spec, SpecLoader loader, Transform origin)
        {
            Clear();
            _bench = new GameObject("Bench").transform;
            _bench.SetParent(transform, false);
            _bench.position = origin.position + origin.forward * benchDistance + Vector3.up * benchHeight;
            _bench.rotation = Quaternion.LookRotation(Flat(origin.forward), Vector3.up);

            var order = OrderOfAppearance(spec);
            // the machine the parts belong to, when generated code built one: scaled onto the bench, one anchor per
            // part that lives on it; everything else is a loose item in a row at the front
            var anchors = new Dictionary<string, HostAnchor>();
            float hostWidth = 0f, hostDepth = 0f;
            var hostShape = ShapeKit.FindHost(spec.procedure.id);
            if (hostShape != null) BuildHost(hostShape, anchors, out hostWidth, out hostDepth);
            var loose = new List<string>();
            foreach (var id in order) if (!anchors.ContainsKey(id)) loose.Add(id);
            float hostFrontZ = loose.Count > 0 ? 0.18f : 0f;
            if (Host) Host.localPosition = new Vector3(0f, 0f, hostFrontZ + hostDepth / 2f);

            int looseRows = Mathf.CeilToInt(loose.Count / (float)rowCapacity);
            float depth = Host ? 0.2f + hostFrontZ + hostDepth + 0.1f : 0.36f + Mathf.Max(0, looseRows - 1) * rowSpacing;
            float width = Mathf.Max(reachWidth, hostWidth) + 0.2f;
            var top = GameObject.CreatePrimitive(PrimitiveType.Cube);
            top.name = "BenchTop";
            top.transform.SetParent(_bench, false);
            top.transform.localScale = new Vector3(width, 0.04f, depth);
            top.transform.localPosition = new Vector3(0f, -0.03f, (depth - 0.4f) / 2f);   // front edge stays at z = -0.2
            top.GetComponent<Renderer>().material = NewMaterial(new Color(0.93f, 0.90f, 0.84f));   // pale wood
            Destroy(top.GetComponent<Collider>());
            // four legs down to the floor, so the bench reads as a table in a room rather than a floating slab
            float legDrop = benchHeight - 0.05f;
            foreach (var (lx, lz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
            {
                var leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                leg.name = "BenchLeg";
                leg.transform.SetParent(_bench, false);
                leg.transform.localScale = new Vector3(0.04f, legDrop, 0.04f);
                leg.transform.localPosition = new Vector3(lx * (width / 2f - 0.04f), -0.05f - legDrop / 2f, (depth - 0.4f) / 2f + lz * (depth / 2f - 0.04f));
                leg.GetComponent<Renderer>().material = NewMaterial(new Color(0.55f, 0.52f, 0.48f));
                Destroy(leg.GetComponent<Collider>());
            }

            // loose items: up to rowCapacity per row; further rows sit behind, where the 30-degree camera still sees them
            int n = loose.Count;
            int rows = Mathf.CeilToInt(n / (float)rowCapacity), perRow = Mathf.CeilToInt(n / (float)Mathf.Max(1, rows));
            for (int i = 0; i < order.Count; i++)
            {
                var part = spec.FindPart(order[i]);
                anchors.TryGetValue(part.id, out var anchor);
                float size, cap, x = 0f, z = 0f;
                if (anchor != null)
                {
                    // real size on the machine, scaled with it; clamped so it stays something a hand can take
                    size = cap = Mathf.Clamp((anchor.size > 0f ? anchor.size : part.size_hint_m) * HostScale, hostedMinPart, hostedMaxPart);
                }
                else
                {
                    int li = loose.IndexOf(part.id);
                    int row = li / perRow, col = li % perRow, inRow = Mathf.Min(perRow, n - row * perRow);
                    x = inRow == 1 ? 0f : Mathf.Lerp(-reachWidth / 2f, reachWidth / 2f, col / (float)(inRow - 1));
                    z = row * rowSpacing;
                    size = Mathf.Max(part.size_hint_m, minColliderSize);
                    // crowded benches get smaller parts so neighbours don't overlap
                    cap = Mathf.Min(maxCardSize, inRow > 1 ? reachWidth / (inRow - 1) * 0.8f : maxCardSize);
                }
                bool needsRotate = UsesAction(spec, part.id, Actions.TurnCw) || UsesAction(spec, part.id, Actions.TurnCcw);

                Texture2D cutout = null;
                var shape = ShapeKit.Find(spec.procedure.id, part.id);
                if (shape == null) yield return loader.LoadCutout(part.id, t => cutout = t);
                GameObject go;
                float height;
                Vector3 turnAxis = Vector3.up;
                if (shape != null)
                {
                    go = CreateShaped(shape, part, Mathf.Min(size, cap), out var modelSize);
                    height = modelSize.y;
                    // a wheel or a dial stands facing the trainee and turns about its face normal; a cap lying flat turns about up
                    if (needsRotate && modelSize.z < modelSize.y * 0.6f) turnAxis = Vector3.back;
                    var faulty = ShapeKit.Find(spec.procedure.id, part.id, "faulty");
                    if (faulty != null) AddFaultyModel(go, faulty, part, Mathf.Min(size, cap));
                }
                else if (cutout != null)
                {
                    go = CreateCard(cutout, Mathf.Min(size, cap));
                    height = go.transform.localScale.y;
                    turnAxis = Vector3.back;   // a card turns about its normal, like the lid in the video
                }
                else
                {
                    go = CreatePrimitiveFor(part);
                    go.transform.localScale = ScaleFor(part, size);
                    height = go.transform.localScale.y;
                }
                go.name = $"Part:{part.id}";
                go.transform.SetParent(_bench, false);
                Vector3 outward;
                if (anchor != null)
                {
                    // the part's front (-Z) faces out of the machine's surface; for a pedal seen from above, its top points away from the seat
                    outward = anchor.transform.forward;
                    var upHint = Mathf.Abs(Vector3.Dot(outward, Vector3.up)) > 0.9f ? Host.forward : Vector3.up;
                    go.transform.SetPositionAndRotation(anchor.transform.position, Quaternion.LookRotation(-outward, upHint));
                }
                else
                {
                    outward = -_bench.forward;
                    go.transform.localPosition = new Vector3(x, height / 2f, z);
                    go.transform.localRotation = Quaternion.identity;
                }
                EnsureCollider(go, size);

                var pb = go.AddComponent<PartBehaviour>();
                pb.IsCard = shape == null && cutout != null;
                pb.TurnAxisLocal = turnAxis;
                pb.Outward = outward;
                pb.Init(part);
                PartLabel.Create(pb, height + 0.045f).hideWhenIdle = anchor != null;   // a dozen names on one machine is noise
                Backend.Attach(pb, go, part.interaction, needsRotate);

                var rend = go.GetComponent<Renderer>();
                if (shape != null)
                {
                    // the generated model carries its own materials
                }
                else if (cutout != null)
                {
                    rend.material = NewCutoutMaterial(cutout);
                }
                else
                {
                    rend.material = NewMaterial(ColorFor(part));
                    Texture2D tex = null;
                    yield return loader.LoadTexture(part.id, t => tex = t);
                    if (tex != null) { rend.material.mainTexture = tex; rend.material.color = Color.white; }
                }
                if (UsesAction(spec, part.id, Actions.Place)) pb.SnapZone = CreateSnapZone(go, part);

                Spawned[part.id] = pb;
            }
            Verdicts = VerdictPanel.Create(this, _bench, spec, reachWidth);
            StageBounds = ComputeStageBounds();
            Debug.Log($"[PartFactory] spawned {Spawned.Count} parts with {Backend.Name}" + (Host ? $", {anchors.Count} on the host model, {loose.Count} loose" : ""));
        }

        /// <summary>
        /// Build the generated machine body at real scale, then scale it onto the bench (longest horizontal side =
        /// hostSize, no taller than hostMaxHeight) with its bottom centre at the root. Collects its anchors.
        /// </summary>
        void BuildHost(IHostShape host, Dictionary<string, HostAnchor> anchors, out float width, out float depth)
        {
            width = depth = 0f;
            var root = new GameObject("Host").transform;
            root.SetParent(_bench, false);
            var body = new GameObject("Model");
            body.transform.SetParent(root, false);
            try { host.Build(body); }
            catch (System.Exception e) { Debug.LogWarning($"[PartFactory] host model threw: {e.Message}"); }
            var b = ShapeKit.LocalBounds(body);
            if (b.size.sqrMagnitude < 1e-6f) { Destroy(root.gameObject); return; }
            foreach (var c in body.GetComponentsInChildren<Collider>()) Destroy(c);   // the body is scenery, never a wrong-part touch
            float k = Mathf.Min(hostSize / Mathf.Max(b.size.x, b.size.z, 1e-3f), hostMaxHeight / Mathf.Max(b.size.y, 1e-3f));
            root.localScale = Vector3.one * k;
            body.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);   // bottom centre at the root
            Host = root; HostScale = k;
            width = b.size.x * k; depth = b.size.z * k;
            foreach (var a in body.GetComponentsInChildren<HostAnchor>())
                if (!string.IsNullOrEmpty(a.partId)) anchors[a.partId] = a;
        }

        Bounds ComputeStageBounds()
        {
            var b = new Bounds(_bench.position, Vector3.one * 0.3f);
            foreach (var r in _bench.GetComponentsInChildren<Renderer>())
                if (r.gameObject.name != "BenchLeg" && r.gameObject.name != "BenchTop") b.Encapsulate(r.bounds);   // the things on the bench, not the table
            return b;
        }

        public void Clear()
        {
            Spawned.Clear();
            Controls.Clear();
            Verdicts = null;
            Host = null; HostScale = 1f;
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

        /// <summary>
        /// A stylised model built by generated code (Muse Code, compile-gated): primitives under an empty root,
        /// re-centred so the model sits on the bench, with a box collider around everything it built.
        /// </summary>
        static GameObject CreateShaped(IPartShape shape, Part part, float size, out Vector3 modelSize)
        {
            var root = new GameObject("Shape");
            var body = new GameObject("Model");
            body.transform.SetParent(root.transform, false);
            try { shape.Build(body, size, ColorFor(part)); }
            catch (System.Exception e) { Debug.LogWarning($"[PartFactory] shape for {part.id} threw: {e.Message}"); }
            var b = ShapeKit.LocalBounds(body);
            if (b.size.sqrMagnitude < 1e-6f)
            {
                // nothing built: a plain block so the part still exists
                ShapeKit.Box(body, Vector3.zero, Vector3.one * size, ColorFor(part));
                b = ShapeKit.LocalBounds(body);
            }
            // keep the model inside the size budget whatever the generator did
            float longest = Mathf.Max(b.size.x, b.size.y, b.size.z);
            if (longest > size * 1.05f) { body.transform.localScale = Vector3.one * (size / longest); b = ShapeKit.LocalBounds(body); }
            body.transform.localPosition = -b.center;   // centred on the root
            var bc = root.AddComponent<BoxCollider>();
            bc.size = b.size;
            modelSize = b.size;
            return root;
        }

        /// <summary>The same part with its defect visible, built by the generated "faulty" variant; hidden until a fault is injected.</summary>
        static void AddFaultyModel(GameObject root, IPartShape faulty, Part part, float size)
        {
            var body = new GameObject("Model:faulty");
            body.transform.SetParent(root.transform, false);
            try { faulty.Build(body, size, ColorFor(part)); }
            catch (System.Exception e) { Debug.LogWarning($"[PartFactory] faulty shape for {part.id} threw: {e.Message}"); }
            var b = ShapeKit.LocalBounds(body);
            if (b.size.sqrMagnitude < 1e-6f) { Destroy(body); return; }
            float longest = Mathf.Max(b.size.x, b.size.y, b.size.z);
            if (longest > size * 1.05f) { body.transform.localScale = Vector3.one * (size / longest); b = ShapeKit.LocalBounds(body); }
            var good = root.transform.Find("Model");
            // same footprint as the good model: bottom on the bench, centred like it
            body.transform.localPosition = good ? new Vector3(-b.center.x, good.localPosition.y + (ShapeKit.LocalBounds(good.gameObject).min.y - b.min.y), -b.center.z) : -b.center;
            body.SetActive(false);
        }

        /// <summary>
        /// A photo card: the part cut from the video (or its Muse Image product shot) on a flat, double-sided
        /// quad standing on the bench and facing the trainee, longest side = size. A thin box collider makes
        /// it grabbable; EnsureCollider grows the depth to the hand-tracking floor.
        /// </summary>
        static GameObject CreateCard(Texture2D cutout, float size)
        {
            var go = new GameObject("Card");
            go.AddComponent<MeshFilter>().sharedMesh = CardMesh();
            go.AddComponent<MeshRenderer>();
            float aspect = cutout.width / (float)Mathf.Max(1, cutout.height);
            go.transform.localScale = aspect >= 1f ? new Vector3(size, size / aspect, 1f) : new Vector3(size * aspect, size, 1f);
            var bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(1f, 1f, 0.02f);
            return go;
        }

        /// <summary>Unit quad in XY, front face toward -Z, with a back face so it is visible from behind.</summary>
        static Mesh CardMesh()
        {
            if (_cardMesh != null) return _cardMesh;
            var m = new Mesh { name = "Vid2SimCard" };
            var v = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            var uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.vertices = new[] { v[0], v[1], v[2], v[3], v[0], v[1], v[2], v[3] };
            m.uv = new[] { uv[0], uv[1], uv[2], uv[3], uv[0], uv[1], uv[2], uv[3] };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back, Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            m.triangles = new[] { 0, 3, 2, 0, 2, 1, 4, 6, 7, 4, 5, 6 };
            m.RecalculateBounds();
            _cardMesh = m;
            return m;
        }

        Material NewCutoutMaterial(Texture2D cutout)
        {
            var src = Resources.Load<Material>("Vid2SimCutout");
            var m = src ? new Material(src) : new Material(Shader.Find("Standard"));
            if (!src)
            {
                m.SetFloat("_Mode", 1f); m.SetFloat("_Cutoff", 0.4f); m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450;
            }
            m.mainTexture = cutout;
            m.color = Color.white;
            return m;
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
            GameObject zone;
            var pb = part.GetComponent<PartBehaviour>();
            var pr = part.GetComponent<Renderer>();
            if (pr == null)
            {
                // generated model: a green translucent copy of it where it must go
                zone = PartVisuals.CloneTranslucent(part, $"SnapZone:{p.id}", new Color(0.2f, 0.8f, 0.4f, 0.45f));
                zone.transform.SetParent(_bench, false);
                zone.transform.localScale = part.transform.localScale;
                zone.transform.SetPositionAndRotation(part.transform.position + pb.Outward * 0.14f, part.transform.rotation);
                return zone.transform;
            }
            if (pb != null && pb.IsCard)
            {
                // a green silhouette of the card itself where it has to go: "put the lid here"
                zone = new GameObject();
                zone.AddComponent<MeshFilter>().sharedMesh = CardMesh();
                zone.AddComponent<MeshRenderer>();
                zone.transform.localScale = part.transform.localScale;
            }
            else
            {
                zone = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var bc = part.GetComponent<BoxCollider>();
                zone.transform.localScale = Vector3.Scale(part.transform.localScale, bc ? bc.size : Vector3.one) * 1.15f;
                Destroy(zone.GetComponent<Collider>());
            }
            zone.name = $"SnapZone:{p.id}";
            zone.transform.SetParent(_bench, false);
            zone.transform.SetPositionAndRotation(part.transform.position + pb.Outward * 0.14f, part.transform.rotation);
            var zm = NewZoneMaterial(new Color(0.2f, 0.8f, 0.4f, 0.45f));
            if (pb != null && pb.IsCard && pr != null && pr.material.mainTexture) zm.mainTexture = pr.material.mainTexture;
            zone.GetComponent<Renderer>().material = zm;
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
            "press" => new Color(0.93f, 0.42f, 0.36f),   // coral
            "turn" => new Color(0.96f, 0.72f, 0.30f),    // amber
            "inspect" => new Color(0.45f, 0.66f, 0.95f), // sky
            _ => new Color(0.62f, 0.66f, 0.74f),         // slate
        };

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude < 1e-4f ? Vector3.forward : v.normalized; }
    }
}
