import React, { useEffect, useMemo, useState } from 'react'
import { ACTIONS, INTERACTIONS, api } from '../api.js'
import Overlay from './Overlay.jsx'

const clone = (x) => JSON.parse(JSON.stringify(x))

const SPATIAL = /\b(left|right|top|bottom|upper|lower|front|back|rear|first|second|third|fourth|nearest|farthest|middle|centre|center|above|below|beside|next to)\b/i
const spatial = phrase => { const m = SPATIAL.exec(phrase || ''); return m ? m[1] : null }

export default function SpecEditor({ id, spec: initial, review, bundle, notify, onSaved, webBuild }) {
  const [spec, setSpec] = useState(() => clone(initial))
  const [dirty, setDirty] = useState(false)
  const [errors, setErrors] = useState([])
  const [partSel, setPartSel] = useState(initial.parts[0]?.id)
  const [saving, setSaving] = useState(false)
  const [buildLog, setBuildLog] = useState(null)

  useEffect(() => { setSpec(clone(initial)); setDirty(false); setErrors([]); setPartSel(initial.parts[0]?.id) }, [initial])

  const flagged = useMemo(() => Object.fromEntries((review?.steps || []).filter(r => !r.supported).map(r => [r.order, r])), [review])
  const errFor = (path) => errors.filter(e => e.path.startsWith(path)).map(e => e.message).join('; ')

  const update = (fn) => setSpec(s => { const n = clone(s); fn(n); return n }) || setDirty(true)
  const move = (i, d) => update(n => { const j = i + d; if (j < 0 || j >= n.steps.length) return; [n.steps[i], n.steps[j]] = [n.steps[j], n.steps[i]]; n.steps.forEach((s, k) => s.order = k + 1) })
  const removeStep = (i) => update(n => { n.steps.splice(i, 1); n.steps.forEach((s, k) => s.order = k + 1) })
  const addStep = () => update(n => n.steps.push({ order: n.steps.length + 1, part_id: n.parts[0]?.id || '', action: 'grab', instruction: 'New step', start_frame: 0, end_frame: 0, check: '', failure: '', safety: false, coach: '' }))

  const save = async () => {
    setSaving(true); setErrors([])
    try {
      const res = await api.saveSpec(id, spec)
      setDirty(false); onSaved(clone(spec))
      notify(res.needs_resegment?.length ? `Saved. Re-run to re-segment: ${res.needs_resegment.join(', ')}` : 'Saved and repackaged for Unity')
    } catch (e) {
      if (e.status === 422 && e.data?.errors) { setErrors(e.data.errors); notify('Fix the highlighted fields', 'err') } else notify(e.message, 'err')
    } finally { setSaving(false) }
  }

  const build = async () => {
    setBuildLog(['building…'])
    try { const r = await api.build(id, { push: true }); setBuildLog(r.log); notify(r.pushed ? 'Pushed to headset; sim hot-reloads' : 'Packaged for Unity') }
    catch (e) { setBuildLog([e.message]); notify(e.message, 'err') }
  }

  const part = spec.parts.find(p => p.id === partSel)

  return (
    <section className="editor">
      <div className="row head">
        <input className="title" value={spec.procedure.title} onChange={e => update(n => n.procedure.title = e.target.value)} />
        <div className="spacer" />
        {review?.flagged_count > 0 && <span className="flag">⚠ {review.flagged_count} step(s) not supported by the {review.evidence_mode === "narration+video" ? "narration or video" : "narration"}</span>}
        <button onClick={save} disabled={!dirty || saving} className="primary">{saving ? 'Saving…' : dirty ? 'Save + repackage' : 'Saved'}</button>
        <button onClick={() => window.open(`/guide/?procedure=${id}`, '_blank')} disabled={dirty} title="guide a trainee on the real machine through a phone camera">Guide (phone)</button>
        <button onClick={() => window.open(`/guide/verify.html?procedure=${id}`, '_blank')} disabled={dirty} title="grade a trainee's video against this spec">Verify a video</button>
      </div>
      {buildLog && <pre className="log small">{buildLog.join('\n')}</pre>}

      <div className="cols">
        <div className="steps">
          <h3>Steps</h3>
          {spec.steps.map((s, i) => (
            <div key={i} className={`step ${flagged[s.order] ? 'flagged' : ''} ${errFor(`/steps/${i}`) ? 'bad' : ''}`}>
              <div className="row">
                <span className="num">{s.order}</span>
                <select value={s.part_id} onChange={e => update(n => n.steps[i].part_id = e.target.value)}>
                  {spec.parts.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                </select>
                <select value={s.action} onChange={e => update(n => n.steps[i].action = e.target.value)}>
                  {ACTIONS.map(a => <option key={a}>{a}</option>)}
                </select>
                {s.custom_action && <span className="pill" title="Muse Code generates this verb">⚙ {s.custom_action}</span>}
                <label className="chk"><input type="checkbox" checked={s.safety} onChange={e => update(n => n.steps[i].safety = e.target.checked)} /> safety</label>
                <div className="spacer" />
                <span className="frames">f{s.start_frame}–{s.end_frame}</span>
                <button className="icon" onClick={() => move(i, -1)}>↑</button>
                <button className="icon" onClick={() => move(i, 1)}>↓</button>
                <button className="icon" onClick={() => removeStep(i)}>✕</button>
              </div>
              <div className="row">
                <img className="card" src={api.fileUrl(id, `bundle/cards/step_${String(s.order).padStart(2, '0')}.png`)} alt="" onError={e => { e.target.style.display = 'none' }} title="Muse Image step card" />
                <input className="instr" value={s.instruction} onChange={e => update(n => n.steps[i].instruction = e.target.value)} />
              </div>
              <div className="row small">
                <input placeholder="check" value={s.check} onChange={e => update(n => n.steps[i].check = e.target.value)} />
                <input placeholder="coach line" value={s.coach} onChange={e => update(n => n.steps[i].coach = e.target.value)} />
              </div>
              {flagged[s.order] && <div className="why">Narration: “{flagged[s.order].evidence}” {flagged[s.order].suggested_fix && <button className="link" onClick={() => update(n => n.steps[i].instruction = flagged[s.order].suggested_fix)}>apply fix</button>}</div>}
              {errFor(`/steps/${i}`) && <div className="err">{errFor(`/steps/${i}`)}</div>}
            </div>
          ))}
          <button onClick={addStep}>+ step</button>
        </div>

        <div className="parts">
          <h3>Parts</h3>
          <ul className="partlist">
            {spec.parts.map((p, i) => (
              <li key={p.id} className={`${p.id === partSel ? 'sel' : ''} ${errFor(`/parts/${i}`) ? 'bad' : ''}`} onClick={() => setPartSel(p.id)}>
                <img src={api.fileUrl(id, `bundle/crops/${p.id}.png`)} alt="" onError={e => e.target.style.visibility = 'hidden'} />
                <div>
                  <input value={p.name} onChange={e => update(n => n.parts[i].name = e.target.value)} onClick={e => e.stopPropagation()} />
                  <div className="m"><span className={`src ${p.source}`}>{p.source}</span> c={p.confidence.toFixed(2)} · {p.size_hint_m} m</div>
                </div>
              </li>
            ))}
          </ul>
          {part && (
            <div className="partdetail">
              <label>SAM prompt <input value={part.prompt_phrase} onChange={e => update(n => { n.parts.find(p => p.id === part.id).prompt_phrase = e.target.value })} className={spatial(part.prompt_phrase) ? 'warn' : ''} /></label>
              {spatial(part.prompt_phrase) && <div className="why">SAM ignores position words ("{spatial(part.prompt_phrase)}"). Name the object by colour, shape or material instead: "red rocker switch", "brass drain valve".</div>}
              <label>Interaction <select value={part.interaction} onChange={e => update(n => { n.parts.find(p => p.id === part.id).interaction = e.target.value })}>{INTERACTIONS.map(x => <option key={x}>{x}</option>)}</select></label>
              <label>Size (m) <input type="number" step="0.01" value={part.size_hint_m} onChange={e => update(n => { n.parts.find(p => p.id === part.id).size_hint_m = parseFloat(e.target.value) || 0.04 })} /></label>
              <Overlay id={id} spec={spec} part={part} track={bundle.find(b => b.id === part.id)} onBox={(box, frame) => update(n => { const p = n.parts.find(p => p.id === part.id); p.box = box; p.keyframe = frame; p.source = 'manual'; p.confidence = 1 })} />
            </div>
          )}
        </div>
      </div>
    </section>
  )
}
