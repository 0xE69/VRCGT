#!/usr/bin/env bash
# ============================================================
# scripts/build.sh — VRCGT build + publish script
# ============================================================
# Builds and publishes self-contained VRCGT artifacts for one
# or more .NET runtime identifiers (RIDs).
#
# Usage:
#   ./scripts/build.sh [options]
#
# Options:
#   --rids <r1,r2,...>   Comma-separated RID list.
#                        Default: linux-x64,osx-x64,osx-arm64,win-x64
#   --configuration <c>  MSBuild configuration. Default: Release
#   --out <dir>          Output directory. Default: artifacts/
#   --skip-frontend      Skip npm build (wwwroot already built)
#   --help               Show this help
#
# Examples:
#   ./scripts/build.sh
#   ./scripts/build.sh --rids linux-x64
#   ./scripts/build.sh --skip-frontend --rids osx-arm64
# ============================================================

set -euo pipefail

# ── Defaults ─────────────────────────────────────────────────────────────────

RIDS="linux-x64 osx-x64 osx-arm64 win-x64"
CONFIGURATION="Release"
SKIP_FRONTEND=0
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
OUT_DIR="$REPO_ROOT/artifacts"

# ── Argument parsing ──────────────────────────────────────────────────────────

while [[ $# -gt 0 ]]; do
    case "$1" in
        --rids)
            # Accept comma-separated or space-separated list
            RIDS="${2//,/ }"
            shift 2
            ;;
        --configuration)
            CONFIGURATION="$2"
            shift 2
            ;;
        --out)
            OUT_DIR="$2"
            shift 2
            ;;
        --skip-frontend)
            SKIP_FRONTEND=1
            shift
            ;;
        --help|-h)
            sed -n '/^# Usage/,/^# ====/p' "$0" | grep '^#' | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            exit 1
            ;;
    esac
done

# ── Paths ─────────────────────────────────────────────────────────────────────

WEB_DIR="$REPO_ROOT/src/VRCGT.Web"
LAUNCH_PROJ="$REPO_ROOT/src/VRCGT.Launcher/VRCGT.Launcher.csproj"
CORE_SRC="$REPO_ROOT/src/VRCGT.Core/Class1.cs"

# ── Extract version ───────────────────────────────────────────────────────────

VERSION="unknown"
if [[ -f "$CORE_SRC" ]]; then
    VERSION=$(grep -oE 'AppVersion\s*=\s*"[^"]+"' "$CORE_SRC" | grep -oE '"[^"]+"' | tr -d '"' || true)
fi

# ── Banner ────────────────────────────────────────────────────────────────────

cyan='\033[0;36m'
green='\033[0;32m'
yellow='\033[0;33m'
red='\033[0;31m'
gray='\033[0;90m'
reset='\033[0m'

printf "\n${cyan}  ┌─────────────────────────────────────────────────┐\n"
printf "  │  VRCGT Build Script                             │\n"
printf "  │  Version   : %-35s│\n" "$VERSION"
printf "  │  Config    : %-35s│\n" "$CONFIGURATION"
printf "  │  Targets   : %-35s│\n" "${RIDS// /, }"
printf "  └─────────────────────────────────────────────────┘\n${reset}\n"

# ── Prerequisite checks ───────────────────────────────────────────────────────

require_cmd() {
    if ! command -v "$1" &>/dev/null; then
        echo -e "${red}ERROR: '$1' not found in PATH. Please install it and re-run.${reset}" >&2
        exit 1
    fi
}

require_cmd dotnet

if [[ $SKIP_FRONTEND -eq 0 ]]; then
    require_cmd node
    require_cmd npm
fi

# ── Step 1 — Build frontend ───────────────────────────────────────────────────

