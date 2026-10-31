using UnityEngine;
using Vid2Sim;
namespace Vid2Sim.Generated.Hosts
{
[Vid2Sim.HostShape("forklift-pre-shift-inspection")]
public class Host_forklift_pre_shift_inspection : Vid2Sim.IHostShape
{
public void Build(GameObject root)
{
ShapeKit.Box(root, new Vector3(0f, 0.62f, 0.30f), new Vector3(0.84f, 0.45f, 1.70f), ShapeKit.Yellow);
ShapeKit.Box(root, new Vector3(-0.48f, 0.35f, 0.10f), new Vector3(0.08f, 0.08f, 0.40f), ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(0.48f, 0.35f, 0.10f), new Vector3(0.08f, 0.08f, 0.40f), ShapeKit.DarkSteel);
ShapeKit.Cylinder(root, new Vector3(-0.44f, 0.30f, -0.40f), 0.60f, 0.20f, ShapeKit.Black, new Vector3(0f, 0f, 90f));
ShapeKit.Cylinder(root, new Vector3(-0.44f, 0.30f, -0.40f), 0.24f, 0.22f, ShapeKit.Steel, new Vector3(0f, 0f, 90f));
ShapeKit.Cylinder(root, new Vector3(-0.40f, 0.225f, 0.85f), 0.45f, 0.18f, ShapeKit.Black, new Vector3(0f, 0f, 90f));
ShapeKit.Cylinder(root, new Vector3(-0.40f, 0.225f, 0.85f), 0.20f, 0.20f, ShapeKit.Steel, new Vector3(0f, 0f, 90f));
ShapeKit.Cylinder(root, new Vector3(0.40f, 0.225f, 0.85f), 0.45f, 0.18f, ShapeKit.Black, new Vector3(0f, 0f, 90f));
ShapeKit.Cylinder(root, new Vector3(0.40f, 0.225f, 0.85f), 0.20f, 0.20f, ShapeKit.Steel, new Vector3(0f, 0f, 90f));
ShapeKit.Box(root, new Vector3(-0.15f, 0.30f, -0.40f), new Vector3(0.60f, 0.12f, 0.12f), ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(0f, 0.225f, 0.85f), new Vector3(0.72f, 0.10f, 0.10f), ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(-0.25f, 0.08f, -1.55f), new Vector3(0.12f, 0.05f, 1.10f), ShapeKit.Steel);
ShapeKit.Box(root, new Vector3(-0.25f, 0.32f, -1.02f), new Vector3(0.12f, 0.50f, 0.06f), ShapeKit.Steel);
ShapeKit.Box(root, new Vector3(-0.45f, 1.05f, -0.95f), new Vector3(0.10f, 1.80f, 0.12f), ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(0.45f, 1.05f, -0.95f), new Vector3(0.10f, 1.80f, 0.12f), ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(-0.30f, 1.00f, -0.99f), new Vector3(0.08f, 1.50f, 0.08f), ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(0.30f, 1.00f, -0.99f), new Vector3(0.08f, 1.50f, 0.08f), ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(0f, 1.90f, -0.95f), new Vector3(1.00f, 0.10f, 0.12f), ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(0f, 0.35f, -1.02f), new Vector3(0.80f, 0.12f, 0.08f), ShapeKit.Steel);
ShapeKit.Box(root, new Vector3(0f, 0.70f, -1.02f), new Vector3(0.80f, 0.10f, 0.07f), ShapeKit.Steel);
ShapeKit.Cylinder(root, new Vector3(-0.38f, 0.65f, -0.68f), 0.08f, 0.60f, ShapeKit.Steel, new Vector3(15f, 0f, 0f));
ShapeKit.Cylinder(root, new Vector3(0.38f, 0.65f, -0.68f), 0.08f, 0.60f, ShapeKit.Steel, new Vector3(15f, 0f, 0f));
ShapeKit.Box(root, new Vector3(0f, 0.70f, 1.20f), new Vector3(0.84f, 0.60f, 0.40f), ShapeKit.Yellow);
ShapeKit.Box(root, new Vector3(0f, 0.95f, 1.15f), new Vector3(0.70f, 0.25f, 0.30f), ShapeKit.Yellow);
ShapeKit.Box(root, new Vector3(0f, 0.70f, 1.41f), new Vector3(0.60f, 0.30f, 0.04f), ShapeKit.Black);
ShapeKit.Box(root, new Vector3(0f, 0.58f, -0.10f), new Vector3(0.80f, 0.06f, 0.60f), ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(0.37f, 0.65f, 0.40f), new Vector3(0.10f, 0.35f, 0.60f), ShapeKit.Yellow);
ShapeKit.Box(root, new Vector3(0f, 0.72f, 0.55f), new Vector3(0.30f, 0.20f, 0.30f), ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(0f, 0.88f, 0.55f), new Vector3(0.45f, 0.12f, 0.45f), ShapeKit.Black);
ShapeKit.Box(root, new Vector3(0f, 1.13f, 0.78f), new Vector3(0.45f, 0.45f, 0.12f), ShapeKit.Black);
ShapeKit.Box(root, new Vector3(0f, 0.85f, -0.45f), new Vector3(0.70f, 0.40f, 0.25f), ShapeKit.Yellow);
ShapeKit.Box(root, new Vector3(0f, 1.02f, -0.45f), new Vector3(0.55f, 0.08f, 0.20f), ShapeKit.Black);
ShapeKit.Cylinder(root, new Vector3(0f, 0.90f, -0.25f), 0.06f, 0.40f, ShapeKit.DarkSteel, new Vector3(30f, 0f, 0f));
ShapeKit.Box(root, new Vector3(-0.42f, 1.50f, -0.55f), new Vector3(0.07f, 1.15f, 0.07f), ShapeKit.Black);
ShapeKit.Box(root, new Vector3(0.42f, 1.50f, -0.55f), new Vector3(0.07f, 1.15f, 0.07f), ShapeKit.Black);
ShapeKit.Box(root, new Vector3(-0.42f, 1.50f, 0.90f), new Vector3(0.07f, 1.15f, 0.07f), ShapeKit.Black);
ShapeKit.Box(root, new Vector3(0.42f, 1.50f, 0.90f), new Vector3(0.07f, 1.15f, 0.07f), ShapeKit.Black);
ShapeKit.Box(root, new Vector3(0f, 2.08f, 0.17f), new Vector3(0.95f, 0.08f, 1.65f), ShapeKit.Yellow);
ShapeKit.Box(root, new Vector3(0f, 2.02f, -0.20f), new Vector3(0.86f, 0.04f, 0.10f), ShapeKit.Black);
ShapeKit.Box(root, new Vector3(0f, 2.02f, 0.20f), new Vector3(0.86f, 0.04f, 0.10f), ShapeKit.Black);
ShapeKit.Box(root, new Vector3(0f, 2.02f, 0.60f), new Vector3(0.86f, 0.04f, 0.10f), ShapeKit.Black);
ShapeKit.Cylinder(root, new Vector3(0.30f, 1.20f, 0.85f), 0.07f, 0.70f, ShapeKit.Black);
ShapeKit.Cylinder(root, new Vector3(0.30f, 1.57f, 0.85f), 0.09f, 0.06f, ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(-0.28f, 0.92f, 1.42f), new Vector3(0.12f, 0.08f, 0.04f), ShapeKit.Red);
ShapeKit.Box(root, new Vector3(-0.35f, 0.95f, 1.05f), new Vector3(0.08f, 0.20f, 0.20f), ShapeKit.DarkSteel);
ShapeKit.Box(root, new Vector3(0.35f, 0.95f, 1.05f), new Vector3(0.08f, 0.20f, 0.20f), ShapeKit.DarkSteel);
ShapeKit.Anchor(root, "drive_tire", new Vector3(0.44f, 0.30f, -0.40f), new Vector3(1f, 0f, 0f), 0.60f);
ShapeKit.Anchor(root, "running_light", new Vector3(-0.51f, 1.75f, -0.55f), new Vector3(-1f, 0f, 0f), 0.12f);
ShapeKit.Anchor(root, "forklift_battery", new Vector3(-0.50f, 0.65f, 0.40f), new Vector3(-1f, 0f, 0f), 0.50f);
ShapeKit.Anchor(root, "direction_pedal", new Vector3(0.20f, 0.68f, -0.10f), new Vector3(0f, 1f, 0f), 0.20f);
ShapeKit.Anchor(root, "control_lever", new Vector3(0.35f, 1.10f, -0.35f), new Vector3(0f, 1f, 0f), 0.25f);
ShapeKit.Anchor(root, "fork_tine", new Vector3(0.25f, 0.10f, -1.60f), new Vector3(0f, 0f, -1f), 1.10f);
ShapeKit.Anchor(root, "hydraulic_hose", new Vector3(-0.52f, 1.20f, -0.95f), new Vector3(-1f, 0f, 0f), 1.00f);
ShapeKit.Anchor(root, "lift_chain", new Vector3(0f, 1.10f, -1.08f), new Vector3(0f, 0f, -1f), 1.20f);
ShapeKit.Anchor(root, "carriage_bolt", new Vector3(0.28f, 0.70f, -1.09f), new Vector3(0f, 0f, -1f), 0.05f);
ShapeKit.Anchor(root, "brake_pedal", new Vector3(-0.15f, 0.68f, -0.10f), new Vector3(0f, 1f, 0f), 0.15f);
ShapeKit.Anchor(root, "steering_wheel", new Vector3(0f, 1.12f, -0.15f), new Vector3(0f, 0f, 1f), 0.30f);
ShapeKit.Anchor(root, "propane_tank", new Vector3(0f, 1.12f, 1.02f), new Vector3(0f, 0f, 1f), 0.65f);
}
}
}
