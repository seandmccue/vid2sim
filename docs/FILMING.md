# Filming your own footage (both procedures)

The pipeline is tuned to what it has seen: a phone, a bench, one take, narration. Here is what makes the footage pipeline-ready the first time.

**Setup**
- Phone, landscape, 1080p30 (or 24), locked exposure if the app allows it. Tripod or a steady prop; no walking.
- Camera above and slightly in front of the bench, parts in the lower two thirds of frame. Top-down angles made SAM's boxes tightest on the test clips.
- A tape measure or your hand flat on the bench in the first second (the size hint uses frame width ≈ 0.6 m by default; `--reference-width-m` overrides it).
- Plain background. Busy backgrounds gave SAM a second object to track.

**Narration (this matters most)**
- Say the part name with an attribute and the action, as you do it: "I'm pressing the red power switch off." "Now I unscrew the black filter cap counter-clockwise."
- One sentence per step, a short pause between steps. The transcript's turns become the step frame ranges.
- Say "this is a safety step" where it is. Spark sets the flag from it.
- Don't say "this one" or "that". Name it.

**Procedure**
- Six steps or fewer, 60 to 120 seconds, one take, no cuts. A second take from another angle as backup.
- Parts visible and unoccluded for at least a second each at some point; that frame becomes the texture crop.

**After filming**
```bash
ffmpeg -i raw.mp4 -c copy -movflags +faststart data/procedures/<id>/p.mp4
```
Write `data/procedures/<id>/ground_truth.json` (order, part, action, text) before running anything. Then:
```bash
.venv/bin/python backend/run.py data/procedures/<id>/p.mp4 --title "<title>" --ground-truth data/procedures/<id>/ground_truth.json
```
