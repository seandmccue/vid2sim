using UnityEngine;
using Vid2Sim;
namespace Vid2Sim.Generated.Shapes
{
    [Vid2Sim.PartShape("forklift-pre-shift-inspection", "direction_pedal")]
    public class Shape_forklift_pre_shift_inspection__direction_pedal : IPartShape
    {
        public void Build(GameObject root, float size, Color accent)
        {
            ShapeKit.Box(root, new Vector3(0f, size * 0.06f, 0f), new Vector3(size * 0.8f, size * 0.12f, size * 0.8f), ShapeKit.Rubber);
            ShapeKit.Cylinder(root, new Vector3(0f, size * 0.2f, 0f), size * 0.1f, size * 0.7f, ShapeKit.Steel, new Vector3(0f, 0f, 90f));
            ShapeKit.Box(root, new Vector3(0f, size * 0.32f, 0f), new Vector3(size * 0.58f, size * 0.07f, size * 0.68f), accent, new Vector3(-12f, 0f, 0f));
            ShapeKit.Box(root, new Vector3(0f, size * 0.327f, size * -0.206f), new Vector3(size * 0.5f, size * 0.03f, size * 0.08f), ShapeKit.Rubber, new Vector3(-12f, 0f, 0f));
            ShapeKit.Box(root, new Vector3(0f, size * 0.369f, size * -0.01f), new Vector3(size * 0.5f, size * 0.03f, size * 0.08f), ShapeKit.Rubber, new Vector3(-12f, 0f, 0f));
            ShapeKit.Box(root, new Vector3(0f, size * 0.411f, size * 0.185f), new Vector3(size * 0.5f, size * 0.03f, size * 0.08f), ShapeKit.Rubber, new Vector3(-12f, 0f, 0f));
            ShapeKit.Box(root, new Vector3(0f, size * 0.13f, size * -0.32f), new Vector3(size * 0.3f, size * 0.02f, size * 0.08f), ShapeKit.Green);
            ShapeKit.Box(root, new Vector3(0f, size * 0.13f, size * 0.32f), new Vector3(size * 0.3f, size * 0.02f, size * 0.08f), ShapeKit.Blue);
        }
    }
}
