using UnityEngine;

namespace Vid2Sim
{
    /// <summary>Android TextToSpeech through JNI for instruction and coaching lines. Logs in the editor.</summary>
    public class Speech : MonoBehaviour
    {
        public bool enabledSpeech = true;
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject _tts;
        bool _ready;

        void Awake()
        {
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                _tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, new InitListener(this));
            }
            catch (System.Exception e) { Debug.LogWarning("[Speech] TTS init failed: " + e.Message); }
        }

        class InitListener : AndroidJavaProxy
        {
            readonly Speech _owner;
            public InitListener(Speech owner) : base("android.speech.tts.TextToSpeech$OnInitListener") { _owner = owner; }
            public void onInit(int status) { _owner._ready = status == 0; }
        }

        public void Say(string text, bool interrupt = true)
        {
            if (!enabledSpeech || string.IsNullOrEmpty(text) || _tts == null || !_ready) return;
            // speak(CharSequence, int queueMode, Bundle params, String utteranceId): QUEUE_FLUSH = 0, QUEUE_ADD = 1
            _tts.Call<int>("speak", text, interrupt ? 0 : 1, null, "vid2sim");
        }

        void OnDestroy() { _tts?.Call("shutdown"); }
#elif UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void Vid2SimSpeak(string text, int interrupt);

        public void Say(string text, bool interrupt = true)
        {
            if (!enabledSpeech || string.IsNullOrEmpty(text)) return;
            try { Vid2SimSpeak(text, interrupt ? 1 : 0); } catch (System.Exception e) { Debug.LogWarning("[Speech] web TTS: " + e.Message); }
        }
#else
        public void Say(string text, bool interrupt = true)
        {
            if (enabledSpeech && !string.IsNullOrEmpty(text)) Debug.Log($"[Speech] \"{text}\"");
        }
#endif
    }
}
