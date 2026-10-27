import React from 'react'

export default function MetricsCard({ metrics: m }) {
  const gt = m.ground_truth
  return (
    <section className="metrics">
      <Stat label="upload → playable" value={`${(m.upload_to_spec_s / 60).toFixed(1)} min`} />
      <Stat label="API cost" value={`$${Number(m.api_cost_usd).toFixed(3)}`} />
      <Stat label="steps" value={gt ? `${gt.steps_correct}/${gt.steps_truth} correct` : m.steps} sub={gt ? 'vs trainer ground truth' : 'extracted'} />
      <Stat label="parts found" value={`${m.parts_found_concept} text · ${m.parts_found_box} box`} sub={m.parts_manual ? `${m.parts_manual} need a tap` : 'SAM 3.1'} />
      <Stat label="generated" value={(m.generated_actions || []).filter(g => g.path && g.compiled !== false).length} sub={(m.generated_actions || []).some(g => g.compiled === null) ? 'scripts, gate not run yet' : 'Muse Code scripts, compile-gated'} />
    </section>
  )
}

const Stat = ({ label, value, sub }) => (
  <div className="stat"><div className="v">{value}</div><div className="l">{label}</div>{sub && <div className="s">{sub}</div>}</div>
)
