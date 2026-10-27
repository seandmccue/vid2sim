# vid2sim — submission write-up (draft for the Cerebral Valley form)

> Fill the bracketed numbers from `data/procedures/*/metrics.json` and `metrics/runs.csv` on Nov 3. Keep every claim measured.

## One line

Film a procedure once on a phone; get a hands-first Meta Quest training module back in minutes, for under two dollars.

## The number first

A single-procedure VR training module takes Frame Sixty **[X] weeks** and **$[Y]** today: a studio scripts the steps, models the parts, hand-wires the interactions, QAs. vid2sim did it in **[9.6] minutes** for **$[1.16]** on a real clip, and **[N] minutes** for **$[M]** on a second, unrelated machine with zero code changes.

## What it does

1. **Capture.** A trainer films the procedure once, narrating. 30 s to 3 min, phone, one take.
2. **Understand.** Muse Voice Transcribe turns the narration into a frame-stamped transcript. Muse Spark 1.3 reads the frames and the transcript together and returns the procedure as structured JSON against a frozen schema: ordered steps, the part each step touches, the action from a closed vocabulary, safety flags, checks, failure text, a coaching line, and the frame range. A second Spark pass verifies every step against the narration and flags the ones it can't support.
3. **Segment.** SAM 3.1 finds and tracks every part across its step's frames from a text prompt. When the text prompt misses, Spark proposes a box on the keyframe and SAM re-runs from it. Every part records how it was found.
4. **Build.** A data-driven Unity engine reads the spec: one object per part, sized from SAM's box, textured by Muse Image from the keyframe crop, with the Meta Interaction SDK component the action needs. Muse Code generates a script only for verbs the engine doesn't know, and every generated script passes a headless Unity compile before it's referenced.
5. **Review.** The trainer sees the steps and the masks in a dashboard, renames a part, reorders a step, draws a box where SAM missed, and hits Rebuild. The spec hot-loads into the running headset build.
6. **Train.** Hands-first on Quest. Step order, time per step, wrong-part touches and safety skips are scored. Skipping a safety step fails the attempt, which is what industrial clients ask for.

**Spark understands, SAM segments, Muse Code builds, Muse Image paints.**

## Use of Meta AI tooling

| Model | Role | What we learned using it |
| --- | --- | --- |
| Muse Spark 1.3 | Video + transcript → structured procedure; verify pass; box proposals; writes long-tail scripts when Muse Code is absent | Reasoning shares the output budget. 6000 tokens returned nothing on a 79 s clip; 16000 with one retry is the floor. Reads burned-in captions when there is no narration. |
| SAM 3.1 | Per-part detection, masks and tracking from a noun phrase | One concept per request; zero matches is a branch, not an error. A mask raster covers only its box bounds. Frame-accurate trims keep a procedure under a dollar. |
| Muse Voice Transcribe | Narration → turns with timestamps | Matches a clip's own subtitle track nearly word for word. Spark 1.3's audio path is documented as degraded, so narration never goes through Spark. |
| Muse Image | Part textures from keyframe crops | Turns a 100-pixel crop into a usable flat-lit texture; capped at 1024 px for the APK. |
| Muse Code | Generates `IStepAction` MonoBehaviours for verbs outside the engine's vocabulary | Gated by a headless Unity compile; generated files are guarded so the project compiles with or without the SDK. |

Optional: the verify pass can run on Muse Spark Open via Baseten or Modal with three env vars (`VERIFY_*`), the open-weight bonus line.

## Agentic behaviour (where it's true)

The pipeline extracts, verifies its own extraction against the transcript, detects low-confidence parts and retries them with a different prompt type, and gates generated code behind a compiler before trusting it. The human is in the loop at exactly one place, the review step, and that step is a feature, not an apology.

## Measured

| Procedure | Upload → playable | API cost | Steps correct vs trainer list | Parts: text / box / manual |
| --- | --- | --- | --- | --- |
| [procedure #1, own footage] | [ ] min | $[ ] | [ ]/[ ] | [ ] / [ ] / [ ] |
| [procedure #2, own footage] | [ ] min | $[ ] | [ ]/[ ] | [ ] / [ ] / [ ] |

Development runs on CC BY test clips (water filter install, PC graphics-card swap) are in the repo README with attribution; they are not part of the demo.

## Risks we designed around

| Risk | Mitigation |
| --- | --- |
| Spark merges or misreads steps | Fixed schema with structured output; verify pass against the transcript; dashboard review |
| SAM's text prompt misses an industrial part | Spark box fallback with forward propagation; trainer draws a box; `source` field makes the miss rate a metric |
| Generated code doesn't compile | Engine is data-driven; Muse Code writes only the long tail; headless compile gate |
| Parts have no 3D geometry | Representative primitives sized from SAM's box, textured from the crop. Photogrammetry is the roadmap |
| Hand tracking on small parts | Every collider ≥ 4 cm, generous snap zones, poke and grab only |

## What's new vs prior work

Everything in the repo was written during the hackathon from an empty repo on Oct 26 (see the PR trail). `prior/` is empty.

## What's next

Photogrammetry for real part geometry; Muse Wonder for a trainee agent that remembers past attempts; Voice Transcribe realtime for "which one is the bypass valve?" in the headset.

## Links

Repo: https://github.com/seandmccue/vid2sim · Demo video: [ ] · Team: Sean McCue (Frame Sixty), [Dev]
