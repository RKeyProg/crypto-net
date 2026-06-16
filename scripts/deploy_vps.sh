#!/usr/bin/env bash
set -euo pipefail

PROJECT_DIR="${1:-/opt/cryptonet}"

cd "$PROJECT_DIR"

if [[ ! -f ".env" ]]; then
  echo "Missing .env in $PROJECT_DIR"
  exit 1
fi

docker compose down --remove-orphans
docker compose up -d --build --remove-orphans
docker image prune -f
docker compose ps
