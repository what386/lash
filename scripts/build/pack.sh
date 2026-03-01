#!/usr/bin/env bash
set -euo pipefail
cd ../..
readonly INPUT_ROOT="./target/release"
readonly OUTPUT_DIR="./dist"
mkdir -p "$OUTPUT_DIR"
cd "$INPUT_ROOT"
packed=0
for dir in ./*; do
    readonly archive="../../dist/${dir}.tar.gz"
    echo "Packing:" $dir "->" $archive
    tar -czf "$archive" "$dir"
    packed=$(( ${packed} + 1 ))
done
if [[ ${packed} == 0 ]]; then
    echo "No directories found under ./target/release"
else
    echo "Packed {\$packed} directories."
fi
