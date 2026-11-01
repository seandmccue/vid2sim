# Handoff, Oct 31 evening (after the pivot)

Everything in the Dev column of the pivot plan is on `main`, plus Verify, the archive move, the launcher, the
metrics card and the docs. What is left needs Sean: footage, hosting on your own domain, the stranger test, the
video, the submission.

## Running it

```bash
scripts/serve.sh          # backend :8000 + Cloudflare quick tunnel; prints the https link and a QR (logs/guide-qr.png)
scripts/serve.sh stop
```

The quick-tunnel URL changes every time the tunnel restarts. For a judge URL that lasts the week, run a named
Cloudflare tunnel (or any HTTPS reverse proxy) to 127.0.0.1:8000 and start the backend with
`TUNNEL_URL=https://your.domain scripts/serve.sh`. iOS Safari needs HTTPS for the camera; everything else works on http.

## Keeping it up for the week

```bash
nohup scripts/keepalive.sh > logs/keepalive.log 2>&1 &     # restarts backend/tunnel if they die, keeps the Mac awake
cat logs/public-url.txt                                     # the current public URL
```

With a named tunnel on your domain: `TUNNEL_URL=https://apprentice.yourdomain scripts/keepalive.sh`.

## Hands-free

Guide now checks the step by itself: once the part is in view, Spark looks at a frame every 4 s and advances on a yes
(capped at six silent checks per step, so a step that is never done costs about a cent). Done, Enter or saying "done"
asks for the check right now. `?auto=0` turns it off.

## Your list (from the pivot plan)

1. **Film tonight.** Desk demo expert take (60 s): close the lid, unplug the charger, cap the bottle, mouse on the
   laptop. Second machine expert take. Two compressor trainee takes: one correct, one that skips the power-off.
   Faststart them (`ffmpeg -i in.mov -movflags +faststart -c copy out.mp4`). Write each ground-truth step list before
   running the pipeline.
2. **Run each expert video** through the dashboard (upload → run) and fix phrases: anything flagged amber in the SAM
   prompt field has a position word; name the object by colour, shape or material. Write every `check` as a visible
   binary state ("switch is in the OFF position"), because Spark judges from that text and one frame.
3. **Replace the stand-ins.** Done for the desk: `data/procedures/desk-demo` is now your Nov 1 laptop take (two steps,
   SAM 0.99 on the laptop, Spark 6/6 on held-out frames). Add the compressor as `compressor-filter-swap` and your second machine to
   `data/guide.json` under `featured` (the forklift on sample footage is there now and can stay as a third).
4. **Sample footage per procedure.** Drop a `sample.mp4` in the procedure folder (480p is plenty) and "Watch on
   sample footage" appears for it.
5. **Verify demo.** Upload the wrong compressor take at `/guide/verify.html`; the report should fail on the skipped
   power-off. The forklift pass/fail reports are already saved (`/guide/verify.html?session=forklift-wrong-take`).
6. **Stranger test (Monday).** Hand the link to someone who has not seen it; they should finish the desk demo without
   help. Watch where they hesitate and tell me.
7. **Video and submission.** `docs/VIDEO.md` is the shot list; `docs/SUBMISSION.md` is the page text with the measured
   numbers; `scripts/metrics_card.py` prints the card for the 2:30 shot. Post the 2:45 clip on X with the QR.

## Where things are

- Guide page: `backend/static/guide/` (`index.html`, `guide.js`, `verify.html`); backend logic `backend/guide.py`,
  routes in `backend/app.py` (`/guide/find`, `/guide/check`, `/guide/hear`, `/guide/ask`, `/guide/sessions`, `/guide/qr.svg`,
  `/verify/...`). Verify pipeline: `backend/pipeline/verify.py`.
- Results: `data/sessions/*.json` (Guide and Verify), `data/verify/<session>/` (uploaded video, thumbs, status).
- Every API call logs to `metrics/runs.csv`; `scripts/metrics_card.py` reads it.
- The VR sim: `archive/unity`, not part of the demo.

## Numbers so far (all measured, Oct 31)

SAM on a live frame: median about 0.9 s, $0.0025; laptop / bottle / mouse at 0.95+; forklift parts 3/8 on first
phrases (thin parts miss). Spark step check: 7/7 on desk photos, 1.6 to 2.4 s, about $0.002. Voice: "done" in 0.8 s,
a spoken answer in about 2.3 s. Verify: forklift full take PASS 100, battery-check-cut take FAIL on safety, about
60 s and $0.08 each. Total API spend for the hackathon so far: about $13.
