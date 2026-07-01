#!/usr/bin/env bash
# Wire a local kind cluster to pull images from an insecure (HTTP) Nexus docker registry,
# for the Aspire deploy path (aspirate apply). Run AFTER `kind create cluster` and with the
# Nexus container running. Idempotent — safe to re-run (e.g. after adding worker nodes).
#
# It does two things per kind node:
#   1. connects the node container to the Docker network Nexus is on, so the registry host resolves;
#   2. writes a containerd hosts.toml marking the registry insecure (HTTP, skip TLS verify).
#
# Auth is handled separately by the deployment service (Deployment:Aspirate:EnsurePullSecret=true,
# which provisions a dockerconfigjson pull secret) — this script only handles transport + reachability.
#
# Env (override as needed):
#   CLUSTER     kind cluster name                            (default: kind)
#   NEXUS_NET   docker network the Nexus container is on     (default: cicd_default)
#   NEXUS_HOST  registry host:port used in manifests + pull  (default: nexus:8082)
#
# After running: set Deployment:Aspirate:PullRegistry to $NEXUS_HOST and EnsurePullSecret=true,
# then deploy the Aspire app and `kubectl get pods -n <ns> -w`.
set -euo pipefail

CLUSTER="${CLUSTER:-kind}"
NEXUS_NET="${NEXUS_NET:-cicd_default}"
NEXUS_HOST="${NEXUS_HOST:-nexus:8082}"
NEXUS_NAME="${NEXUS_HOST%%:*}"   # host without the port

echo "==> cluster='$CLUSTER'  nexus-network='$NEXUS_NET'  registry='$NEXUS_HOST'"

nodes="$(kind get nodes --name "$CLUSTER")"
[ -n "$nodes" ] || { echo "No nodes for kind cluster '$CLUSTER' — create it first."; exit 1; }

for node in $nodes; do
  echo "--> $node"

  # 1) Join the Nexus network so the node can resolve/reach the registry host.
  if docker network connect "$NEXUS_NET" "$node" 2>/dev/null; then
    echo "    connected to network '$NEXUS_NET'"
  else
    echo "    already on '$NEXUS_NET' (or the network name is wrong — check 'docker network ls')"
  fi

  # 2) Mark the registry insecure for containerd (config_path hosts.toml; read live, no restart).
  docker exec "$node" mkdir -p "/etc/containerd/certs.d/$NEXUS_HOST"
  docker exec "$node" sh -c "cat > '/etc/containerd/certs.d/$NEXUS_HOST/hosts.toml' <<EOF
[host.\"http://$NEXUS_HOST\"]
  capabilities = [\"pull\", \"resolve\"]
  skip_verify = true
EOF"
  echo "    wrote /etc/containerd/certs.d/$NEXUS_HOST/hosts.toml"

  # 3) Ensure containerd reads certs.d (config_path). Recent kind sets this by default.
  if docker exec "$node" grep -q 'config_path = "/etc/containerd/certs.d"' /etc/containerd/config.toml 2>/dev/null; then
    echo "    containerd config_path OK"
  else
    echo "    WARNING: containerd config_path is NOT set on this node, so hosts.toml is ignored."
    echo "             Recreate the cluster with this config, then re-run this script:"
    echo "             ----"
    echo "             kind: Cluster"
    echo "             apiVersion: kind.x-k8s.io/v1alpha4"
    echo "             containerdConfigPatches:"
    echo "               - |-"
    echo "                 [plugins.\"io.containerd.grpc.v1.cri\".registry]"
    echo "                   config_path = \"/etc/containerd/certs.d\""
    echo "             ----"
  fi

  # 4) Sanity: does the registry host resolve on the node now?
  if docker exec "$node" getent hosts "$NEXUS_NAME" >/dev/null 2>&1; then
    echo "    resolves '$NEXUS_NAME' on node"
  else
    echo "    WARNING: '$NEXUS_NAME' does not resolve on $node — is NEXUS_NET correct and is Nexus on it?"
  fi
done

echo "==> Done. Next: Deployment:Aspirate:PullRegistry='$NEXUS_HOST' + EnsurePullSecret=true, then deploy."
