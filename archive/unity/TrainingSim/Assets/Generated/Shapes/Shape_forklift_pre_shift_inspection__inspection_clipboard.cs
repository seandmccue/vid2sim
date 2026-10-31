using UnityEngine;
using Vid2Sim;
namespace Vid2Sim.Generated.Shapes
{
[Vid2Sim.PartShape("forklift-pre-shift-inspection", "inspection_clipboard")]
public class Shape_forklift_pre_shift_inspection__inspection_clipboard : Vid2Sim.IPartShape
{
public void Build(GameObject root, float size, Color accent)
{
ShapeKit.Box(root, new Vector3(size * 0f, size * 0.35f, size * 0.02f), new Vector3(size * 0.96f, size * 0.68f, size * 0.05f), accent);
ShapeKit.Box(root, new Vector3(size * 0f, size * 0.335f, size * -0.02f), new Vector3(size * 0.82f, size * 0.54f, size * 0.02f), ShapeKit.Cream);
ShapeKit.Box(root, new Vector3(size * 0f, size * 0.68f, size * 0f), new Vector3(size * 0.28f, size * 0.12f, size * 0.08f), ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(size * 0f, size * 0.73f, size * -0.02f), new Vector3(size * 0.22f, size * 0.04f, size * 0.06f), ShapeKit.Steel);
ShapeKit.Box(root, new Vector3(size * 0f, size * 0.55f, size * -0.033f), new Vector3(size * 0.6f, size * 0.06f, size * 0.012f), ShapeKit.Blue);
ShapeKit.Box(root, new Vector3(size * -0.06f, size * 0.46f, size * -0.033f), new Vector3(size * 0.48f, size * 0.03f, size * 0.012f), ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(size * 0.3f, size * 0.46f, size * -0.033f), new Vector3(size * 0.08f, size * 0.06f, size * 0.014f), ShapeKit.Green);
ShapeKit.Box(root, new Vector3(size * -0.06f, size * 0.36f, size * -0.033f), new Vector3(size * 0.48f, size * 0.03f, size * 0.012f), ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(size * 0.3f, size * 0.36f, size * -0.033f), new Vector3(size * 0.08f, size * 0.06f, size * 0.014f), ShapeKit.Green);
ShapeKit.Box(root, new Vector3(size * -0.06f, size * 0.26f, size * -0.033f), new Vector3(size * 0.48f, size * 0.03f, size * 0.012f), ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(size * 0.3f, size * 0.26f, size * -0.033f), new Vector3(size * 0.08f, size * 0.06f, size * 0.014f), ShapeKit.Red);
ShapeKit.Box(root, new Vector3(size * -0.02f, size * 0.15f, size * -0.033f), new Vector3(size * 0.6f, size * 0.025f, size * 0.012f), ShapeKit.Yellow);
}
}
}
