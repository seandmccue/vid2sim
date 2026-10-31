using UnityEngine;
using Vid2Sim;

namespace Vid2Sim.Generated.Shapes
{
    [Vid2Sim.PartShape("forklift-pre-shift-inspection", "forklift_battery")]
    public class Shape_forklift_pre_shift_inspection__forklift_battery : IPartShape
    {
        public void Build(GameObject root, float size, Color accent)
        {
            ShapeKit.Box(root, new Vector3(size * 0f, size * 0.30f, size * 0f), new Vector3(size * 1.0f, size * 0.60f, size * 0.65f), ShapeKit.Black);
            ShapeKit.Box(root, new Vector3(size * 0f, size * 0.625f, size * 0f), new Vector3(size * 0.96f, size * 0.05f, size * 0.61f), ShapeKit.DarkSteel);
            ShapeKit.Cylinder(root, new Vector3(size * -0.30f, size * 0.68f, size * 0.08f), size * 0.12f, size * 0.06f, ShapeKit.Steel);
            ShapeKit.Cylinder(root, new Vector3(size * 0f, size * 0.68f, size * 0.08f), size * 0.12f, size * 0.06f, ShapeKit.Steel);
            ShapeKit.Cylinder(root, new Vector3(size * 0.30f, size * 0.68f, size * 0.08f), size * 0.12f, size * 0.06f, ShapeKit.Steel);
            ShapeKit.Cylinder(root, new Vector3(size * -0.32f, size * 0.71f, size * 0.20f), size * 0.14f, size * 0.12f, ShapeKit.Red);
            ShapeKit.Cylinder(root, new Vector3(size * 0.32f, size * 0.71f, size * 0.20f), size * 0.14f, size * 0.12f, ShapeKit.Blue);
            ShapeKit.Box(root, new Vector3(size * 0f, size * 0.32f, size * -0.33f), new Vector3(size * 0.70f, size * 0.28f, size * 0.02f), accent);
        }
    }
}
