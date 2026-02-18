#!/usr/bin/env bash
set -euo pipefail

PROJECT_NAME="Lash"
OUTPUT_DIR="./target/release"
PROJECTS=(
    "lash:./src/Lash.Cli/Lash.Cli.csproj"
    "lashc:./src/Lash.Compiler/Lash.Compiler.csproj"
    "lashfmt:./src/Lash.Formatter/Lash.Formatter.csproj"
)

# Build defaults
CONFIGURATION="Release"
ENABLE_TRIMMING=true
ENABLE_SINGLE_FILE=true
ENABLE_READY_TO_RUN=true

# Color output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

show_help() {
    echo "Usage: $0 [OPTIONS] <platform1> [platform2] ..."
    echo ""
    echo "Options:"
    echo "  --no-trim          Disable trimming (faster builds, larger binaries)"
    echo "  --no-single-file   Publish as multiple files (faster builds, easier debugging)"
    echo "  --no-ready2run     Disable ReadyToRun compilation (faster builds, slower startup)"
    echo "  -h, --help         Show this help message"
    echo ""
    echo "Arguments:"
    echo "  <platforms>  Space-separated list of platforms to build"
    echo ""
    echo "Available platforms:"
    echo "  win-x64, win-x86, win-arm64"
    echo "  linux-x64, linux-arm64, linux-arm"
    echo "  osx-x64, osx-arm64"
    echo ""
    echo "Examples:"
    echo "  $0 win-x64                           # Build only Windows 64-bit"
    echo "  $0 --no-trim win-x64                 # Build Windows 64-bit without trimming"
    echo "  $0 --no-single-file --no-trim win-x64  # Fast dev build"
    echo "  $0 win-x64 linux-x64                 # Build Windows and Linux 64-bit"
    echo "  $0 osx-x64 osx-arm64                 # Build both macOS versions"
    exit 0
}

# Parse options
BUILD_PLATFORMS=()
while [[ $# -gt 0 ]]; do
    case $1 in
    -h | --help)
        show_help
        ;;
    --no-trim)
        ENABLE_TRIMMING=false
        shift
        ;;
    --no-single-file)
        ENABLE_SINGLE_FILE=false
        shift
        ;;
    --no-ready2run)
        ENABLE_READY_TO_RUN=false
        shift
        ;;
    *)
        BUILD_PLATFORMS+=("$1")
        shift
        ;;
    esac
done

# Check if any platforms specified
if [ ${#BUILD_PLATFORMS[@]} -eq 0 ]; then
    echo -e "${RED}Error: No platforms specified${NC}"
    echo ""
    show_help
fi

echo -e "${GREEN}  Building ${PROJECT_NAME}${NC}"
echo -e "${BLUE}Configuration:${NC}"
echo -e "  Trimming:     $([ "$ENABLE_TRIMMING" = true ] && echo "${GREEN}Enabled${NC}" || echo "${YELLOW}Disabled${NC}")"
echo -e "  Single File:  $([ "$ENABLE_SINGLE_FILE" = true ] && echo "${GREEN}Enabled${NC}" || echo "${YELLOW}Disabled${NC}")"
echo -e "  ReadyToRun:   $([ "$ENABLE_READY_TO_RUN" = true ] && echo "${GREEN}Enabled${NC}" || echo "${YELLOW}Disabled${NC}")"

# Clean output directory
if [ -d "$OUTPUT_DIR" ]; then
    echo -e "${YELLOW}Cleaning output directory...${NC}"
    rm -rf "$OUTPUT_DIR"
fi
mkdir -p "$OUTPUT_DIR"

get_output_dirname() {
    local rid=$1
    local platform=""
    local arch=""
    local bits=""

    case $rid in
    win-x64)
        platform="win"
        arch="x86"
        bits="64"
        ;;
    win-x86)
        platform="win"
        arch="x86"
        bits="32"
        ;;
    win-arm64)
        platform="win"
        arch="arm"
        bits="64"
        ;;
    linux-x64)
        platform="linux"
        arch="x86"
        bits="64"
        ;;
    linux-arm64)
        platform="linux"
        arch="arm"
        bits="64"
        ;;
    linux-arm)
        platform="linux"
        arch="arm"
        bits="32"
        ;;
    osx-x64)
        platform="osx"
        arch="x86"
        bits="64"
        ;;
    osx-arm64)
        platform="osx"
        arch="arm"
        bits="64"
        ;;
    esac

    echo "${PROJECT_NAME}-${platform}_${arch}-${bits}"
}

