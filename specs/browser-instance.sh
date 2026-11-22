#!/usr/bin/env bash
# Separate instance for manual browser checks: API :5291 + Next :3200 on the fuelclub_browser
# database (postgres-test container). Never touches the dev database or the owner's :5271/:3000.
# Usage: specs/browser-instance.sh start|stop
set -euo pipefail
cd "$(dirname "$0")/.."

DB='Host=localhost;Port=5434;Database=fuelclub_browser;Username=fuelclub_test;Password=fuelclub_test'
LOGS="${TMPDIR:-/tmp}/fuelclub-browser"
mkdir -p "$LOGS"

listening() { lsof -t -iTCP:5291 -iTCP:3200 -sTCP:LISTEN 2>/dev/null || true; }

case "${1:-start}" in
start)
  if [ -n "$(listening)" ]; then
    echo "port 5291 or 3200 already in use - run '$0 stop' first" >&2
    exit 1
  fi
  dotnet tool run dotnet-ef database update --project apps/api --connection "$DB" >"$LOGS/migrate.log"
  ASPNETCORE_ENVIRONMENT=Development \
    ConnectionStrings__DefaultConnection="$DB" \
    DemoAccounts__Enabled=true \
    DemoAccounts__Password='Browser-Demo-Pass1!' \
    nohup dotnet run --project apps/api --no-launch-profile --urls http://localhost:5291 >"$LOGS/api.log" 2>&1 &
  (cd apps/web && NEXT_DIST_DIR=.next-browser API_INTERNAL_URL=http://localhost:5291 \
    nohup npx next dev --port 3200 >"$LOGS/web.log" 2>&1 &)
  for _ in $(seq 1 120); do
    curl -sf http://localhost:5291/openapi/v1.json -o /dev/null &&
      curl -sf http://localhost:3200/login -o /dev/null && { echo "ready: http://localhost:3200 (demo accounts: DemoAccounts__Password in this script)"; exit 0; }
    sleep 1
  done
  echo "not ready, see $LOGS" >&2
  exit 1
  ;;
stop)
  # Listeners only: a browser connected to :3200 must survive.
  listening | xargs kill 2>/dev/null || true
  ;;
esac
