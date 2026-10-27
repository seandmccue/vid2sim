# 3-minute demo video: script and shot list

Final-round judges watch this live on Nov 9 and rank on Impact 30 / Meta tooling 25 / Demo 25 / Technical 20.
Impact forms in the first 15 seconds. The chain sentence is said out loud once. No architecture slides.

| Time | On screen | Voice-over (say it, don't read it) |
| --- | --- | --- |
| 0:00 | Black. One line of text: "A VR training module takes us [X] weeks. This takes [Y] minutes." | "At Frame Sixty we build VR training for factories and hospitals. One module takes us [X] weeks and [$Y]. Watch this." |
| 0:12 | Sean in the shop, phone in hand, filming the compressor filter swap. Narrating naturally. Visible machine, visible hands. | (live sound from the phone) "Switch it off first… open the drain to bleed pressure…" |
| 0:40 | Dashboard. Drag the file in. Wall clock starts. Status log scrolls: transcribe, extract, segment. | "The narration goes through Voice Transcribe. Spark 1.3 watches the frames and writes the procedure as a spec a trainer would recognise." |
| 0:58 | Steps appear. Then the keyframe with SAM masks drawn over the parts, scrubber moving. One part tagged "box", one tagged "text". | "SAM 3.1 finds every part and tracks it through the clip. When the text prompt misses, Spark proposes a box and SAM runs again." |
| 1:20 | One step is flagged yellow: "not supported by narration". Click apply fix. Rename a part. Draw a box on a missed part. Save. Rebuild → Quest. | "Every AI pipeline guesses wrong sometimes. The trainer fixes it in ten seconds and rebuilds. That's what makes this a product." |
| 1:40 | Quest capture, hands only. Coach panel, step 1. Trainee grabs the wrong part: panel flashes, coach speaks. Then the right one. Timer, step counter. Procedure complete, score. | "Hands-first on Quest. Order, time per step, wrong-part touches. Skip a safety step and the attempt fails, because that's what our clients ask for." |
| 2:15 | Second machine (breaker panel / different shape). Phone clip → dashboard → wall clock → Quest, fast cut, no edits made. | "Different machine, same pipeline, zero code changes. [N] minutes." |
| 2:40 | Metrics card: minutes per module, cost per module, steps vs trainer list, parts found by text vs box. | "Measured, not claimed. Spark understands, SAM segments, Muse Code builds, Muse Image paints." |
| 2:52 | Repo URL, team, one line of what's next. | "Any plant can train on its own machines without a studio in the loop. That's the frontier we're pushing." |

## Capture checklist

- Dashboard: record at 1080p with the wall clock visible the whole time; no cuts inside the run.
- Quest: use the headset's own recording (Share → Record video) so hands are tracked in the capture; 16:9 crop is fine.
- Keep the trainer fix real: let the pipeline make a genuine mistake and fix it on camera. Don't stage a fake one.
- Second machine: film it on Oct 30 and never hand-edit its spec.
- Metrics card: `python backend/metrics_card.py` writes `docs/metrics-card.png` from both procedures' metrics.json.

## X post (Nov 4, right after submitting)

> Film a procedure once on a phone. Get a hands-first Quest training module back in minutes. Spark understands, SAM segments, Muse Code builds, Muse Image paints. Built in 10 days for the Meta AI hackathon. [video] [repo] #MetaAIHackathon

Ask the Discord to vote in the gallery in the same hour.
