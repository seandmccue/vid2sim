import React, { useEffect, useState } from 'react'

export default function RunPanel({ id, status, health, onRun }) {
  const running = status?.state === 'running'
  const [tick, setTick] = useState(0)
  useEffect(() => { if (!running) return; const t = setInterval(() => setTick(x => x + 1), 250); return () => clearInterval(t) }, [running])
  const elapsed = status?.elapsed_s ?? 0
  const log = status?.log || []
  return (
    <section className="run">
      <div className="row">
        <h2>{id}</h2>
        <div className="clock" title="wall clock, upload to StreamingAssets">{running ? '⏱ ' : ''}{fmt(elapsed)}</div>
        <div className="spacer" />
        <button className="primary" disabled={running} onClick={() => onRun({})}>{running ? 'Running…' : status?.has_spec || status?.card ? 'Re-run pipeline' : 'Run pipeline'}</button>
        {health && !health.has_key && <button disabled={running} onClick={() => onRun({ mock: true })} title="no API key on server">Run (mock)</button>}
        <button disabled={running} onClick={() => onRun({ skip_segment: true, skip_generate: true })} title="Spark only, no SAM / Muse">Quick run</button>
      </div>
      <div className={`state-badge ${status?.state || 'idle'}`}>{status?.state || 'idle'}</div>
      {status?.error && <div className="err">{status.error}</div>}
      {log.length > 0 && (
        <pre className="log">{log.slice(-12).join('\n')}</pre>
      )}
    </section>
  )
}

const fmt = (s) => `${Math.floor(s / 60)}:${String(Math.floor(s % 60)).padStart(2, '0')}`
