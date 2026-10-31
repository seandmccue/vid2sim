using UnityEngine;
using Vid2Sim;
namespace Vid2Sim.Generated.Shapes
{
[Vid2Sim.PartShape("forklift-pre-shift-inspection", "hydraulic_hose", "faulty")]
public class Shape_forklift_pre_shift_inspection__hydraulic_hose_faulty : Vid2Sim.IPartShape
{
public void Build(GameObject root, float size, Color accent)
{
ShapeKit.Cylinder(root, new Vector3(size * 0f, size * 0.07f, size * 0f), size * 0.20f, size * 0.14f, ShapeKit.DarkSteel);
ShapeKit.Cylinder(root, new Vector3(size * 0f, size * 0.17f, size * 0f), size * 0.24f, size * 0.08f, ShapeKit.Steel);
ShapeKit.Cylinder(root, new Vector3(size * 0f, size * 0.51f, size * 0f), size * 0.14f, size * 0.60f, ShapeKit.Rubber);
ShapeKit.Cylinder(root, new Vector3(size * 0f, size * 0.67f, size * 0f), size * 0.165f, size * 0.05f, ShapeKit.Steel);
ShapeKit.Cylinder(root, new Vector3(size * 0f, size * 0.84f, size * 0f), size * 0.18f, size * 0.10f, ShapeKit.DarkSteel);
ShapeKit.Cylinder(root, new Vector3(size * 0f, size * 0.935f, size * 0f), size * 0.14f, size * 0.09f, ShapeKit.Steel);
ShapeKit.Sphere(root, new Vector3(size * 0f, size * 0.55f, size * -0.065f), size * 0.06f, ShapeKit.Red);
ShapeKit.Sphere(root, new Vector3(size * 0.035f, size * 0.45f, size * -0.06f), size * 0.035f, ShapeKit.Blue);
ShapeKit.Box(root, new Vector3(size * 0.22f, size * 0.55f, size * -0.14f), new Vector3(size * 0.18f, size * 0.24f, size * 0.015f), accent, new Vector3(0f, 0f, 10f));
ShapeKit.Cylinder(root, new Vector3(size * 0f, size * 0.32f, size * 0f), size * 0.18f, size * 0.08f, ShapeKit.Rubber);
ShapeKit.Sphere(root, new Vector3(size * 0f, size * 0.32f, size * 0.09f), size * 0.035f, ShapeKit.Red);
ShapeKit.Sphere(root, new Vector3(size * 0.07f, size * 0.29f, size * 0.14f), size * 0.032f, ShapeKit.Red);
ShapeKit.Sphere(root, new Vector3(size * 0.14f, size * 0.23f, size * 0.18f), size * 0.027f, ShapeKit.Red);
ShapeKit.Sphere(root, new Vector3(size * 0.20f, size * 0.15f, size * 0.20f), size * 0.022f, ShapeKit.Red);
ShapeKit.Cylinder(root, new Vector3(size * 0.22f, size * 0.015f, size * 0.20f), size * 0.14f, size * 0.03f, ShapeKit.Red);
ShapeKit.Cylinder(root, new Vector3(size * -0.15f, size * 0.01f, size * 0.18f), size * 0.08f, size * 0.02f, ShapeKit.Red);
}
}
}
