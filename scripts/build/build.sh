#!/usr/bin/env bash

set -euo pipefail

cd "$(dirname "$0")/../.."

PROJECT_NAME="Lash"
OUTPUT_DIR="./target/release"
PROJECTS=(
  "lash:./src/Lash.Cli/Lash.Cli.csproj"
  "lashc:./src/Lash.Compiler/Lash.Compiler.csproj"
  "lashfmt:./src/Lash.Formatter/Lash.Formatter.csproj"
  "lashlsp:./src/Lash.Lsp/Lash.Lsp.csproj"
)

CONFIGURATION="Release"
ENABLE_TRIMMING=true
NO_TRIM_TOOLS=()
ENABLE_SINGLE_FILE=true
ENABLE_READY_TO_RUN=true

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

show_help() {
  cat <<USAGE
Usage: $0 [OPTIONS] <platform1> [platform2] ...

Options:
  --no-trim [tool]   Disable trimming globally or for one tool
  --no-single-file   Publish as multiple files (faster builds, easier debugging)
  --no-ready2run     Disable ReadyToRun compilation (faster builds, slower startup)
  -h, --help         Show this help message

Arguments:
  <platforms>  Space-separated list of platforms to build

Available platforms:
  win-x64, win-x86, win-arm64
  linux-x64, linux-arm64, linux-arm
  osx-x64, osx-arm64

Examples:
  $0 win-x64                           # Build only Windows 64-bit
  $0 --no-trim win-x64                 # Build Windows 64-bit without trimming
  $0 --no-trim lashlsp linux-x64       # Disable trimming only for lashlsp
  $0 --no-single-file --no-trim win-x64  # Fast dev build
  $0 win-x64 linux-x64                 # Build Windows and Linux 64-bit
  $0 osx-x64 osx-arm64                 # Build both macOS versions
USAGE
}

is_known_tool() {
  local tool_name="$1"
  local entry known
  for entry in "${PROJECTS[@]}"; do
    known="${entry%%:*}"
    if [[ "$known" == "$tool_name" ]]; then
      return 0
    fi
  done
  return 1
}

list_to_csv() {
  local -a values=("$@")
  local IFS=,
  printf '%s' "${values[*]}"
}

should_trim_tool() {
  local tool_name="$1"
  local disabled

  if [[ "$ENABLE_TRIMMING" != true ]]; then
    return 1
  fi

  for disabled in "${NO_TRIM_TOOLS[@]}"; do
    if [[ "$disabled" == "$tool_name" ]]; then
      return 1
    fi
  done

  return 0
}

is_windows_rid() {
  local rid="$1"
  [[ "$rid" == win-* ]]
}

get_output_dirname() {
  local rid="$1"
  local platform=""
  local arch=""
  local bits=""

  case "$rid" in
    win-x64) platform="win"; arch="x86"; bits="64" ;;
    win-x86) platform="win"; arch="x86"; bits="32" ;;
    win-arm64) platform="win"; arch="arm"; bits="64" ;;
    linux-x64) platform="linux"; arch="x86"; bits="64" ;;
    linux-arm64) platform="linux"; arch="arm"; bits="64" ;;
    linux-arm) platform="linux"; arch="arm"; bits="32" ;;
    osx-x64) platform="osx"; arch="x86"; bits="64" ;;
    osx-arm64) platform="osx"; arch="arm"; bits="64" ;;
    *) platform="$rid"; arch="unknown"; bits="" ;;
  esac

  if [[ -n "$bits" ]]; then
    printf '%s' "${PROJECT_NAME}-${platform}_${arch}-${bits}"
  else
    printf '%s' "${PROJECT_NAME}-${platform}_${arch}"
  fi
}

find_published_executable() {
  local temp_dir="$1"
  local rid="$2"
  local project_stem="$3"
  local expected

  if is_windows_rid "$rid"; then
    expected="${temp_dir}/${project_stem}.exe"
    if [[ -f "$expected" ]]; then
      printf '%s' "$expected"
    else
      find "$temp_dir" -maxdepth 1 -type f -name '*.exe' ! -name 'createdump.exe' -print -quit
    fi
    return
  fi

  expected="${temp_dir}/${project_stem}"
  if [[ -f "$expected" ]]; then
    printf '%s' "$expected"
  else
    find "$temp_dir" -maxdepth 1 -type f -perm -u+x -print -quit
  fi
}

