using System;
using UnityEngine;

namespace Vid2Sim
{
    /// <summary>
    /// Test mode. Guided mode teaches: a walkthrough first, the expected part lit, a ghost and an arrow for the
    /// motion, the check text on screen, and a wrong call coached while the step stays open. Exam mode measures:
    /// no walkthrough, no hints, no lit part, every call is final, nothing is revealed until the end, and the
    /// score is the faults caught (false alarms cost). Faults are re-rolled on every attempt either way.
    /// On: ?mode=exam on the web URL, the EXAM block on the bench, or the T key. Switching restarts the attempt.
    /// </summary>
    public class ExamMode : MonoBehaviour
    {
        /// <summary>Read by the guide, coach, HUD and detector; one instance per scene sets it.</summary>
        public static bool On { get; private set; }
        public static bool RequestedByUrl => Application.absoluteURL.Contains("mode=exam") || Application.absoluteURL.Contains("mode=test");

        public KeyCode toggleKey = KeyCode.T;
        public event Action<bool> Changed;
        PartBehaviour _button;

        public bool Enabled
        {
            get => On;
            set
            {
                if (On == value) return;
                On = value;
                Paint();
                Changed?.Invoke(value);
            }
        }

        void Awake() { if (RequestedByUrl) On = true; }
        void OnDestroy() { On = false; }

        /// <summary>The EXAM block at the front centre of the bench, between the verdict buttons. Poke it to switch.</summary>
        public void AttachBench(PartFactory factory, Transform bench)
        {
            if (factory == null || bench == null) return;
            _button = BenchControls.Button(factory, bench, "__exam", "EXAM", ShapeKit.Steel, new Vector3(0f, 0f, -0.15f));
            _button.Selected += _ => Enabled = !Enabled;
            Paint();
        }

        void Paint()
        {
            BenchControls.Recolor(_button, On ? ShapeKit.Blue : ShapeKit.Steel);
            BenchControls.Relabel(_button, On ? "EXAM: ON" : "EXAM: OFF");
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey)) Enabled = !Enabled;
        }
    }
}
