const j = async (r) => {
  const text = await r.text()
  let data
  try { data = text ? JSON.parse(text) : null } catch { data = { detail: text } }
  if (!r.ok) {
    const err = new Error(data?.detail || data?.errors?.map(e => `${e.path}: ${e.message}`).join('; ') || r.statusText)
    err.status = r.status; err.data = data
    throw err
  }
  return data
}

export const api = {
  health: () => fetch('/api/health').then(j),
  procedures: () => fetch('/api/procedures').then(j),
  upload: (file, title) => {
    const fd = new FormData(); fd.append('video', file); fd.append('title', title || '')
    return fetch('/api/upload', { method: 'POST', body: fd }).then(j)
  },
  run: (id, opts = {}) => {
    const q = new URLSearchParams(Object.entries(opts).filter(([, v]) => v !== undefined && v !== null)).toString()
    return fetch(`/api/run/${id}${q ? '?' + q : ''}`, { method: 'POST' }).then(j)
  },
  status: (id) => fetch(`/api/status/${id}`).then(j),
  spec: (id) => fetch(`/api/procedures/${id}/spec`).then(j),
  saveSpec: (id, spec) => fetch(`/api/procedures/${id}/spec`, { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(spec) }).then(j),
  review: (id) => fetch(`/api/procedures/${id}/review`).then(j),
  metrics: (id) => fetch(`/api/procedures/${id}/metrics`).then(j),
  bundle: (id) => fetch(`/api/procedures/${id}/bundle`).then(j),
  build: (id, opts = {}) => {
    const q = new URLSearchParams(opts).toString()
    return fetch(`/api/procedures/${id}/build${q ? '?' + q : ''}`, { method: 'POST' }).then(j)
  },
  frameUrl: (id, n) => `/api/procedures/${id}/frame/${n}.jpg`,
  fileUrl: (id, path) => `/api/procedures/${id}/files/${path}`,
}

export const ACTIONS = ['press', 'grab', 'pull', 'lift', 'place', 'turn_cw', 'turn_ccw', 'inspect']
export const INTERACTIONS = ['press', 'grab', 'turn', 'inspect']
