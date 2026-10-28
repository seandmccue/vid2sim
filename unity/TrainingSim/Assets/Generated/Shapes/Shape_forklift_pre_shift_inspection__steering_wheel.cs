using UnityEngine;
using Vid2Sim;

namespace Vid2Sim.Generated.Shapes
{
    [Vid2Sim.PartShape("forklift-pre-shift-inspection", "steering_wheel")]
    public class Shape_forklift_pre_shift_inspection__steering_wheel : Vid2Sim.IPartShape
    {
        public void Build(GameObject root, float size, Color accent)
        {
            ShapeKit.Capsule(root, new Vector3(size * 0.42f, size * 0.52f, 0f), size * 0.11f, size * 0.32f, ShapeKit.Black, new Vector3(0f, 0f, 0f));
            ShapeKit.Capsule(root, new Vector3(size * 0.297f, size * 0.817f, 0f), size * 0.11f, size * 0.32f, ShapeKit.Black, new Vector3(0f, 0f, 45f));
            ShapeKit.Capsule(root, new Vector3(0f, size * 0.94f, 0f), size * 0.11f, size * 0.32f, ShapeKit.Black, new Vector3(0f, 0f, 90f));
            ShapeKit.Capsule(root, new Vector3(size * -0.297f, size * 0.817f, 0f), size * 0.11f, size * 0.32f, ShapeKit.Black, new Vector3(0f, 0f, 135f));
            ShapeKit.Capsule(root, new Vector3(size * -0.42f, size * 0.52f, 0f), size * 0.11f, size * 0.32f, ShapeKit.Black, new Vector3(0f, 0f, 0f));
            ShapeKit.Capsule(root, new Vector3(size * -0.297f, size * 0.223f, 0f), size * 0.11f, size * 0.32f, ShapeKit.Black, new Vector3(0f, 0f, 45f));
            ShapeKit.Capsule(root, new Vector3(0f, size * 0.10f, 0f), size * 0.11f, size * 0.32f, ShapeKit.Black, new Vector3(0f, 0f, 90f));
            ShapeKit.Capsule(root, new Vector3(size * 0.297f, size * 0.223f, 0f), size * 0.11f, size * 0.32f, ShapeKit.Black, new Vector3(0f, 0f, 135f));
            ShapeKit.Box(root, new Vector3(size * -0.235f, size * 0.52f, 0f), new Vector3(size * 0.30f, size * 0.08f, size * 0.08f), ShapeKit.DarkSteel);
            ShapeKit.Box(root, new Vector3(size * 0.235f, size * 0.52f, 0f), new Vector3(size * 0.30f, size * 0.08f, size * 0.08f), ShapeKit.DarkSteel);
            ShapeKit.Box(root, new Vector3(0f, size * 0.285f, 0f), new Vector3(size * 0.08f, size * 0.30f, size * 0.08f), ShapeKit.DarkSteel);
            ShapeKit.Cylinder(root, new Vector3(0f, size * 0.52f, 0f), size * 0.22f, size * 0.14f, accent, new Vector3(90f, 0f, 0f));
            ShapeKit.Cylinder(root, new Vector3(0f, size * 0.26f, size * 0.05f), size * 0.12f, size * 0.52f, ShapeKit.Steel);
        }
    }
}
