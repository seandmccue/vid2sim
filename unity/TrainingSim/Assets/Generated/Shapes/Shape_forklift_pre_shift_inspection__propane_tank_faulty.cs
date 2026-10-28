using UnityEngine;
using Vid2Sim;
namespace Vid2Sim.Generated.Shapes
{
    [Vid2Sim.PartShape("forklift-pre-shift-inspection", "propane_tank", "faulty")]
    public class Shape_forklift_pre_shift_inspection__propane_tank_faulty : Vid2Sim.IPartShape
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
            ShapeKit.Cylinder(root, new Vector3(size * 0.25f, size * 0.62f, size * 0.10f), size * 0.07f, size * 0.35f, ShapeKit.Rubber, new Vector3(0f, 0f, 15f));
            ShapeKit.Cylinder(root, new Vector3(size * 0.14f, size * 0.82f, size * 0.06f), size * 0.04f, size * 0.18f, ShapeKit.Rubber, new Vector3(0f, 0f, 65f));
            ShapeKit.Sphere(root, new Vector3(size * 0.06f, size * 0.86f, size * 0.03f), size * 0.10f, ShapeKit.Red);
            ShapeKit.Sphere(root, new Vector3(size * 0.15f, size * 0.86f, size * 0.06f), size * 0.07f, ShapeKit.Red);
        }
    }
}
