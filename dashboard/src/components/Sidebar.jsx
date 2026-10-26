import React, { useRef, useState } from 'react'
import { api } from '../api.js'

export default function Sidebar({ procs, selected, onSelect, onUploaded, health }) {
  const [title, setTitle] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState(null)
  const fileRef = useRef()

  const upload = async () => {
    const f = fileRef.current?.files?.[0]
    if (!f) { setErr('Pick an MP4 first'); return }
    setBusy(true); setErr(null)
    try { onUploaded(await api.upload(f, title)); setTitle(''); fileRef.current.value = '' }
    catch (e) { setErr(e.message) } finally { setBusy(false) }
  }

  return (
    <aside>
      <div className="brand">vid2sim <span className={`dot ${health?.ok ? 'on' : 'off'}`} title={health?.ok ? 'backend up' : 'backend down'} /></div>
      <div className="upload">
        <input ref={fileRef} type="file" accept="video/mp4,video/quicktime" />
        <input placeholder="Title (e.g. Replace the intake filter)" value={title} onChange={e => setTitle(e.target.value)} />
        <button onClick={upload} disabled={busy}>{busy ? 'Uploading…' : 'Upload video'}</button>
        {err && <div className="err">{err}</div>}
      </div>
      <h3>Procedures</h3>
      <ul className="proclist">
        {procs.map(p => (
          <li key={p.id} className={p.id === selected ? 'sel' : ''} onClick={() => onSelect(p.id)}>
            <div className="t">{p.title}</div>
            <div className="m">{p.steps ? `${p.steps} steps · ${p.parts} parts` : 'no spec yet'} · <span className={`state ${p.state}`}>{p.state}</span></div>
          </li>
        ))}
        {procs.length === 0 && <li className="muted">none yet</li>}
      </ul>
    </aside>
  )
}
