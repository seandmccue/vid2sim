/* Apprentice guide: one page, phone browser.
   Landing lists procedures. In a procedure: rear camera (or sample footage) full-screen, the current step's
   part found by SAM 3.1 on a frame every ~1 s through /guide/find and boxed on an overlay canvas, a step card
   with the instruction, the part's keyframe crop and a Done button. Boxes clear on every response so a stale
   box never survives a moved camera. One request in flight at a time; frames are dropped while waiting. */
(() => {
  const $ = id => document.getElementById(id);
  const q = new URLSearchParams(location.search);
  const MUTE = q.get('mute') === '1';
  const FLOOR = 0.5;
  const AUTO = q.get('auto') !== '0';          // hands-free: once the part is seen, Spark checks the step by itself
  const AUTO_EVERY_MS = 4000;                   // a silent check every 4 s while the part is in view
  const voice = { on: false, rec: null, stream: null, mime: '', busy: false, speaking: false };
  const state = { spec: null, pid: null, i: 0, timer: null, inFlight: false, intervalMs: 1000, lastResult: null,
                  stepStart: 0, lastFound: 0, results: [], sessionId: null, source: 'camera', stream: null, rtt: [] };

  const say = text => { if (MUTE || !('speechSynthesis' in window) || !text) return; try { const u = new SpeechSynthesisUtterance(text); voice.speaking = true; u.onend = u.onerror = () => { voice.speaking = false; }; speechSynthesis.cancel(); speechSynthesis.speak(u); } catch (e) { voice.speaking = false; } };
  const chime = () => { if (MUTE) return; try { const ac = new (window.AudioContext || window.webkitAudioContext)(); const o = ac.createOscillator(), g = ac.createGain(); o.frequency.value = 880; g.gain.value = 0.08; o.connect(g); g.connect(ac.destination); o.start(); o.stop(ac.currentTime + 0.12); } catch (e) {} };
  const toast = (text, bad) => { const t = document.createElement('div'); t.className = 'toast' + (bad ? ' bad' : ''); t.textContent = text; $('stage').appendChild(t); setTimeout(() => t.remove(), bad ? 6000 : 3500); };

  // ---------------- landing
  async function landing() {
    document.getElementById('app').className = 'landing';
    $('guide').hidden = true;
    // a laptop viewer gets a QR to carry the link to a phone (the camera needs HTTPS, which the link already is)
    const phone = /Android|iPhone|iPad|Mobile/i.test(navigator.userAgent);
    const link = location.origin + location.pathname;
    $('qr').hidden = phone;
    if (!phone) { $('qrimg').src = `/guide/qr.svg?url=${encodeURIComponent(link)}`; $('qrurl').textContent = link; }
    const ul = $('procedures');
    let list = [];
    try { list = await (await fetch('/guide/procedures')).json(); } catch (e) { ul.innerHTML = '<li class="muted">Backend not reachable.</li>'; return; }
    if (!list.length) { ul.innerHTML = '<li class="muted">No procedures yet. Run the pipeline on an expert video first.</li>'; return; }
    ul.innerHTML = ''; $('more-list').innerHTML = '';
    const more = list.filter(p => p.featured === false), featured = list.filter(p => p.featured !== false);
    $('more').hidden = !(featured.length && more.length);
    for (const p of list) {
      const li = document.createElement('li');
      if (p.featured) li.className = 'featured';
      li.innerHTML = `${p.thumbnail ? `<img src="${p.thumbnail}" alt="">` : '<div class="ph">▣</div>'}
        <div><h3></h3><p class="d"></p><p class="muted">${p.steps} steps · ${p.parts} parts${p.safety_steps ? ` · ${p.safety_steps} safety` : ''}</p></div>
        <div class="actions"><button class="go">Guide me (camera)</button>${p.sample_video ? '<button class="sample">Watch on sample footage</button>' : ''}</div>`;
      li.querySelector('h3').textContent = p.title;
      li.querySelector('.d').textContent = p.description;
      li.querySelector('.go').onclick = () => start(p.id, null);
      const s = li.querySelector('.sample'); if (s) s.onclick = () => start(p.id, p.sample_video);
      (featured.length && p.featured === false ? $('more-list') : ul).appendChild(li);
    }
  }

  // ---------------- guide
  async function start(pid, videoUrl) {
    state.pid = pid; state.i = 0; state.results = []; state.sessionId = null; state.rtt = [];
    stopListening();
    try { if (!state.cards) state.cards = await (await fetch('/guide/procedures')).json(); } catch (e) { state.cards = []; }
    state.card = (state.cards || []).find(c => c.id === pid) || {};
    state.videoUrl = videoUrl;
    state.source = videoUrl ? 'sample' : 'camera';
    history.replaceState(null, '', `?procedure=${encodeURIComponent(pid)}${videoUrl ? '&video=' + encodeURIComponent(videoUrl) : ''}${MUTE ? '&mute=1' : ''}`);
    try { state.spec = await (await fetch(`/api/procedures/${encodeURIComponent(pid)}/spec`)).json(); }
    catch (e) { toast('Could not load the procedure', true); return; }
    document.getElementById('app').className = 'guiding';
    $('guide').hidden = false; $('finish').hidden = true; $('card').hidden = false;
    const ok = await openSource(videoUrl);
    sourceButton(ok);
    if (!ok) return;
    showStep();
  }

  // camera <-> sample footage, same loop either way; offered prominently when the camera is not available
  function sourceButton(cameraOk) {
    const b = $('source');
    const hasSample = !!state.card.sample_video;
    b.hidden = !(hasSample || state.source === 'sample');
    b.textContent = state.source === 'sample' ? 'Use camera' : 'Sample footage';
    b.style.background = cameraOk === false ? 'var(--accent)' : '';
    b.style.color = cameraOk === false ? '#06202b' : '';
    b.onclick = () => start(state.pid, state.source === 'sample' ? null : state.card.sample_video);
  }

  async function openSource(videoUrl) {
    const v = $('cam');
    stopSource();
    if (videoUrl) {
      v.srcObject = null; v.src = videoUrl; v.loop = true; v.muted = true;
      try { await v.play(); } catch (e) {}
      return true;
    }
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
      status(location.protocol === 'https:' ? 'This browser has no camera access.' : 'Camera needs HTTPS. Open the https link, or use sample footage.');
      return false;
    }
    try {
      state.stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' }, width: { ideal: 1280 }, height: { ideal: 720 } }, audio: false });
      v.srcObject = state.stream; await v.play();
      return true;
    } catch (e) {
      status('Camera blocked: ' + (e.name || e.message) + '. Allow the camera, or use sample footage.');
      return false;
    }
  }

  function stopSource() {
    const v = $('cam');
    if (state.stream) { for (const t of state.stream.getTracks()) t.stop(); state.stream = null; }
    v.pause(); v.removeAttribute('src'); v.srcObject = null;
  }

  function status(text, pulse) { const s = $('status'); s.textContent = text || ''; s.className = 'status' + (pulse ? ' pulse' : ''); }

  function showStep() {
    const steps = state.spec.steps, s = steps[state.i];
    const part = state.spec.parts.find(p => p.id === s.part_id) || { name: s.part_id };
    $('stepno').textContent = `Step ${s.order} of ${steps.length}` + (s.safety ? ' · safety step' : '');
    $('instruction').textContent = s.instruction;
    $('hint').textContent = s.safety ? 'Safety step: cannot be skipped.' : '';
    $('hint').className = 'hint' + (s.safety ? ' safety' : '');
    const th = $('thumb'); th.hidden = false;
    th.src = `/api/procedures/${encodeURIComponent(state.pid)}/files/bundle/crops/${encodeURIComponent(part.id)}.png`;
    th.onerror = () => { th.hidden = true; };
    const dots = $('dots'); dots.innerHTML = steps.map((_, k) => `<i class="${k < state.i ? 'done' : k === state.i ? 'now' : ''}"></i>`).join('');
    clearOverlay();
    status(`Finding the ${part.name}…`, true);
    state.stepStart = performance.now(); state.lastFound = 0; state.lastResult = null;
    state.results.push({ step_order: s.order, part_id: s.part_id, frames: 0, hits: 0, seconds: 0, attempts: 1, done_by: null, checks: [], override: false });
    state.checking = false; $('done').disabled = false; $('done').textContent = 'Done'; hideOverride();
    state.lastAuto = performance.now(); state.autoNo = 0;
    say(s.instruction);
    loop(true);
  }

  // ---------------- frame loop
  function loop(restart) {
    if (restart && state.timer) { clearInterval(state.timer); state.timer = null; }
    if (!state.timer) state.timer = setInterval(tick, state.intervalMs);
  }

  const work = document.createElement('canvas');
  function grabFrame() {
    const v = $('cam');
    if (!v.videoWidth || !v.videoHeight) return null;
    const scale = Math.min(1, 1280 / Math.max(v.videoWidth, v.videoHeight));
    work.width = Math.round(v.videoWidth * scale); work.height = Math.round(v.videoHeight * scale);
    work.getContext('2d').drawImage(v, 0, 0, work.width, work.height);
    return work.toDataURL('image/jpeg', 0.7);
  }

  async function tick() {
    if (state.inFlight || $('finish').hidden === false) return;
    const s = state.spec.steps[state.i]; if (!s) return;
    const frame = grabFrame(); if (!frame) return;
    state.inFlight = true;
    const t0 = performance.now();
    try {
      const r = await fetch('/guide/find', { method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ procedure_id: state.pid, step_order: s.order, image: frame, session_id: state.sessionId }) });
      if (!r.ok) throw new Error('HTTP ' + r.status);
      const res = await r.json();
      const rtt = performance.now() - t0; state.rtt.push(rtt);
      if (state.rtt.length >= 5 && median(state.rtt.slice(-5)) < 800 && state.intervalMs !== 700) { state.intervalMs = 700; loop(true); }
      if (res.step_order === state.spec.steps[state.i].order) render(res);
    } catch (e) {
      status('Connection hiccup, retrying…', true);
    } finally { state.inFlight = false; }
  }

  const median = a => { const b = [...a].sort((x, y) => x - y); return b[Math.floor(b.length / 2)]; };

  function clearOverlay() { const c = $('overlay'); c.width = c.clientWidth * devicePixelRatio; c.height = c.clientHeight * devicePixelRatio; }

  function render(res) {
    const rec = state.results[state.results.length - 1]; rec.frames++;
    clearOverlay();                                    // never a stale box after the camera moved
    const v = $('cam'), c = $('overlay'), ctx = c.getContext('2d');
    const name = res.part_name;
    const m = res.matches || [];
    if (!m.length) {
      const waited = (performance.now() - (state.lastFound || state.stepStart)) / 1000;
      status(waited > 5 ? `Finding the ${name}… try moving closer or to the side.` : `Finding the ${name}…`, true);
      return;
    }
    rec.hits++; state.lastFound = performance.now();
    if (AUTO && !state.checking && state.autoNo < 6 && performance.now() - state.lastAuto > AUTO_EVERY_MS) { state.lastAuto = performance.now(); autoCheck(); }
    // object-fit: cover maps the frame onto the element: scale by the larger ratio, centre the overflow
    const sx = c.clientWidth / res.width, sy = c.clientHeight / res.height, k = Math.max(sx, sy);
    const ox = (c.clientWidth - res.width * k) / 2, oy = (c.clientHeight - res.height * k) / 2;
    ctx.scale(devicePixelRatio, devicePixelRatio);
    ctx.lineWidth = 3; ctx.font = '600 15px -apple-system, Roboto, sans-serif';
    m.forEach((b, i) => {
      const x = ox + b.x1 * k, y = oy + b.y1 * k, w = (b.x2 - b.x1) * k, h = (b.y2 - b.y1) * k;
      ctx.strokeStyle = '#39c6ff'; ctx.shadowColor = 'rgba(0,0,0,.6)'; ctx.shadowBlur = 6;
      ctx.strokeRect(x, y, w, h);
      const label = m.length > 1 ? `${name} ${i + 1}` : name;
      const tw = ctx.measureText(label).width + 12;
      ctx.shadowBlur = 0; ctx.fillStyle = '#39c6ff'; ctx.fillRect(x, Math.max(0, y - 24), tw, 22);
      ctx.fillStyle = '#06202b'; ctx.fillText(label, x + 6, Math.max(16, y - 8));
    });
    status(m.length > 1 ? `${m.length} found. Either works, or use the one you're touching.` : `${name} found (${Math.round(m[0].confidence * 100)}%)`, false);
  }

  // ---------------- advance: Done asks Spark whether the step's check is true in the current frame
  async function checkAndAdvance(by) {
    if (state.checking || !$('finish').hidden) return;
    const s = state.spec.steps[state.i], rec = state.results[state.results.length - 1];
    const part = state.spec.parts.find(p => p.id === s.part_id) || { name: s.part_id };
    const frame = grabFrame();
    if (!frame) { toast('No camera frame yet', true); return; }
    state.checking = true; state.lastAuto = performance.now(); $('done').disabled = true; $('done').textContent = 'Checking…';
    status(`Checking: ${s.check || s.instruction}`, true);
    try {
      const r = await fetch('/guide/check', { method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ procedure_id: state.pid, step_order: s.order, image: frame, session_id: state.sessionId }) });
      if (!r.ok) throw new Error('HTTP ' + r.status);
      const res = await r.json();
      rec.checks.push({ verdict: res.verdict, note: res.note, wall_ms: res.wall_ms, by });
      if (res.verdict === 'yes') { done(by); return; }
      rec.attempts++;
      // the verdict lives in the card's hint line until the next check, since the find loop keeps rewriting the status
      const hint = $('hint'); hint.textContent = res.message; hint.className = 'hint ' + (res.verdict === 'no' ? 'bad' : 'warn');
      toast(res.message, res.verdict === 'no'); say(res.message);
      if (res.verdict === 'no') showOverride();        // once per step, logged, shown in Verify
    } catch (e) {
      toast('Could not check that frame. Try again.', true);
    } finally {
      state.checking = false; $('done').disabled = false; $('done').textContent = 'Done';
    }
  }

  // hands-free: Spark looks at the frame while the part is in view; yes advances by itself, anything else stays
  // silent (the trainee still has Done for the explicit call). Capped per step so a never-true check is not an endless bill.
  async function autoCheck() {
    if (state.checking || !$('finish').hidden) return;
    const s = state.spec.steps[state.i], rec = state.results[state.results.length - 1];
    const frame = grabFrame(); if (!frame) return;
    state.checking = true;
    $('listen').hidden = false; $('listen').textContent = 'Checking the step…';
    try {
      const r = await fetch('/guide/check', { method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ procedure_id: state.pid, step_order: s.order, image: frame, session_id: state.sessionId }) });
      if (!r.ok) return;
      const res = await r.json();
      rec.checks.push({ verdict: res.verdict, note: res.note, wall_ms: res.wall_ms, by: 'auto' });
      if (res.verdict === 'yes') { state.checking = false; done('auto'); return; }
      state.autoNo++;
    } catch (e) { /* next frame */ }
    finally {
      state.checking = false;
      if (!voice.on) $('listen').hidden = true; else $('listen').textContent = 'Listening… say "done" or ask a question';
    }
  }

  function showOverride() {
    const rec = state.results[state.results.length - 1];
    if (rec.override || $('override')) return;
    const b = document.createElement('button');
    b.id = 'override'; b.className = 'ghost small'; b.textContent = 'I did it, override';
    b.onclick = () => { rec.override = true; rec.checks.push({ verdict: 'override', by: 'tap' }); hideOverride(); done('override'); };
    $('card').querySelector('.row.buttons').insertBefore(b, $('done'));
  }
  function hideOverride() { const b = $('override'); if (b) b.remove(); }

  function done(by) {
    const rec = state.results[state.results.length - 1];
    rec.seconds = Math.round((performance.now() - state.stepStart) / 100) / 10; rec.done_by = by || 'tap';
    chime();
    if (by === 'auto') toast('Done, nice.');
    if (state.i + 1 < state.spec.steps.length) { state.i++; showStep(); }
    else finish();
  }

  async function finish() {
    if (state.timer) { clearInterval(state.timer); state.timer = null; }
    stopListening();
    clearOverlay(); status('');
    const total = state.results.reduce((a, r) => a + r.seconds, 0);
    const frames = state.results.reduce((a, r) => a + r.frames, 0), hits = state.results.reduce((a, r) => a + r.hits, 0);
    const overrides = state.results.filter(r => r.override).length;
    const payload = { mode: 'guide', procedure_id: state.pid, source: state.source, steps: state.results, total_seconds: Math.round(total * 10) / 10, overrides,
                      frames_sent: frames, frames_with_match: hits, median_rtt_ms: state.rtt.length ? Math.round(median(state.rtt)) : null, user_agent: navigator.userAgent };
    let saved = '';
    try { const r = await (await fetch('/guide/sessions', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) })).json(); saved = `Saved as session ${r.session_id}.`; } catch (e) { saved = 'Could not save the session.'; }
    $('summary').textContent = `${state.spec.steps.length} steps in ${payload.total_seconds}s.\n` +
      state.results.map(r => `Step ${r.step_order}: ${r.seconds}s, part seen in ${r.hits}/${r.frames} frames, ${r.checks.length} check${r.checks.length === 1 ? '' : 's'}${r.override ? ' (overridden)' : ''}`).join('\n') +
      `\n${payload.median_rtt_ms ? `Median find round-trip ${payload.median_rtt_ms} ms. ` : ''}${saved}`;
    $('finish').hidden = false;
    say('Procedure complete.');
  }

  // ---------------- voice: a 3 s recorder restarted in a loop so every blob is a complete file; "done" advances,
  // a question goes to Spark with the current frame and the answer is spoken. Paused while we speak.
  async function startListening() {
    if (!navigator.mediaDevices || !window.MediaRecorder) { toast('Voice needs a newer browser', true); return; }
    try { voice.stream = await navigator.mediaDevices.getUserMedia({ audio: true }); }
    catch (e) { toast('Microphone blocked: ' + (e.name || e.message), true); return; }
    voice.mime = ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4', 'audio/ogg;codecs=opus'].find(m => MediaRecorder.isTypeSupported(m)) || '';
    voice.on = true; $('mic').classList.add('on'); $('listen').hidden = false;
    recordSegment();
  }
  function stopListening() {
    voice.on = false; $('mic').classList.remove('on'); $('listen').hidden = true;
    try { if (voice.rec && voice.rec.state !== 'inactive') voice.rec.stop(); } catch (e) {}
    if (voice.stream) { for (const t of voice.stream.getTracks()) t.stop(); voice.stream = null; }
  }
  function recordSegment() {
    if (!voice.on || !voice.stream) return;
    const chunks = [];
    const rec = new MediaRecorder(voice.stream, voice.mime ? { mimeType: voice.mime } : undefined);
    voice.rec = rec;
    rec.ondataavailable = e => { if (e.data && e.data.size) chunks.push(e.data); };
    rec.onstop = () => {
      if (voice.on) recordSegment();                                   // next segment right away
      const blob = new Blob(chunks, { type: rec.mimeType || voice.mime || 'audio/webm' });
      if (blob.size > 2000 && !voice.speaking) hear(blob);           // tiny blobs are silence
    };
    rec.start();
    setTimeout(() => { try { if (rec.state !== 'inactive') rec.stop(); } catch (e) {} }, 3000);
  }
  async function hear(blob) {
    if (voice.busy || !$('finish').hidden) return;
    voice.busy = true;
    try {
      const fd = new FormData();
      fd.append('audio', blob, 'segment.' + (blob.type.includes('mp4') ? 'mp4' : blob.type.includes('ogg') ? 'ogg' : 'webm'));
      fd.append('procedure_id', state.pid); fd.append('step_order', String(state.spec.steps[state.i].order));
      const r = await fetch('/guide/hear', { method: 'POST', body: fd });
      if (!r.ok) return;
      const res = await r.json();
      if (!res.transcript) return;
      $('listen').textContent = `Heard: “${res.transcript}”`;
      setTimeout(() => { if (voice.on) $('listen').textContent = 'Listening… say "done" or ask a question'; }, 4000);
      if (res.intent === 'done') checkAndAdvance('voice');
      else if (res.intent === 'question') askQuestion(res.transcript);
    } catch (e) { /* a dropped segment is fine; the next one is 3 s away */ }
    finally { voice.busy = false; }
  }
  async function askQuestion(question) {
    const s = state.spec.steps[state.i], rec = state.results[state.results.length - 1];
    status(`Thinking about: “${question}”`, true);
    try {
      const r = await fetch('/guide/ask', { method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ procedure_id: state.pid, step_order: s.order, question, image: grabFrame(), session_id: state.sessionId }) });
      const res = await r.json();
      (rec.questions = rec.questions || []).push({ question, answer: res.answer, wall_ms: res.wall_ms });
      toast(res.answer); speak(res.answer);
      $('hint').textContent = res.answer; $('hint').className = 'hint';
    } catch (e) { toast('Could not answer that one.', true); }
  }
  function speak(text) {
    if (MUTE || !('speechSynthesis' in window) || !text) return;
    try {
      const u = new SpeechSynthesisUtterance(text);
      voice.speaking = true; u.onend = u.onerror = () => { voice.speaking = false; };
      speechSynthesis.cancel(); speechSynthesis.speak(u);
    } catch (e) { voice.speaking = false; }
  }
  $('mic').onclick = () => voice.on ? stopListening() : startListening();

  $('done').onclick = () => checkAndAdvance('tap');
  $('again').onclick = () => { $('finish').hidden = true; state.i = 0; state.results = []; state.rtt = []; showStep(); };
  $('home').onclick = () => { stopListening(); stopSource(); history.replaceState(null, '', location.pathname + (MUTE ? '?mute=1' : '')); landing(); };
  $('back').onclick = () => { if (state.timer) { clearInterval(state.timer); state.timer = null; } stopListening(); stopSource(); history.replaceState(null, '', location.pathname + (MUTE ? '?mute=1' : '')); landing(); };
  window.addEventListener('resize', () => { if (state.lastResult) render(state.lastResult); else clearOverlay(); });
  document.addEventListener('keydown', e => { if (e.key === 'Enter' && !$('guide').hidden && $('finish').hidden) checkAndAdvance('key'); });

  const pid = q.get('procedure');
  if (pid) start(pid, q.get('video')); else landing();
})();