build_tool_for_platform() {
  local tool_name="$1"
  local project_path="$2"
  local rid="$3"
  local bundle_dir="$4"

  local temp_dir="${OUTPUT_DIR}/temp_${tool_name}_${rid}"
  local -a publish_args=(
    "$project_path"
    "-c" "$CONFIGURATION"
    "-r" "$rid"
    "--self-contained"
    "-p:UseAppHost=true"
    "-o" "$temp_dir"
  )

  if [[ "$ENABLE_SINGLE_FILE" == true ]]; then
    publish_args+=("-p:PublishSingleFile=True")
  fi

  if [[ "$ENABLE_READY_TO_RUN" == true ]]; then
    publish_args+=("-p:PublishReadyToRun=True")
  fi

  if should_trim_tool "$tool_name"; then
    publish_args+=(
      "-p:PublishTrimmed=True"
      "-p:TrimMode=CopyUsed"
      "-p:EnableTrimAnalyzer=True"
      "-warnaserror:IL2*"
    )
  fi

  dotnet publish "${publish_args[@]}"

  local project_stem
  project_stem="$(basename "$project_path" .csproj)"

  local src_file
  src_file="$(find_published_executable "$temp_dir" "$rid" "$project_stem")"

  if [[ -z "$src_file" || ! -f "$src_file" ]]; then
    echo -e "${RED}✗ Build completed but executable not found for ${tool_name}${NC}"
    rm -rf "$temp_dir"
    return 1
  fi

  local extension=""
  if is_windows_rid "$rid"; then
    extension=".exe"
  fi

  local dest_file="${bundle_dir}/${tool_name}${extension}"
  mv "$src_file" "$dest_file"
  rm -rf "$temp_dir"

  if ! is_windows_rid "$rid"; then
    chmod +x "$dest_file"
  fi

  local size
  size="$(du -h "$dest_file" | cut -f1)"
  echo -e "${GREEN}✓ Built ${tool_name} (${size}) -> ${dest_file}${NC}"
}

build_platform() {
  local rid="$1"
  local description="$2"
  local bundle_dir="${OUTPUT_DIR}/$(get_output_dirname "$rid")"

  echo
  echo -e "${YELLOW}Building for ${description} (${rid})...${NC}"

  rm -rf "$bundle_dir"
  mkdir -p "$bundle_dir"

  local entry tool_name project_path

  for entry in "${PROJECTS[@]}"; do
    tool_name="${entry%%:*}"
    project_path="${entry#*:}"
    if ! build_tool_for_platform "$tool_name" "$project_path" "$rid" "$bundle_dir"; then
      local build_status=$?
      echo -e "${RED}✗ Build failed for ${tool_name} (${rid})${NC}"
      return "$build_status"
    fi
  done

  local bundle_size
  bundle_size="$(du -sh "$bundle_dir" | cut -f1)"
  echo -e "${GREEN}✓ Bundle ready (${bundle_size}) -> ${bundle_dir}${NC}"
  ls -lah "$bundle_dir"
}

get_platform_description() {
  local rid="$1"
  case "$rid" in
    win-x64) printf '%s' "Windows (64-bit)" ;;
    win-x86) printf '%s' "Windows (32-bit)" ;;
    win-arm64) printf '%s' "Windows ARM64" ;;
    linux-x64) printf '%s' "Linux (64-bit)" ;;
    linux-arm64) printf '%s' "Linux ARM64" ;;
    linux-arm) printf '%s' "Linux ARM" ;;
    osx-x64) printf '%s' "macOS Intel" ;;
    osx-arm64) printf '%s' "macOS Apple Silicon" ;;
    *) printf '%s' "$rid" ;;
  esac
}

BUILD_PLATFORMS=()
while [[ $# -gt 0 ]]; do
  arg="$1"
  case "$arg" in
    -h|--help)
      show_help
      exit 0
      ;;
    --no-trim)
      if [[ $# -gt 1 ]] && is_known_tool "$2"; then
        NO_TRIM_TOOLS+=("$2")
        shift 2
      else
        ENABLE_TRIMMING=false
        shift
      fi
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
      BUILD_PLATFORMS+=("$arg")
      shift
      ;;
  esac
done

if [[ ${#BUILD_PLATFORMS[@]} -eq 0 ]]; then
  echo -e "${RED}Error: No platforms specified${NC}"
  echo
  show_help
  exit 1
fi

echo -e "${GREEN}  Building ${PROJECT_NAME}${NC}"
echo -e "${BLUE}Configuration:${NC}"
if [[ "$ENABLE_TRIMMING" == true ]]; then
  if [[ ${#NO_TRIM_TOOLS[@]} -eq 0 ]]; then
    echo -e "  Trimming:     ${GREEN}Enabled${NC}"
  else
    disabled="$(list_to_csv "${NO_TRIM_TOOLS[@]}")"
    echo -e "  Trimming:     ${YELLOW}Enabled (except: ${disabled})${NC}"
  fi
else
  echo -e "  Trimming:     ${YELLOW}Disabled${NC}"
fi

if [[ "$ENABLE_SINGLE_FILE" == true ]]; then
  echo -e "  Single File:  ${GREEN}Enabled${NC}"
else
  echo -e "  Single File:  ${YELLOW}Disabled${NC}"
fi

if [[ "$ENABLE_READY_TO_RUN" == true ]]; then
  echo -e "  ReadyToRun:   ${GREEN}Enabled${NC}"
else
  echo -e "  ReadyToRun:   ${YELLOW}Disabled${NC}"
fi

if [[ -d "$OUTPUT_DIR" ]]; then
  echo -e "${YELLOW}Cleaning output directory...${NC}"
  rm -rf "$OUTPUT_DIR"
fi
mkdir -p "$OUTPUT_DIR"

echo -e "${BLUE}Building platforms: ${BUILD_PLATFORMS[*]}${NC}"
for rid in "${BUILD_PLATFORMS[@]}"; do
  description="$(get_platform_description "$rid")"
  build_platform "$rid" "$description"
done

echo
echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}  Build Complete!${NC}"
echo -e "${GREEN}========================================${NC}"
echo
echo "Bundles are located in:"
echo
find "$OUTPUT_DIR" -maxdepth 1 -mindepth 1 -type d -name "${PROJECT_NAME}-*" | sort
