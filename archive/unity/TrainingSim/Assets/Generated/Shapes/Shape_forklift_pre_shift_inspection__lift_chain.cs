using UnityEngine;
using Vid2Sim;

namespace Vid2Sim.Generated.Shapes
{
    [Vid2Sim.PartShape("forklift-pre-shift-inspection", "lift_chain")]
    public class Shape_forklift_pre_shift_inspection__lift_chain : Vid2Sim.IPartShape
    {
        public void Build(UnityEngine.GameObject root, float size, UnityEngine.Color accent)
        {
            ShapeKit.Box(root, new Vector3(0f, size * 0.92f, 0f), new Vector3(size * 0.30f, size * 0.12f, size * 0.14f), ShapeKit.DarkSteel);
            ShapeKit.Box(root, new Vector3(0f, size * 0.08f, 0f), new Vector3(size * 0.30f, size * 0.12f, size * 0.14f), ShapeKit.DarkSteel);
            ShapeKit.Box(root, new Vector3(size * -0.08f, size * 0.30f, 0f), new Vector3(size * 0.05f, size * 0.26f, size * 0.10f), accent);
            ShapeKit.Box(root, new Vector3(size * 0.08f, size * 0.30f, 0f), new Vector3(size * 0.05f, size * 0.26f, size * 0.10f), accent);
            ShapeKit.Cylinder(root, new Vector3(0f, size * 0.30f, 0f), size * 0.10f, size * 0.20f, ShapeKit.DarkSteel, new Vector3(0f, 0f, 90f));
            ShapeKit.Box(root, new Vector3(size * -0.08f, size * 0.50f, 0f), new Vector3(size * 0.05f, size * 0.26f, size * 0.10f), accent);
            ShapeKit.Box(root, new Vector3(size * 0.08f, size * 0.50f, 0f), new Vector3(size * 0.05f, size * 0.26f, size * 0.10f), accent);
            ShapeKit.Cylinder(root, new Vector3(0f, size * 0.50f, 0f), size * 0.10f, size * 0.20f, ShapeKit.DarkSteel, new Vector3(0f, 0f, 90f));
            ShapeKit.Box(root, new Vector3(size * -0.08f, size * 0.70f, 0f), new Vector3(size * 0.05f, size * 0.26f, size * 0.10f), accent);
            ShapeKit.Box(root, new Vector3(size * 0.08f, size * 0.70f, 0f), new Vector3(size * 0.05f, size * 0.26f, size * 0.10f), accent);
            ShapeKit.Cylinder(root, new Vector3(0f, size * 0.70f, 0f), size * 0.10f, size * 0.20f, ShapeKit.DarkSteel, new Vector3(0f, 0f, 90f));
            ShapeKit.Box(root, new Vector3(size * 0.14f, size * 0.50f, 0f), new Vector3(size * 0.06f, size * 0.03f, size * 0.03f), ShapeKit.Yellow);
            ShapeKit.Box(root, new Vector3(size * 0.14f, size * 0.70f, 0f), new Vector3(size * 0.06f, size * 0.03f, size * 0.03f), ShapeKit.Yellow);
        }
    }
}
