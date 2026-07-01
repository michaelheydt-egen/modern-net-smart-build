#!/usr/bin/env bash
# Build the sample Aspire app's container images, push them to Nexus, and publish the Kustomize
# output archive to the Nexus raw repo. These are the two inputs the deployment service's
# "Aspire application" deploy consumes (it fetches the archive, digest-pins the images, applies).
#
# This is the CI role — run it wherever the app SOURCE + .NET SDK + Docker + aspirate live.
#
# Usage (override any via env):
#   NEXUS_PASS='...' ./publish-to-nexus.sh
#
# Env:
#   APP_NAME        artifact path segment      (default: sampleapp)
#   APP_VERSION     artifact path segment      (default: 1.0.0)
#   NAMESPACE       k8s namespace in manifests (default: sampleapp)
#   BUILD_REGISTRY  docker registry to push to (default: localhost:8082)  -- host-reachable
#   TAG             image tag                  (default: latest)
#   NEXUS_RAW_BASE  raw repo base URL          (default: http://localhost:8081/repository/raw-hosted)
#   NEXUS_USER      (default: admin)
#   NEXUS_PASS      (required)
set -euo pipefail

APP_NAME="${APP_NAME:-sampleapp}"
APP_VERSION="${APP_VERSION:-1.0.0}"
NAMESPACE="${NAMESPACE:-sampleapp}"
BUILD_REGISTRY="${BUILD_REGISTRY:-localhost:8082}"
TAG="${TAG:-latest}"
NEXUS_RAW_BASE="${NEXUS_RAW_BASE:-http://localhost:8081/repository/raw-hosted}"
NEXUS_USER="${NEXUS_USER:-admin}"
: "${NEXUS_PASS:?set NEXUS_PASS}"

export PATH="$PATH:$HOME/.dotnet/tools"
cd "$(dirname "$0")/SampleApp.AppHost"

echo "==> aspirate init (registry $BUILD_REGISTRY, tag $TAG)"
aspirate init -cr "$BUILD_REGISTRY" -ct "$TAG" --disable-secrets --non-interactive >/dev/null

echo "==> aspirate generate — build + push images to Nexus, emit Kustomize (namespace $NAMESPACE)"
aspirate generate --non-interactive --disable-secrets --include-dashboard false \
  --image-pull-policy IfNotPresent --namespace "$NAMESPACE" --output-format kustomize

echo "==> zip + upload kustomize output to Nexus raw"
ZIP="aspirate-output.zip"; rm -f "$ZIP"
if command -v zip >/dev/null 2>&1; then zip -qr "$ZIP" aspirate-output
else powershell.exe -NoProfile -Command "Compress-Archive -Path 'aspirate-output' -DestinationPath '$ZIP' -Force"; fi

URL="$NEXUS_RAW_BASE/$APP_NAME/$APP_VERSION/aspirate-output.zip"
curl -sf -u "$NEXUS_USER:$NEXUS_PASS" --upload-file "$ZIP" "$URL" -o /dev/null -w "uploaded HTTP %{http_code}\n"
rm -f "$ZIP"

echo
echo "Manifest source URL — register this in web-admin (Deployment -> Aspire apps):"
echo "  $URL"
