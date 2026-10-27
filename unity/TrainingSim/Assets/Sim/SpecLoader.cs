using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Vid2Sim
{
    /// <summary>
    /// Loads and validates spec.json at startup and exposes Procedure. Looks in persistentDataPath first
    /// (the dashboard hot-pushes there over adb), then StreamingAssets (the shipped payload).
    /// </summary>
    public class SpecLoader : MonoBehaviour
    {
        [Tooltip("Procedure id (folder name). Empty = first entry in procedures/index.json.")]
        public string procedureId = "";
        [Tooltip("Optional: load spec.json from this URL instead of disk (web output). Set from the page URL's ?spec= on WebGL.")]
        public string specUrl = "";
        [Tooltip("Optional: base URL for textures/<part>.png when specUrl is used. ?textures= on WebGL.")]
        public string texturesUrl = "";

        public bool UsesRemoteSpec => !string.IsNullOrEmpty(specUrl);
        string _lastRemoteText;

        public ProcedureSpec Procedure { get; private set; }
        public string LoadedFrom { get; private set; }
        public string Error { get; private set; }
        public event Action<ProcedureSpec> Loaded;

        public static string PersistentDir(string id) => Path.Combine(Application.persistentDataPath, "procedures", id);
        public static string StreamingDir(string id) => Path.Combine(Application.streamingAssetsPath, "procedures", id);

        /// <summary>Parse ?spec=&textures=&procedure= from a page URL (the web build is launched by the dashboard).</summary>
        public static System.Collections.Generic.Dictionary<string, string> ParseQuery(string url)
        {
            var q = new System.Collections.Generic.Dictionary<string, string>();
            if (string.IsNullOrEmpty(url)) return q;
            int i = url.IndexOf('?');
            if (i < 0) return q;
            foreach (var pair in url.Substring(i + 1).Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                q[UnityWebRequest.UnEscapeURL(pair.Substring(0, eq))] = UnityWebRequest.UnEscapeURL(pair.Substring(eq + 1));
            }
            return q;
        }

        void ApplyPageQuery()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var q = ParseQuery(Application.absoluteURL);
            if (q.TryGetValue("spec", out var sp) && !string.IsNullOrEmpty(sp)) specUrl = sp;
            if (q.TryGetValue("textures", out var tx) && !string.IsNullOrEmpty(tx)) texturesUrl = tx;
            if (q.TryGetValue("procedure", out var pid) && !string.IsNullOrEmpty(pid)) procedureId = pid;
#endif
        }

        public IEnumerator Load()
        {
            Error = null;
            ApplyPageQuery();
            if (UsesRemoteSpec)
            {
                string remote = null;
                yield return ReadText(specUrl, t => remote = t);
                if (string.IsNullOrEmpty(remote)) { Error = $"could not fetch spec from {specUrl}"; yield break; }
                _lastRemoteText = remote;
                LoadedFrom = specUrl;
                ProcedureSpec rs;
                try { rs = JsonUtility.FromJson<ProcedureSpec>(remote); }
                catch (Exception e) { Error = "spec parse failed: " + e.Message; yield break; }
                var rerrors = SpecValidator.Validate(rs);
                if (rerrors.Count > 0) { Error = "spec failed validation: " + string.Join("; ", rerrors); yield break; }
                procedureId = rs.procedure.id;
                Procedure = rs;
                Debug.Log($"[SpecLoader] loaded '{rs.procedure.title}' ({rs.steps.Length} steps, {rs.parts.Length} parts) from {specUrl}");
                Loaded?.Invoke(rs);
                yield break;
            }
            if (string.IsNullOrEmpty(procedureId))
            {
                string indexJson = null;
                yield return ReadText(Path.Combine(Application.streamingAssetsPath, "procedures", "index.json"), t => indexJson = t);
                if (!string.IsNullOrEmpty(indexJson))
                {
                    var idx = JsonUtility.FromJson<ProcedureIndex>(indexJson);
                    if (idx?.procedures != null && idx.procedures.Length > 0) procedureId = idx.procedures[0];
                }
            }
            if (string.IsNullOrEmpty(procedureId)) { Error = "no procedure id and no index.json"; yield break; }

            string json = null;
            var hot = Path.Combine(PersistentDir(procedureId), "spec.json");
            if (File.Exists(hot)) { json = File.ReadAllText(hot); LoadedFrom = hot; }
            else
            {
                var shipped = Path.Combine(StreamingDir(procedureId), "spec.json");
                yield return ReadText(shipped, t => json = t);
                LoadedFrom = shipped;
            }
            if (string.IsNullOrEmpty(json)) { Error = $"spec.json not found for '{procedureId}'"; yield break; }

            ProcedureSpec spec;
            try { spec = JsonUtility.FromJson<ProcedureSpec>(json); }
            catch (Exception e) { Error = "spec.json parse failed: " + e.Message; yield break; }
            var errors = SpecValidator.Validate(spec);
            if (errors.Count > 0) { Error = "spec failed validation: " + string.Join("; ", errors); yield break; }
            Procedure = spec;
            Debug.Log($"[SpecLoader] loaded '{spec.procedure.title}' ({spec.steps.Length} steps, {spec.parts.Length} parts) from {LoadedFrom}");
            Loaded?.Invoke(spec);
        }

        /// <summary>Texture for a part: hot dir first, then StreamingAssets. Null when none.</summary>
        /// <summary>Web hot reload: true when the remote spec text differs from what was loaded.</summary>
        public IEnumerator RemoteSpecChanged(Action<bool> done)
        {
            string remote = null;
            yield return ReadText(specUrl, t => remote = t);
            done(!string.IsNullOrEmpty(remote) && remote != _lastRemoteText);
        }

        public IEnumerator LoadTexture(string partId, Action<Texture2D> done)
        {
            if (UsesRemoteSpec)
            {
                if (string.IsNullOrEmpty(texturesUrl)) { done(null); yield break; }
                byte[] rb = null;
                yield return ReadBytes(texturesUrl.TrimEnd('/') + "/" + partId + ".png", b => rb = b);
                if (rb != null && rb.Length > 0)
                {
                    var rt = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (rt.LoadImage(rb)) { done(rt); yield break; }
                }
                done(null);
                yield break;
            }
            foreach (var dir in new[] { PersistentDir(procedureId), StreamingDir(procedureId) })
            {
                var path = Path.Combine(dir, "textures", partId + ".png");
                byte[] bytes = null;
                yield return ReadBytes(path, b => bytes = b);
                if (bytes != null && bytes.Length > 0)
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(bytes)) { done(tex); yield break; }
                }
            }
            done(null);
        }

        /// <summary>Modified time of the hot spec, for dev hot reload polling.</summary>
        public DateTime HotSpecMtime()
        {
            var hot = Path.Combine(PersistentDir(procedureId), "spec.json");
            return File.Exists(hot) ? File.GetLastWriteTimeUtc(hot) : DateTime.MinValue;
        }

        static IEnumerator ReadText(string path, Action<string> done)
        {
            byte[] bytes = null;
            yield return ReadBytes(path, b => bytes = b);
            done(bytes == null ? null : System.Text.Encoding.UTF8.GetString(bytes));
        }

        static IEnumerator ReadBytes(string path, Action<byte[]> done)
        {
            // On Android, StreamingAssets lives inside the APK and needs UnityWebRequest; elsewhere it's a plain file.
            if (path.Contains("://") || path.StartsWith("jar:") || path.StartsWith("/") && Application.platform == RuntimePlatform.WebGLPlayer)
            {
                using var req = UnityWebRequest.Get(path);
                yield return req.SendWebRequest();
                done(req.result == UnityWebRequest.Result.Success ? req.downloadHandler.data : null);
            }
            else
            {
                done(File.Exists(path) ? File.ReadAllBytes(path) : null);
            }
        }
    }
}
