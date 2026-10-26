# dashboard

React + Vite. Talks only to `backend/app.py`.

```bash
cd dashboard && npm install && npm run dev      # http://localhost:5173, proxies /api to :8000
npm run build                                    # dist/, served by the backend at http://localhost:8000/
```

Upload → status with wall clock → spec view with SAM mask overlays on the keyframe → edit step text, part
name, order, SAM prompt → Save (schema-validated; errors land on the step that caused them) → Rebuild → Quest
(packages StreamingAssets and hot-pushes over adb). Drag on a keyframe to draw a box when SAM missed a part.
