using UnityEngine;
using Vid2Sim;
namespace Vid2Sim.Generated.Shapes
{
    [Vid2Sim.PartShape("forklift-pre-shift-inspection", "running_light")]
    public class Shape_forklift_pre_shift_inspection__running_light : Vid2Sim.IPartShape
    {
        public void Build(GameObject root, float size, Color accent)
        {
            ShapeKit.Box(root, new Vector3(size * 0f, size * 0.06f, size * 0.05f), new Vector3(size * 0.6f, size * 0.12f, size * 0.24f), ShapeKit.Steel);
            ShapeKit.Box(root, new Vector3(size * 0f, size * 0.38f, size * 0.06f), new Vector3(size * 1f, size * 0.52f, size * 0.3f), ShapeKit.DarkSteel);
            ShapeKit.Box(root, new Vector3(size * 0f, size * 0.68f, size * -0.02f), new Vector3(size * 0.98f, size * 0.08f, size * 0.38f), ShapeKit.DarkSteel);
            ShapeKit.Box(root, new Vector3(size * 0f, size * 0.38f, size * -0.1f), new Vector3(size * 0.88f, size * 0.36f, size * 0.08f), accent);
            ShapeKit.Box(root, new Vector3(size * -0.22f, size * 0.38f, size * -0.145f), new Vector3(size * 0.18f, size * 0.2f, size * 0.03f), ShapeKit.Cream);
            ShapeKit.Box(root, new Vector3(size * 0.22f, size * 0.38f, size * -0.145f), new Vector3(size * 0.18f, size * 0.2f, size * 0.03f), ShapeKit.Cream);
            ShapeKit.Cylinder(root, new Vector3(size * -0.22f, size * 0.14f, size * 0.05f), size * 0.08f, size * 0.06f, ShapeKit.Black);
            ShapeKit.Cylinder(root, new Vector3(size * 0.22f, size * 0.14f, size * 0.05f), size * 0.08f, size * 0.06f, ShapeKit.Black);
            ShapeKit.Cylinder(root, new Vector3(size * 0.3f, size * 0.74f, size * 0.08f), size * 0.1f, size * 0.08f, ShapeKit.Rubber);
        }
    }
}