if [[ $SKIP_FRONTEND -eq 0 ]]; then
    printf "${yellow}[1/2] Building VRCGT.Web frontend…${reset}\n"
    pushd "$WEB_DIR" > /dev/null

    if [[ -f package-lock.json ]]; then
        npm ci --prefer-offline
    else
        npm install --prefer-offline
    fi

    npm run build
    popd > /dev/null
    printf "${green}    Frontend built.${reset}\n"
else
    printf "${gray}[1/2] Skipping frontend build (--skip-frontend).${reset}\n"
fi

# ── Step 2 — Publish per RID ──────────────────────────────────────────────────

printf "${yellow}[2/2] Publishing VRCGT.Launcher…${reset}\n"

SUCCEEDED=()
FAILED=()

for rid in $RIDS; do
    RID_OUT="$OUT_DIR/$rid"
    printf "\n${cyan}  ── %s ──${reset}\n" "$rid"

    dotnet publish "$LAUNCH_PROJ" \
        --configuration "$CONFIGURATION" \
        --runtime       "$rid" \
        --self-contained true \
        /p:SkipWebFrontend=true \
        /p:PublishReadyToRun=false \
        --output        "$RID_OUT" \
        --nologo \
        -v minimal

    if [[ $? -ne 0 ]]; then
        printf "${red}  Publish FAILED for %s${reset}\n" "$rid"
        FAILED+=("$rid")
        continue
    fi

    # ── VERSION.txt ──────────────────────────────────────────────────────────
    cat > "$RID_OUT/VERSION.txt" <<EOF
VRCGT $VERSION
Runtime target: $rid
Built: $(date '+%Y-%m-%d %H:%M:%S %z')
Configuration: $CONFIGURATION
EOF

    # ── README.txt ───────────────────────────────────────────────────────────
    if [[ "$rid" == win-* ]]; then
        EXE="VRCGT.Launcher.exe"
        cat > "$RID_OUT/README.txt" <<EOF
VRCGT $VERSION — Windows ($rid)
================================

How to run
----------
Double-click VRCGT.Launcher.exe
  — or —
Open a terminal in this folder and run:
    .\VRCGT.Launcher.exe

The app opens your default browser automatically to the local dashboard.
To stop, press Ctrl+C in the terminal, or close the terminal window.

Data is stored in:
    %LOCALAPPDATA%\VRCGT\

No installation, database server, or Node.js required.
EOF
    else
        EXE="VRCGT.Launcher"
        DATA_DIR="~/.local/share/VRCGT/"
        if [[ "$rid" == osx-* ]]; then
            DATA_DIR="~/Library/Application Support/VRCGT/"
        fi

        cat > "$RID_OUT/README.txt" <<EOF
VRCGT $VERSION — $rid
================================

How to run
----------
Open a terminal in this folder and run:

    chmod +x $EXE
    ./$EXE

The app opens your default browser automatically to the local dashboard.
To stop, press Ctrl+C.

Data is stored in:
    $DATA_DIR

No installation, database server, or Node.js required.
EOF
    fi

    printf "${green}    Published → %s${reset}\n" "$RID_OUT"
    SUCCEEDED+=("$rid")
done

# ── Summary ───────────────────────────────────────────────────────────────────

printf "\n${cyan}  ┌─────────────────────────────────────────────────┐\n"
printf "  │  Build summary                                  │\n"
for rid in "${SUCCEEDED[@]:-}"; do
    [[ -n "$rid" ]] && printf "${green}  │  ✓ %-45s│${reset}\n" "$rid"
done
for rid in "${FAILED[@]:-}"; do
    [[ -n "$rid" ]] && printf "${red}  │  ✗ %-45s│${reset}\n" "$rid"
done
printf "${cyan}  │  Output: %-39s│\n" "$OUT_DIR"
printf "  └─────────────────────────────────────────────────┘\n${reset}\n"

if [[ ${#FAILED[@]} -gt 0 ]]; then
    echo -e "${red}Build completed with ${#FAILED[@]} failure(s): ${FAILED[*]}${reset}" >&2
    exit 1
fi
