#!/usr/bin/env bash
set -euo pipefail

image="${1:-ubuntu:24.04}"
output="${2:-gajaecode-wsl-rootfs.tar}"
container="gajaecode-wsl-rootfs-$$"

cleanup() {
  docker rm -f "$container" >/dev/null 2>&1 || true
}
trap cleanup EXIT

docker create --name "$container" "$image" sleep infinity >/dev/null
docker start "$container" >/dev/null
docker exec "$container" bash -lc '
  set -euo pipefail
  export DEBIAN_FRONTEND=noninteractive
  apt-get update
  apt-get install -y --no-install-recommends \
    bash ca-certificates git less locales nodejs npm procps python3 python3-pip ripgrep tmux
  apt-get clean
  rm -rf /var/lib/apt/lists/*
  id gjc >/dev/null 2>&1 || useradd --create-home --shell /bin/bash gjc
'
docker export "$container" --output "$output"
sha256sum "$output" >"${output}.sha256"
echo "Created $output"
