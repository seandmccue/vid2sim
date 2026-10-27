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
  const dragRef = useRef(null)

  const toFrame = (e) => {
    const r = ref.current.getBoundingClientRect()
    const x = Math.round((e.clientX - r.left) / r.width * W), y = Math.round((e.clientY - r.top) / r.height * H)
    return [Math.min(Math.max(x, 0), W - 1), Math.min(Math.max(y, 0), H - 1)]   // clamp: a drag past the edge still counts
  }

  // Track the drag on the window: a mouse-up just outside the frame must still commit the box,
  // otherwise the overlay is left showing a rectangle that was never saved.
  useEffect(() => {
    if (!drag) return
    const move = (e) => { const [x, y] = toFrame(e); setDrag(d => d ? [d[0], d[1], x, y] : d) }
    const up = () => {
      const d = dragRef.current
      setDrag(null)
      if (d && Math.abs(d[2] - d[0]) > 4 && Math.abs(d[3] - d[1]) > 4)
        onBox([Math.min(d[0], d[2]), Math.min(d[1], d[3]), Math.max(d[0], d[2]), Math.max(d[1], d[3])], cur.frame)
    }
    window.addEventListener('mousemove', move)
    window.addEventListener('mouseup', up)
    return () => { window.removeEventListener('mousemove', move); window.removeEventListener('mouseup', up) }
  }, [!!drag, cur.frame])
  useEffect(() => { dragRef.current = drag }, [drag])

  const box = drag ? [Math.min(drag[0], drag[2]), Math.min(drag[1], drag[3]), Math.max(drag[0], drag[2]), Math.max(drag[1], drag[3])] : cur.box

  return (
    <div className="overlay">
      <div className="frame" ref={ref} style={{ aspectRatio: `${W}/${H}` }}
        onMouseDown={e => { e.preventDefault(); const [x, y] = toFrame(e); setDrag([x, y, x, y]) }}>
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
