#!/bin/bash
set -e

APP_DIR="/opt/objectdetection"
WORK_DIR="${HOME}/objectdetection"
SRC_DIR="${WORK_DIR}/src"
BUILD_DIR="${WORK_DIR}/build"
SERVICE_NAME="objectdetection"
REPO_URL="https://github.com/VovaZhukovsky/ObjectDetectionAlert.git"

# Check server dependencies
for cmd in git dotnet ffmpeg; do
  if ! command -v "$cmd" > /dev/null; then
    echo "$cmd not found in PATH"
    exit 1
  fi
done

# Clone or pull
if [ ! -d "$SRC_DIR/.git" ]; then
  mkdir -p "$WORK_DIR"
  git clone "$REPO_URL" "$SRC_DIR"
else
  git -C "$SRC_DIR" pull --ff-only origin main
fi

# Settings and model live only on the server, next to the app
for file in appsettings.json best.onnx; do
  if [ ! -f "$APP_DIR/$file" ]; then
    echo "$file not found at $APP_DIR/$file"
    exit 1
  fi
done
chmod 600 "$APP_DIR/appsettings.json"

# Build into a separate folder so the running app is not touched
rm -rf "$BUILD_DIR"
dotnet publish "$SRC_DIR/ObjectDetectionApi/ObjectDetectionApi.csproj" -c Release -o "$BUILD_DIR" --nologo

# The systemd unit is managed by hand on the server, not by this script

# Swap in the new build; appsettings.json, best.onnx, logs/ and dataset_frames/ stay
sudo systemctl stop "${SERVICE_NAME}.service"
cp -r "$BUILD_DIR"/. "$APP_DIR"/
rm -rf "$BUILD_DIR"
sudo systemctl start "${SERVICE_NAME}.service"

# Workflow logs are public: print journal lines without the hostname
sleep 5
if ! systemctl is-active --quiet "${SERVICE_NAME}.service"; then
  echo "Service failed to start"
  sudo journalctl -u "${SERVICE_NAME}.service" -n 50 --no-pager -o cat
  exit 1
fi
sudo systemctl status "${SERVICE_NAME}.service" --no-pager -n 0
