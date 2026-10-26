using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Vid2Sim.Editor
{
    /// <summary>
    /// Headless compile gate for Muse Code output:
    ///   Unity -batchmode -nographics -quit -projectPath unity/TrainingSim -executeMethod Vid2Sim.Editor.CompileGate.Run
    /// If scripts have compiler errors Unity never reaches this method and exits non-zero on its own; when it
    /// does run, it requests a fresh compile and exits 0 only when no error was logged.
    /// </summary>
    public static class CompileGate
    {
        static int _errors;

        public static void Run()
        {
            CompilationPipeline.assemblyCompilationFinished += (path, messages) =>
            {
                foreach (var m in messages) if (m.type == CompilerMessageType.Error) { _errors++; Debug.LogError(m.message); }
            };
            CompilationPipeline.compilationFinished += _ =>
            {
                Debug.Log($"[CompileGate] finished with {_errors} error(s)");
                EditorApplication.Exit(_errors == 0 ? 0 : 1);
            };
            CompilationPipeline.RequestScriptCompilation();
            // If nothing needed recompiling the callback may not fire; poll and exit clean.
            EditorApplication.update += () =>
            {
                if (!EditorApplication.isCompiling && !EditorApplication.isUpdating && _errors == 0 && Time.realtimeSinceStartup > 5f)
                    EditorApplication.Exit(0);
            };
        }
    }
}
