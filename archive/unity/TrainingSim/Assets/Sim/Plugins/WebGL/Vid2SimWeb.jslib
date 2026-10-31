// Browser text-to-speech for the web output. Same coaching lines as the Quest build's Android TTS.
mergeInto(LibraryManager.library, {
  Vid2SimSpeak: function (textPtr, interrupt) {
    try {
      var text = UTF8ToString(textPtr);
      if (!window.speechSynthesis || !text) return;
      if (interrupt) window.speechSynthesis.cancel();
      var u = new SpeechSynthesisUtterance(text);
      u.rate = 1.0;
      window.speechSynthesis.speak(u);
    } catch (e) { console.warn('Vid2SimSpeak', e); }
  }
});
