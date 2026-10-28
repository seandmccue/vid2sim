using UnityEngine;
using Vid2Sim;
namespace Vid2Sim.Generated.Shapes
{
[Vid2Sim.PartShape("forklift-pre-shift-inspection", "brake_pedal")]
public class Shape_forklift_pre_shift_inspection__brake_pedal : IPartShape
{
public void Build(GameObject root, float size, Color accent)
{
ShapeKit.Box(root, new Vector3(0f, size * 0.08f, size * 0.10f), new Vector3(size * 0.60f, size * 0.16f, size * 0.50f), ShapeKit.DarkSteel);
ShapeKit.Cylinder(root, new Vector3(0f, size * 0.20f, size * 0.08f), size * 0.14f, size * 0.68f, ShapeKit.Steel, new Vector3(0f, 0f, 90f));
ShapeKit.Box(root, new Vector3(0f, size * 0.45f, size * 0.05f), new Vector3(size * 0.20f, size * 0.70f, size * 0.12f), ShapeKit.Steel, new Vector3(-15f, 0f, 0f));
ShapeKit.Box(root, new Vector3(0f, size * 0.68f, size * -0.05f), new Vector3(size * 0.18f, size * 0.14f, size * 0.18f), ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(0f, size * 0.68f, size * -0.15f), new Vector3(size * 0.70f, size * 0.50f, size * 0.12f), accent);
ShapeKit.Box(root, new Vector3(0f, size * 0.52f, size * -0.225f), new Vector3(size * 0.60f, size * 0.06f, size * 0.05f), ShapeKit.Rubber);
ShapeKit.Box(root, new Vector3(0f, size * 0.62f, size * -0.225f), new Vector3(size * 0.60f, size * 0.06f, size * 0.05f), ShapeKit.Rubber);
ShapeKit.Box(root, new Vector3(0f, size * 0.72f, size * -0.225f), new Vector3(size * 0.60f, size * 0.06f, size * 0.05f), ShapeKit.Rubber);
ShapeKit.Box(root, new Vector3(0f, size * 0.82f, size * -0.225f), new Vector3(size * 0.60f, size * 0.06f, size * 0.05f), ShapeKit.Rubber);
}
}
}
