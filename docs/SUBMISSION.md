# Apprentice — submission page

> Apprentice turns one phone video from the person who knows how into a guide anyone can follow on the real machine. Point your phone at it; the AI finds each part, checks your work, and answers questions. Film yourself doing it and Apprentice grades you against the expert.
>
> **Try it (30 seconds):** open [link] on your phone, choose Desk demo, point at your desk. No camera? Choose "Watch on sample footage" for the forklift.
>
> **Built with:** Muse Spark 1.3 (procedure extraction from video, step verification from a frame, Q&A, grading a trainee video), SAM 3.1 (part detection on the expert video and live on the trainee's camera), Muse Voice Transcribe (narration, voice commands, questions). All through the Meta Model API.
>
> **Built during the hackathon:** everything in this repo. The team's prior work is VR training sims delivered by hand; none of that code is here.

**Spark understands, SAM finds, Spark checks, Voice Transcribe listens.**

## The problem

Every plant has one person who knows how. When they are off shift, the procedure is a laminated sheet or a phone call. Training sims take weeks to build per procedure and cannot keep up with the equipment on the floor.

## What Apprentice does

1. **Capture.** The expert films the procedure once on a phone, narrating. One take.
2. **Understand.** Muse Voice Transcribe stamps the narration to frames. Muse Spark 1.3 reads the video plus the transcript and returns the procedure as structured JSON against a frozen schema: ordered steps, the part each step touches, the action, a visible `check` for when it is done, failure text, safety flags, a coaching line, frame ranges. A second Spark pass flags any step the narration does not support. SAM 3.1 finds each part on the expert video from a text phrase, with a Spark box fallback.
3. **Review.** The trainer fixes a phrase or a step in the dashboard. The frozen schema is the contract.
4. **Guide.** The trainee points a phone at the real machine. Every second a frame goes to SAM 3.1 with the step's phrase and the real part gets a box. Done (tapped or said) sends the frame to Spark with the step's check: yes advances, no coaches with the expert's line, unclear asks for a closer look. A question into the mic is transcribed and answered by Spark in one sentence with the procedure and the frame as context.
5. **Verify.** The trainee films themselves. The same pipeline runs with the expert's steps in the prompt; the report marks each step done, missed or out of order, with a thumbnail from the trainee's video. A skipped safety step fails the attempt. Printable, shareable.

## Measured

Filled from `scripts/metrics_card.py` (every number comes from `metrics/runs.csv` and the saved sessions; nothing typed by hand):

| | |
| --- | --- |
| Expert video → spec | 3 to 24 minutes per procedure, $0.37 to $3.67 (five public-domain test films plus the desk demo); 10/12 steps right on the forklift film against a trainer's list |
| SAM on live phone frames | median under a second per frame, $0.0025 a frame; laptop / bottle / mouse found at 0.95+ on desk photos; thin parts (chains, hoses) miss on a single frame and get a phrase fix |
| Spark step check | 7/7 correct on desk photos (open laptop is not closed; capped bottle is capped; a frame with no laptop comes back unclear), 1.6 to 2.4 s, about $0.002 |
| Voice | "done" recognised in 0.8 s; a question answered and spoken in about 2.3 s |
| Verify | the full forklift inspection passes at 100; the same film with the battery check cut out fails on the skipped safety step; about 60 s and $0.08 per video |
| Total API spend for the whole hackathon | under $15 |

## Known limits, and what the UI does about them

| Limit | What happens | On screen |
| --- | --- | --- |
| Two identical parts | SAM returns both | Both boxed: "2 found, either works". Never a guess |
| Part not visible from this angle | Zero matches | "Finding the X…" with the expert's keyframe crop, then "try moving closer or to the side" |
| 1 to 2 s latency | Box lags a moving phone | Boxes cleared on every response; nobody calls it AR tracking |
| Spark check wrong from one frame | False pass or fail | Checks are visible binary states; one override per step, logged and shown in Verify |

## Links

- Repo: https://github.com/seandmccue/vid2sim (Apprentice)
- Live demo: [link from `scripts/serve.sh`] · Verify report example: `/guide/verify.html?session=forklift-wrong-take`
- Video: [link]
