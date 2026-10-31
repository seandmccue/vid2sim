using UnityEngine;
using Vid2Sim;
namespace Vid2Sim.Generated.Shapes
{
    [Vid2Sim.PartShape("forklift-pre-shift-inspection", "fork_tine")]
    public class Shape_forklift_pre_shift_inspection__fork_tine : IPartShape
    {
        public void Build(GameObject root, float size, Color accent)
        {
            ShapeKit.Box(root, new Vector3(size * -0.06f, size * 0.06f, size * 0.0f), new Vector3(size * 0.86f, size * 0.12f, size * 0.30f), accent);
            ShapeKit.Box(root, new Vector3(size * -0.44f, size * 0.035f, size * 0.0f), new Vector3(size * 0.12f, size * 0.07f, size * 0.28f), ShapeKit.Steel);
            ShapeKit.Box(root, new Vector3(size * 0.34f, size * 0.28f, size * 0.0f), new Vector3(size * 0.16f, size * 0.56f, size * 0.30f), accent);
            ShapeKit.Box(root, new Vector3(size * 0.42f, size * 0.58f, size * 0.0f), new Vector3(size * 0.10f, size * 0.10f, size * 0.30f), ShapeKit.DarkSteel);
            ShapeKit.Box(root, new Vector3(size * 0.42f, size * 0.07f, size * 0.0f), new Vector3(size * 0.10f, size * 0.10f, size * 0.30f), ShapeKit.DarkSteel);
            ShapeKit.Box(root, new Vector3(size * 0.22f, size * 0.15f, size * 0.0f), new Vector3(size * 0.10f, size * 0.08f, size * 0.30f), ShapeKit.Steel);
            ShapeKit.Box(root, new Vector3(size * 0.34f, size * 0.38f, size * -0.16f), new Vector3(size * 0.10f, size * 0.06f, size * 0.02f), ShapeKit.Yellow);
            ShapeKit.Box(root, new Vector3(size * 0.34f, size * 0.25f, size * -0.16f), new Vector3(size * 0.08f, size * 0.06f, size * 0.02f), ShapeKit.Cream);
        }
    }
}
