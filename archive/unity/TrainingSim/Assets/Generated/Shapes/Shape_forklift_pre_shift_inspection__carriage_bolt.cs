using UnityEngine;
using Vid2Sim;
namespace Vid2Sim.Generated.Shapes
{
    [Vid2Sim.PartShape("forklift-pre-shift-inspection", "carriage_bolt")]
    public class Shape_forklift_pre_shift_inspection__carriage_bolt : Vid2Sim.IPartShape
    {
        public void Build(GameObject root, float size, Color accent)
        {
            ShapeKit.Cylinder(root, new Vector3(0f, size * 0.025f, 0f), size * 0.22f, size * 0.05f, ShapeKit.DarkSteel);
            ShapeKit.Cylinder(root, new Vector3(0f, size * 0.21f, 0f), size * 0.24f, size * 0.32f, ShapeKit.Steel);
            ShapeKit.Cylinder(root, new Vector3(0f, size * 0.13f, 0f), size * 0.27f, size * 0.03f, ShapeKit.DarkSteel);
            ShapeKit.Cylinder(root, new Vector3(0f, size * 0.21f, 0f), size * 0.27f, size * 0.03f, ShapeKit.DarkSteel);
            ShapeKit.Cylinder(root, new Vector3(0f, size * 0.29f, 0f), size * 0.27f, size * 0.03f, ShapeKit.DarkSteel);
            ShapeKit.Box(root, new Vector3(0f, size * 0.43f, 0f), new Vector3(size * 0.48f, size * 0.12f, size * 0.26f), ShapeKit.DarkSteel);
            ShapeKit.Box(root, new Vector3(0f, size * 0.43f, 0f), new Vector3(size * 0.48f, size * 0.12f, size * 0.26f), ShapeKit.DarkSteel, new Vector3(0f, 60f, 0f));
            ShapeKit.Box(root, new Vector3(0f, size * 0.43f, 0f), new Vector3(size * 0.48f, size * 0.12f, size * 0.26f), ShapeKit.DarkSteel, new Vector3(0f, 120f, 0f));
            ShapeKit.Box(root, new Vector3(0f, size * 0.54f, 0f), new Vector3(size * 0.22f, size * 0.10f, size * 0.22f), ShapeKit.Steel);
            ShapeKit.Cylinder(root, new Vector3(0f, size * 0.625f, 0f), size * 0.48f, size * 0.07f, accent);
            ShapeKit.Sphere(root, new Vector3(0f, size * 0.68f, 0f), size * 0.48f, accent);
        }
    }
}
