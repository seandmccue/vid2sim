using UnityEngine;
using Vid2Sim;
namespace Vid2Sim.Generated.Shapes
{
    [Vid2Sim.PartShape("forklift-pre-shift-inspection", "drive_tire")]
    public class Shape_forklift_pre_shift_inspection__drive_tire : Vid2Sim.IPartShape
    {
        public void Build(GameObject root, float size, Color accent)
        {
            ShapeKit.Cylinder(root, new Vector3(size * 0f, size * 0.18f, size * 0f), size * 0.82f, size * 0.36f, ShapeKit.Rubber);
            ShapeKit.Cylinder(root, new Vector3(size * 0f, size * 0.19f, size * 0f), size * 0.48f, size * 0.38f, ShapeKit.Steel);
            ShapeKit.Cylinder(root, new Vector3(size * 0f, size * 0.21f, size * 0f), size * 0.18f, size * 0.42f, ShapeKit.DarkSteel);
            ShapeKit.Box(root, new Vector3(size * 0.14f, size * 0.42f, size * -0.12f), new Vector3(size * 0.07f, size * 0.10f, size * 0.07f), accent);
            ShapeKit.Box(root, new Vector3(size * 0.38f, size * 0.18f, size * 0f), new Vector3(size * 0.12f, size * 0.32f, size * 0.18f), ShapeKit.Black, new Vector3(0f, 0f, 0f));
            ShapeKit.Box(root, new Vector3(size * 0.19f, size * 0.18f, size * 0.329f), new Vector3(size * 0.12f, size * 0.32f, size * 0.18f), ShapeKit.Black, new Vector3(0f, -60f, 0f));
            ShapeKit.Box(root, new Vector3(size * -0.19f, size * 0.18f, size * 0.329f), new Vector3(size * 0.12f, size * 0.32f, size * 0.18f), ShapeKit.Black, new Vector3(0f, -120f, 0f));
            ShapeKit.Box(root, new Vector3(size * -0.38f, size * 0.18f, size * 0f), new Vector3(size * 0.12f, size * 0.32f, size * 0.18f), ShapeKit.Black, new Vector3(0f, -180f, 0f));
            ShapeKit.Box(root, new Vector3(size * -0.19f, size * 0.18f, size * -0.329f), new Vector3(size * 0.12f, size * 0.32f, size * 0.18f), ShapeKit.Black, new Vector3(0f, 120f, 0f));
            ShapeKit.Box(root, new Vector3(size * 0.19f, size * 0.18f, size * -0.329f), new Vector3(size * 0.12f, size * 0.32f, size * 0.18f), ShapeKit.Black, new Vector3(0f, 60f, 0f));
        }
    }
}
