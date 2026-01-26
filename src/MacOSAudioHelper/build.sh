#!/bin/bash

# Build script for MacOSAudioHelper
# Run this on macOS to compile the Swift helper

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DIR="$SCRIPT_DIR/../../src/WisprClone.Avalonia/bin/Debug/net8.0"

echo "Building MacOSAudioHelper..."

# Compile for both architectures (Universal Binary)
swiftc -O -o "$SCRIPT_DIR/MacOSAudioHelper" \
    -target arm64-apple-macos11.0 \
    -target x86_64-apple-macos11.0 \
    "$SCRIPT_DIR/main.swift" \
    -framework AVFoundation \
    -framework Foundation

# Make executable
chmod +x "$SCRIPT_DIR/MacOSAudioHelper"

echo "Built: $SCRIPT_DIR/MacOSAudioHelper"

# Copy to output directory if it exists
if [ -d "$OUTPUT_DIR" ]; then
    cp "$SCRIPT_DIR/MacOSAudioHelper" "$OUTPUT_DIR/"
    echo "Copied to: $OUTPUT_DIR/MacOSAudioHelper"
fi

echo "Done!"
