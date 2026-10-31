#!/usr/bin/env bash
# One command: backend + HTTPS tunnel + QR code for the phone.
#   scripts/serve.sh            # starts uvicorn on 127.0.0.1:8000 and a Cloudflare quick tunnel, prints the https URL and a QR
#   scripts/serve.sh stop       # stops both
# Needs: .venv (pip install -r backend/requirements.txt), ffmpeg, cloudflared (brew install cloudflared), MODEL_API_KEY in .env.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p logs
if [ "${1:-}" = "stop" ]; then
  pkill -f "uvicorn backend.app:app" 2>/dev/null || true
  pkill -f "cloudflared tunnel" 2>/dev/null || true
  echo "stopped"; exit 0
fi
if ! curl -sf http://127.0.0.1:8000/api/health >/dev/null 2>&1; then
  nohup .venv/bin/uvicorn backend.app:app --host 127.0.0.1 --port 8000 > logs/backend.log 2>&1 &
  for i in $(seq 1 30); do curl -sf http://127.0.0.1:8000/api/health >/dev/null 2>&1 && break; sleep 0.5; done
fi
echo "backend: http://127.0.0.1:8000  (dashboard /, guide /guide/, verify /guide/verify.html)"
if [ -n "${TUNNEL_URL:-}" ]; then
  URL="$TUNNEL_URL"          # a named tunnel or any reverse proxy you run yourself
elif command -v cloudflared >/dev/null 2>&1; then
  if ! pgrep -f "cloudflared tunnel" >/dev/null 2>&1; then
    nohup cloudflared tunnel --no-autoupdate --url http://127.0.0.1:8000 > logs/tunnel.log 2>&1 &
  fi
  URL=""
  for i in $(seq 1 40); do
    URL=$(grep -o "https://[a-z0-9-]*\.trycloudflare\.com" logs/tunnel.log 2>/dev/null | head -1 || true)
    [ -n "$URL" ] && break; sleep 0.5
  done
else
  echo "cloudflared not installed (brew install cloudflared); phones need HTTPS for the camera. Set TUNNEL_URL to use your own."; exit 0
fi
GUIDE="$URL/guide/"
echo "phone:   $GUIDE"
if .venv/bin/python -c "import qrcode" 2>/dev/null; then
  .venv/bin/python - "$GUIDE" <<'PY'
import sys, qrcode
q = qrcode.QRCode(border=1); q.add_data(sys.argv[1]); q.make()
q.print_ascii(invert=True)
q.make_image().save("logs/guide-qr.png"); print("QR saved to logs/guide-qr.png")
PY
fi