find_published_executable() {
    local temp_dir=$1
    local rid=$2
    local project_stem=$3

    if [[ "$rid" == win-* ]]; then
        if [ -f "$temp_dir/$project_stem.exe" ]; then
            echo "$temp_dir/$project_stem.exe"
            return
        fi

        find "$temp_dir" -maxdepth 1 -type f -name '*.exe' ! -name 'createdump.exe' -print -quit
        return
    fi

    if [ -f "$temp_dir/$project_stem" ]; then
        echo "$temp_dir/$project_stem"
        return
    fi

    while IFS= read -r candidate; do
        if [ -x "$candidate" ]; then
            echo "$candidate"
            return
        fi
    done < <(find "$temp_dir" -maxdepth 1 -type f -print)
}

build_tool_for_platform() {
    local tool_name=$1
    local project_path=$2
    local rid=$3
    local bundle_dir=$4
    local temp_dir="$OUTPUT_DIR/temp_${tool_name}_${rid}"

    local publish_args=(
        "$project_path"
        -c "$CONFIGURATION"
        -r "$rid"
        --self-contained
        -p:UseAppHost=true
        -o "$temp_dir"
    )

    if [ "$ENABLE_SINGLE_FILE" = true ]; then
        publish_args+=(-p:PublishSingleFile=True)
    fi

    if [ "$ENABLE_READY_TO_RUN" = true ]; then
        publish_args+=(-p:PublishReadyToRun=True)
    fi

    if [ "$ENABLE_TRIMMING" = true ]; then
        publish_args+=(
            -p:PublishTrimmed=True
            -p:TrimMode=CopyUsed
            -p:EnableTrimAnalyzer=True
            -warnaserror:IL2*
        )
    fi

    dotnet publish "${publish_args[@]}"

    local project_stem
    project_stem="$(basename "$project_path" .csproj)"
    local src_file
    src_file="$(find_published_executable "$temp_dir" "$rid" "$project_stem")"

    if [ -z "$src_file" ] || [ ! -f "$src_file" ]; then
        echo -e "${RED}✗ Build completed but executable not found for ${tool_name}${NC}"
        rm -rf "$temp_dir"
        return 1
    fi

    local extension=""
    if [[ "$rid" == win-* ]]; then
        extension=".exe"
    fi

    local dest_file="$bundle_dir/${tool_name}${extension}"
    mv "$src_file" "$dest_file"
    rm -rf "$temp_dir"

    if [[ "$rid" != win-* ]]; then
        chmod +x "$dest_file"
    fi

    local size
    size=$(du -h "$dest_file" | cut -f1)
    echo -e "${GREEN}✓ Built ${tool_name} (${size}) -> ${dest_file}${NC}"
}

build_platform() {
    local rid=$1
    local description=$2
    local bundle_dir="$OUTPUT_DIR/$(get_output_dirname "$rid")"

    echo ""
    echo -e "${YELLOW}Building for $description ($rid)...${NC}"

    rm -rf "$bundle_dir"
    mkdir -p "$bundle_dir"

    for entry in "${PROJECTS[@]}"; do
        local tool_name
        local project_path
        IFS=':' read -r tool_name project_path <<< "$entry"
        build_tool_for_platform "$tool_name" "$project_path" "$rid" "$bundle_dir"
    done

    local bundle_size
    bundle_size=$(du -sh "$bundle_dir" | cut -f1)
    echo -e "${GREEN}✓ Bundle ready (${bundle_size}) -> ${bundle_dir}${NC}"
    ls -lah "$bundle_dir"
}

get_platform_description() {
    local rid=$1
    case $rid in
    win-x64) echo "Windows (64-bit)" ;;
    win-x86) echo "Windows (32-bit)" ;;
    win-arm64) echo "Windows ARM64" ;;
    linux-x64) echo "Linux (64-bit)" ;;
    linux-arm64) echo "Linux ARM64" ;;
    linux-arm) echo "Linux ARM" ;;
    osx-x64) echo "macOS Intel" ;;
    osx-arm64) echo "macOS Apple Silicon" ;;
    *) echo "$rid" ;;
    esac
}

# Build selected platforms
echo -e "${BLUE}Building platforms: ${BUILD_PLATFORMS[*]}${NC}"

for rid in "${BUILD_PLATFORMS[@]}"; do
    description=$(get_platform_description "$rid")
    build_platform "$rid" "$description"
done

echo ""
echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}  Build Complete!${NC}"
echo -e "${GREEN}========================================${NC}"
echo ""
echo "Bundles are located in:"
echo ""
find "$OUTPUT_DIR" -maxdepth 1 -mindepth 1 -type d -name "${PROJECT_NAME}-*" | sort
