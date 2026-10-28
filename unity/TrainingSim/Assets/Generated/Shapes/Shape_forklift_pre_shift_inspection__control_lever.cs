using UnityEngine;
using Vid2Sim;

namespace Vid2Sim.Generated.Shapes
{
[Vid2Sim.PartShape("forklift-pre-shift-inspection", "control_lever")]
public class Shape_forklift_pre_shift_inspection__control_lever : IPartShape
{
public void Build(GameObject root, float size, Color accent)
{
ShapeKit.Box(root, new Vector3(0f, size * 0.035f, 0f), new Vector3(size * 0.44f, size * 0.07f, size * 0.36f), ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(0f, size * 0.13f, size * 0.02f), new Vector3(size * 0.28f, size * 0.12f, size * 0.22f), ShapeKit.Steel);
ShapeKit.Cylinder(root, new Vector3(0f, size * 0.25f, size * 0.02f), size * 0.22f, size * 0.12f, ShapeKit.Rubber);
ShapeKit.Cylinder(root, new Vector3(0f, size * 0.35f, size * 0.02f), size * 0.14f, size * 0.12f, ShapeKit.Rubber);
ShapeKit.Cylinder(root, new Vector3(0f, size * 0.60f, size * 0.02f), size * 0.06f, size * 0.45f, ShapeKit.DarkSteel);
ShapeKit.Capsule(root, new Vector3(0f, size * 0.86f, size * 0.02f), size * 0.16f, size * 0.22f, accent);
ShapeKit.Sphere(root, new Vector3(0f, size * 0.965f, size * 0.02f), size * 0.06f, ShapeKit.Red);
ShapeKit.Sphere(root, new Vector3(size * -0.16f, size * 0.13f, size * 0.02f), size * 0.08f, ShapeKit.Steel);
ShapeKit.Sphere(root, new Vector3(size * 0.16f, size * 0.13f, size * 0.02f), size * 0.08f, ShapeKit.Steel);
}
}
}
