#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
cd "${REPO_ROOT}"

RID="linux-x64"
CONFIGURATION="Debug"
OUTPUT_ROOT="./target/Debug"
BUNDLE_DIR="${OUTPUT_ROOT}/Lash-linux_x86-64"

PROJECTS=(
    "lash:./src/Lash.Cli/Lash.Cli.csproj"
    "lashc:./src/Lash.Compiler/Lash.Compiler.csproj"
    "lashfmt:./src/Lash.Formatter/Lash.Formatter.csproj"
    "lashlsp:./src/Lash.Lsp/Lash.Lsp.csproj"
)

printf "Bootstrap build (linux-x64)\n"

rm -rf "${BUNDLE_DIR}"
mkdir -p "${BUNDLE_DIR}"

for entry in "${PROJECTS[@]}"; do
    tool_name="${entry%%:*}"
    project_path="${entry#*:}"
    temp_dir="${OUTPUT_ROOT}/temp_${tool_name}_${RID}"

    rm -rf "${temp_dir}"

    dotnet publish "${project_path}" \
        -c "${CONFIGURATION}" \
        -r "${RID}" \
        --self-contained \
        -p:UseAppHost=true \
        -p:PublishSingleFile=true \
        -p:PublishReadyToRun=false \
        -p:PublishTrimmed=false \
        -o "${temp_dir}"

    project_stem="$(basename "${project_path}" .csproj)"
    src_file="${temp_dir}/${project_stem}"
    if [[ ! -f "${src_file}" ]]; then
        src_file="$(find "${temp_dir}" -maxdepth 1 -type f -perm -u+x -print -quit)"
    fi

    if [[ -z "${src_file}" || ! -f "${src_file}" ]]; then
        echo "Build completed but executable not found for ${tool_name}"
        exit 1
    fi

    dest_file="${BUNDLE_DIR}/${tool_name}"
    mv "${src_file}" "${dest_file}"
    chmod +x "${dest_file}"
    rm -rf "${temp_dir}"

    size="$(du -h "${dest_file}" | cut -f1)"
    echo "Built ${tool_name} (${size}) -> ${dest_file}"
done

echo "Bundle ready -> ${BUNDLE_DIR}"
ls -lah "${BUNDLE_DIR}"
