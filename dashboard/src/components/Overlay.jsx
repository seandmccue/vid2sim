import React, { useEffect, useRef, useState } from 'react'
import { api } from '../api.js'

/**
 * Keyframe with SAM's box and mask drawn over it, a scrubber across the tracked frames, and
 * drag-to-draw for the trainer to tap a box when SAM missed (source becomes "manual").
 */
export default function Overlay({ id, spec, part, track, onBox }) {
  const frames = track?.track?.length ? track.track : [{ frame: part.keyframe, box: part.box, c: part.confidence }]
  const [idx, setIdx] = useState(() => Math.max(0, frames.findIndex(f => f.frame === part.keyframe)))
  useEffect(() => { setIdx(Math.max(0, frames.findIndex(f => f.frame === part.keyframe))) }, [part.id, part.keyframe])
  const cur = frames[Math.min(idx, frames.length - 1)]
  const W = spec.procedure.width, H = spec.procedure.height
  const mask = track?.masks?.find(m => m.includes(`_f${String(cur.frame).padStart(6, '0')}_`))
  const [drag, setDrag] = useState(null)
  const ref = useRef()

  const toFrame = (e) => {
    const r = ref.current.getBoundingClientRect()
    return [Math.round((e.clientX - r.left) / r.width * W), Math.round((e.clientY - r.top) / r.height * H)]
  }
  const box = drag ? [Math.min(drag[0], drag[2]), Math.min(drag[1], drag[3]), Math.max(drag[0], drag[2]), Math.max(drag[1], drag[3])] : cur.box

  return (
    <div className="overlay">
      <div className="frame" ref={ref} style={{ aspectRatio: `${W}/${H}` }}
        onMouseDown={e => { const [x, y] = toFrame(e); setDrag([x, y, x, y]) }}
        onMouseMove={e => { if (drag) { const [x, y] = toFrame(e); setDrag(d => [d[0], d[1], x, y]) } }}
        onMouseUp={() => { if (drag && Math.abs(drag[2] - drag[0]) > 4 && Math.abs(drag[3] - drag[1]) > 4) onBox([Math.min(drag[0], drag[2]), Math.min(drag[1], drag[3]), Math.max(drag[0], drag[2]) - 1, Math.max(drag[1], drag[3]) - 1].map(v => Math.max(0, v)), cur.frame); setDrag(null) }}>
        <img src={api.frameUrl(id, cur.frame)} alt={`frame ${cur.frame}`} draggable={false} />
        {mask && <div className="mask" style={{ WebkitMaskImage: `url(${api.fileUrl(id, mask)})`, maskImage: `url(${api.fileUrl(id, mask)})` }} />}
        {box && <div className="box" style={{ left: `${box[0] / W * 100}%`, top: `${box[1] / H * 100}%`, width: `${(box[2] - box[0] + 1) / W * 100}%`, height: `${(box[3] - box[1] + 1) / H * 100}%` }} />}
      </div>
      <div className="row small">
        <input type="range" min={0} max={frames.length - 1} value={Math.min(idx, frames.length - 1)} onChange={e => setIdx(+e.target.value)} />
        <span className="muted">frame {cur.frame}{cur.c != null ? ` · c=${Number(cur.c).toFixed(2)}` : ''} · {frames.length} tracked</span>
      </div>
      <div className="muted tiny">Drag on the frame to draw a box when SAM missed the part.</div>
    </div>
  )
}
