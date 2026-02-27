#!/usr/bin/env bash
set -euo pipefail

INPUT_ROOT="target/release"
OUTPUT_DIR="dist"

mkdir -p "$OUTPUT_DIR"

packed=0
for dir in "$INPUT_ROOT"/*; do
    if [[ ! -d "$dir" ]]; then
        continue
    fi

    name="$(basename "$dir")"
    archive="$OUTPUT_DIR/${name}.tar.gz"

    echo "Packing $dir -> $archive"
    tar -C "$INPUT_ROOT" -czf "$archive" "$name"
    packed=$((packed + 1))
done

if [[ $packed -eq 0 ]]; then
    echo "No directories found under $INPUT_ROOT"
fi
