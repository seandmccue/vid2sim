#!/usr/bin/env bash
# Keep the demo up for the judging week: restart the backend and the tunnel if either dies, keep the Mac awake,
# and write the current public URL to logs/public-url.txt. Run it in a terminal you leave open (or under nohup):
#   nohup scripts/keepalive.sh > logs/keepalive.log 2>&1 &
# With a named tunnel / your own HTTPS host, set TUNNEL_URL and only the backend is watched.
set -u
cd "$(dirname "$0")/.."
mkdir -p logs
caffeinate -dimsu -w $$ &      # no sleep while this script runs
while true; do
  if ! curl -sf http://127.0.0.1:8000/api/health >/dev/null 2>&1; then
    echo "$(date '+%F %T') backend down, starting"
    pkill -f "uvicorn backend.app:app" 2>/dev/null || true
    nohup .venv/bin/uvicorn backend.app:app --host 127.0.0.1 --port 8000 > logs/backend.log 2>&1 &
  fi
  if [ -z "${TUNNEL_URL:-}" ] && command -v cloudflared >/dev/null 2>&1; then
    if ! pgrep -f "cloudflared tunnel" >/dev/null 2>&1; then
      echo "$(date '+%F %T') tunnel down, starting"
      : > logs/tunnel.log
      nohup cloudflared tunnel --no-autoupdate --url http://127.0.0.1:8000 >> logs/tunnel.log 2>&1 &
      sleep 6
    fi
    URL=$(grep -o "https://[a-z0-9-]*\.trycloudflare\.com" logs/tunnel.log 2>/dev/null | tail -1 || true)
  else
    URL="${TUNNEL_URL:-}"
  fi
  if [ -n "$URL" ] && [ "$URL" != "$(cat logs/public-url.txt 2>/dev/null)" ]; then
    echo "$URL" > logs/public-url.txt
    echo "$(date '+%F %T') public URL: $URL/guide/"
  fi
  sleep 20
done
