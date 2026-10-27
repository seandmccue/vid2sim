import React, { useCallback, useEffect, useState } from 'react'
import { api } from './api.js'
import Sidebar from './components/Sidebar.jsx'
import RunPanel from './components/RunPanel.jsx'
import SpecEditor from './components/SpecEditor.jsx'
import MetricsCard from './components/MetricsCard.jsx'

export default function App() {
  const [health, setHealth] = useState(null)
  const [procs, setProcs] = useState([])
  const [selected, setSelected] = useState(null)
  const [status, setStatus] = useState(null)
  const [spec, setSpec] = useState(null)
  const [review, setReview] = useState(null)
  const [bundle, setBundle] = useState([])
  const [metrics, setMetrics] = useState(null)
  const [toast, setToast] = useState(null)

  const refreshList = useCallback(() => api.procedures().then(setProcs).catch(() => {}), [])

  useEffect(() => { api.health().then(setHealth).catch(() => setHealth({ ok: false })); refreshList() }, [refreshList])

  const loadProcedure = useCallback(async (id) => {
    setSelected(id); setSpec(null); setReview(null); setBundle([]); setMetrics(null)
    if (!id) return
    const st = await api.status(id).catch(() => null)
    setStatus(st)
    const [s, r, b, m] = await Promise.all([
      api.spec(id).catch(() => null), api.review(id).catch(() => null),
      api.bundle(id).catch(() => []), api.metrics(id).catch(() => null)])
    setSpec(s); setReview(r); setBundle(b); setMetrics(m)
  }, [])

  // poll status while running
  useEffect(() => {
    if (!selected || !status || status.state !== 'running') return
    const t = setInterval(async () => {
      const st = await api.status(selected).catch(() => null)
      if (!st) return
      setStatus(st)
      if (st.state !== 'running') { clearInterval(t); loadProcedure(selected); refreshList() }
    }, 1000)
    return () => clearInterval(t)
  }, [selected, status?.state, loadProcedure, refreshList])

  const notify = (msg, kind = 'ok') => { setToast({ msg, kind }); setTimeout(() => setToast(null), 4000) }

  return (
    <div className="app">
      <Sidebar procs={procs} selected={selected} onSelect={loadProcedure} health={health}
        onUploaded={async (res) => { await refreshList(); await loadProcedure(res.id); notify(`Uploaded ${res.id}: ${res.width}×${res.height}, ${res.duration_s.toFixed(1)} s`) }} />
      <main>
        {!selected && <Empty health={health} />}
        {selected && (
          <>
            <RunPanel id={selected} status={status} health={health}
              onRun={async (opts) => { try { await api.run(selected, opts); setStatus({ state: 'running', log: ['queued'] }) } catch (e) { notify(e.message, 'err') } }} />
            {metrics && metrics.run_id && <MetricsCard metrics={metrics} />}
            {spec && <SpecEditor id={selected} spec={spec} review={review} bundle={bundle} notify={notify} webBuild={!!health?.web_build}
              onSaved={(s) => { setSpec(s); refreshList() }} />}
          </>
        )}
      </main>
      {toast && <div className={`toast ${toast.kind}`}>{toast.msg}</div>}
    </div>
  )
}

function Empty({ health }) {
  return (
    <div className="empty">
      <h1>vid2sim</h1>
      <p>Film a procedure once on a phone. Get a hands-first Quest training module back.</p>
      <p className="chain">Spark understands · SAM segments · Muse Code builds · Muse Image paints</p>
      <p className="muted">Upload a video on the left, then Run. {health && !health.has_key && 'No MODEL_API_KEY on the server: runs will use mock mode.'}</p>
    </div>
  )
}
