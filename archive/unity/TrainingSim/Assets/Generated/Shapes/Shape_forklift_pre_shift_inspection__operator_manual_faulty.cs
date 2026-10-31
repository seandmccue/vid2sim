using UnityEngine;
using Vid2Sim;
namespace Vid2Sim.Generated.Shapes
{
    [Vid2Sim.PartShape("forklift-pre-shift-inspection", "operator_manual", "faulty")]
    public class Shape_forklift_pre_shift_inspection__operator_manual_faulty : Vid2Sim.IPartShape
    {
        public void Build(UnityEngine.GameObject root, float size, UnityEngine.Color accent)
        {
            ShapeKit.Box(root, new Vector3(0f, size * 0.0125f, 0f), new Vector3(size * 1.0f, size * 0.025f, size * 0.70f), accent);
            ShapeKit.Box(root, new Vector3(size * 0.02f, size * 0.065f, size * 0.015f), new Vector3(size * 0.93f, size * 0.08f, size * 0.64f), ShapeKit.Cream);
            ShapeKit.Box(root, new Vector3(0f, size * 0.1175f, 0f), new Vector3(size * 1.0f, size * 0.025f, size * 0.70f), accent);
            ShapeKit.Box(root, new Vector3(size * -0.465f, size * 0.065f, 0f), new Vector3(size * 0.07f, size * 0.13f, size * 0.70f), ShapeKit.DarkSteel);
            ShapeKit.Box(root, new Vector3(size * 0.08f, size * 0.136f, size * -0.10f), new Vector3(size * 0.60f, size * 0.012f, size * 0.34f), ShapeKit.Cream);
            ShapeKit.Box(root, new Vector3(size * 0.08f, size * 0.148f, size * -0.18f), new Vector3(size * 0.44f, size * 0.012f, size * 0.05f), ShapeKit.DarkSteel);
            ShapeKit.Box(root, new Vector3(size * 0.08f, size * 0.148f, size * -0.09f), new Vector3(size * 0.44f, size * 0.012f, size * 0.05f), ShapeKit.Blue);
            ShapeKit.Box(root, new Vector3(size * 0.08f, size * 0.136f, size * 0.12f), new Vector3(size * 0.30f, size * 0.012f, size * 0.10f), ShapeKit.Yellow);
            ShapeKit.Box(root, new Vector3(size * 0.20f, size * 0.155f, size * 0.12f), new Vector3(size * 0.03f, size * 0.05f, size * 0.10f), ShapeKit.DarkSteel);
            ShapeKit.Box(root, new Vector3(size * -0.01f, size * 0.136f, size * 0.12f), new Vector3(size * 0.06f, size * 0.012f, size * 0.12f), ShapeKit.Black);
            ShapeKit.Box(root, new Vector3(size * 0.16f, size * 0.136f, size * 0.12f), new Vector3(size * 0.06f, size * 0.012f, size * 0.12f), ShapeKit.Black);
            ShapeKit.Box(root, new Vector3(size * 0.20f, size * 0.06f, size * -0.38f), new Vector3(size * 0.12f, size * 0.02f, size * 0.12f), ShapeKit.Red);
            ShapeKit.Box(root, new Vector3(size * 0.08f, size * 0.146f, size * 0.12f), new Vector3(size * 0.34f, size * 0.02f, size * 0.14f), ShapeKit.Red);
            ShapeKit.Box(root, new Vector3(size * -0.08f, size * 0.165f, size * 0.12f), new Vector3(size * 0.03f, size * 0.05f, size * 0.14f), ShapeKit.Red);
        }
    }
}
