using UnityEngine;
using Vid2Sim;
namespace Vid2Sim.Generated.Shapes
{
    [Vid2Sim.PartShape("forklift-pre-shift-inspection", "propane_tank")]
    public class Shape_forklift_pre_shift_inspection__propane_tank : Vid2Sim.IPartShape
    {
        public void Build(UnityEngine.GameObject root, float size, UnityEngine.Color accent)
        {
            ShapeKit.Cylinder(root, new Vector3(0f, size * 0.44f, 0f), size * 0.52f, size * 0.44f, accent, new Vector3(0f, 0f, 90f));
            ShapeKit.Sphere(root, new Vector3(-size * 0.22f, size * 0.44f, 0f), size * 0.52f, accent);
            ShapeKit.Sphere(root, new Vector3(size * 0.22f, size * 0.44f, 0f), size * 0.52f, accent);
            ShapeKit.Box(root, new Vector3(0f, size * 0.09f, 0f), new Vector3(size * 0.60f, size * 0.18f, size * 0.40f), ShapeKit.DarkSteel);
            ShapeKit.Cylinder(root, new Vector3(-size * 0.15f, size * 0.44f, 0f), size * 0.56f, size * 0.06f, ShapeKit.DarkSteel, new Vector3(0f, 0f, 90f));
            ShapeKit.Cylinder(root, new Vector3(size * 0.15f, size * 0.44f, 0f), size * 0.56f, size * 0.06f, ShapeKit.DarkSteel, new Vector3(0f, 0f, 90f));
            ShapeKit.Box(root, new Vector3(0f, size * 0.76f, 0f), new Vector3(size * 0.22f, size * 0.12f, size * 0.18f), ShapeKit.Steel);
            ShapeKit.Cylinder(root, new Vector3(0f, size * 0.85f, 0f), size * 0.16f, size * 0.06f, ShapeKit.Red);
            ShapeKit.Box(root, new Vector3(-size * 0.10f, size * 0.44f, -size * 0.27f), new Vector3(size * 0.18f, size * 0.12f, size * 0.04f), ShapeKit.Cream);
        }
    }
}
