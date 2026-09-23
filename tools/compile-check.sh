#!/usr/bin/env bash
# MARK: ! AI disclaimer
# All code in this file was written by DeepSeek-V41-Flash
#
# compile-check.sh — type-check the Seasons C# without opening Unity.
#
# Usage:
#   tools/compile-check.sh [<worktree-root>] [--all]
#
#   <worktree-root>  Unity project to check. Defaults to the repo this script lives in.
#   --all            Print every warning, not just warnings in files you changed.
#
# What it does:
#   * Reads the Unity-generated csprojs in $SEASONS_UNITY_PROJECT (default
#     /home/mutant/seasons-game) for references, defines, analyzers and LangVersion.
#   * Compiles the worktree's *current* Assets/**/*.cs, so brand-new files count and
#     deleted or renamed ones drop out. No csprojs are needed inside the worktree.
#   * Three assemblies, in order, stopping at the first failure because the later
#     ones would just cascade:
#       firstpass = Assets/Plugins/** (the DOTween modules), built on DOTween.dll
#       runtime   = the rest of Assets/**, minus any Editor/ folder
#       editor    = any Assets/**/Editor/** , built on top of firstpass + runtime
#     That mirrors Unity's predefined-assembly rules. It assumes the project has no
#     asmdefs (there are none today); add one and this split will be wrong.
#
# Caveats:
#   * UNITY_EDITOR is defined for the runtime assembly, exactly as Unity's own
#     generated csproj does. So #if UNITY_EDITOR code is checked, but player-only
#     compile breakage is not.
#   * Scratch (response files + output dlls) goes to
#     $SEASONS_COMPILE_CACHE/<worktree-name>, else
#     ${XDG_CACHE_HOME:-~/.cache}/seasons-compile-check/<worktree-name>, else
#     ${TMPDIR:-/tmp}/seasons-compile-check/<worktree-name>. Nothing is written
#     into Assets/, Library/ or the main checkout.
#   * Only errors and warnings are printed, with paths relative to the worktree.
#     The last line is "compile-check: OK (firstpass, runtime, editor)" or
#     "compile-check: FAILED in <assembly>". Exit code is non-zero on any error.

set -u -o pipefail

UNITY_PROJECT="${SEASONS_UNITY_PROJECT:-/home/mutant/seasons-game}"

usage() {
    sed -n '3,7p' "$0"
}

ALL=0
ROOT=""

# MARK: args

while [ $# -gt 0 ]; do
    case "$1" in
        --all) ALL=1 ;;
        -h|--help) usage; exit 0 ;;
        -*)
            echo "compile-check: unknown option: $1" >&2
            usage >&2
            exit 2
            ;;
        *)
            if [ -n "$ROOT" ]; then
                echo "compile-check: too many worktree arguments" >&2
                exit 2
            fi
            ROOT="$1"
            ;;
    esac
    shift
done

if [ -z "$ROOT" ]; then
    ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
fi

if [ ! -d "$ROOT/Assets" ]; then
    echo "compile-check: $ROOT is not a Unity project (no Assets/ folder)" >&2
    exit 2
fi
ROOT="$(cd "$ROOT" && pwd)"

# MARK: source material

FIRSTPASS_CSPROJ="$UNITY_PROJECT/Assembly-CSharp-firstpass.csproj"
RUNTIME_CSPROJ="$UNITY_PROJECT/Assembly-CSharp.csproj"
EDITOR_CSPROJ="$UNITY_PROJECT/Assembly-CSharp-Editor.csproj"

for csproj in "$FIRSTPASS_CSPROJ" "$RUNTIME_CSPROJ" "$EDITOR_CSPROJ"; do
    if [ ! -f "$csproj" ]; then
        echo "compile-check: missing $csproj" >&2
        echo "compile-check: Unity version changed? regenerate csprojs by opening the main project in Unity" >&2
        exit 1
    fi
done

# MARK: scratch

scratch_dir() {
    local name="$1" dir
    local candidates=()
    if [ -n "${SEASONS_COMPILE_CACHE:-}" ]; then
        candidates+=("$SEASONS_COMPILE_CACHE/$name")
    fi
    candidates+=("${XDG_CACHE_HOME:-$HOME/.cache}/seasons-compile-check/$name")
    candidates+=("${TMPDIR:-/tmp}/seasons-compile-check/$name")
    for dir in "${candidates[@]}"; do
        # mkdir -p can "succeed" on a read-only mount when the dir already exists,
        # so actually write a file before trusting it.
        if mkdir -p "$dir" 2>/dev/null && ( : > "$dir/.write-test" ) 2>/dev/null; then
            rm -f "$dir/.write-test" 2>/dev/null
            printf '%s\n' "$dir"
            return 0
        fi
    done
    return 1
}

SCRATCH="$(scratch_dir "$(basename "$ROOT")")"
if [ -z "$SCRATCH" ]; then
    echo "compile-check: could not create a scratch directory" >&2
    exit 1
fi
rm -rf "$SCRATCH"
if ! mkdir -p "$SCRATCH" 2>/dev/null || ! ( : > "$SCRATCH/.write-test" ) 2>/dev/null; then
    echo "compile-check: scratch directory is not writable: $SCRATCH" >&2
    exit 1
fi
rm -f "$SCRATCH/.write-test"

# MARK: compiler

CSC_CMD=()
if command -v dotnet >/dev/null 2>&1; then
    DOTNET_BIN="$(command -v dotnet)"
    DOTNET_DIR="$(cd "$(dirname "$(readlink -f "$DOTNET_BIN")")" && pwd)"
    CSC_DLL="$(ls -1 "$DOTNET_DIR"/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | sort -V | tail -1)"
    if [ -n "$CSC_DLL" ]; then
        CSC_CMD=(dotnet "$CSC_DLL")
    fi
fi
if [ "${#CSC_CMD[@]}" -eq 0 ]; then
    if command -v csc >/dev/null 2>&1; then
        CSC_CMD=(csc)
    else
        echo "compile-check: found no C# compiler (dotnet SDK Roslyn csc or csc)" >&2
        exit 1
    fi
fi

# MARK: references

MISSING_REFS=()
USED_MAIN_LIBRARY=0
USED_MAIN_PROJECT=0
NOTED_MAIN_LIBRARY=0
NOTED_MAIN_PROJECT=0

# Appends -r: lines to $2. Absolute HintPaths must exist; relative ones resolve
# against the worktree first, then against the main checkout, which is where
# Unity wrote them. Relative paths are read-only there, never written.
resolve_refs() {
    local csproj="$1" out="$2" path
    while IFS= read -r path; do
        [ -n "$path" ] || continue
        case "$path" in
            /*)
                if [ -f "$path" ]; then
                    printf -- '-r:"%s"\n' "$path" >> "$out"
                else
                    MISSING_REFS+=("$path")
                fi
                ;;
            *)
                if [ -f "$ROOT/$path" ]; then
                    printf -- '-r:"%s"\n' "$ROOT/$path" >> "$out"
                elif [ -f "$UNITY_PROJECT/$path" ]; then
                    case "$path" in
                        Library/*) USED_MAIN_LIBRARY=1 ;;
                        *) USED_MAIN_PROJECT=1 ;;
                    esac
                    printf -- '-r:"%s"\n' "$UNITY_PROJECT/$path" >> "$out"
                else
                    MISSING_REFS+=("$path")
                fi
                ;;
        esac
    done < <(grep -o '<HintPath>[^<]*</HintPath>' "$csproj" | sed 's#<HintPath>##;s#</HintPath>##')
}

note_main_checkout_refs() {
    if [ "$USED_MAIN_LIBRARY" = 1 ] && [ "$NOTED_MAIN_LIBRARY" = 0 ]; then
        echo "compile-check: using $UNITY_PROJECT/Library for package refs" >&2
        NOTED_MAIN_LIBRARY=1
    fi
    if [ "$USED_MAIN_PROJECT" = 1 ] && [ "$NOTED_MAIN_PROJECT" = 0 ]; then
        echo "compile-check: using $UNITY_PROJECT for missing relative refs" >&2
        NOTED_MAIN_PROJECT=1
    fi
    USED_MAIN_LIBRARY=0
    USED_MAIN_PROJECT=0
}

fail_missing_refs() {
    local path
    for path in "${MISSING_REFS[@]}"; do
        echo "compile-check: missing reference: $path" >&2
    done
    echo "compile-check: Unity version changed? regenerate csprojs by opening the main project in Unity" >&2
    exit 1
}

analyzer_lines() {
    local csproj="$1" path
    while IFS= read -r path; do
        [ -n "$path" ] || continue
        if [ -f "$path" ]; then
            printf -- '-analyzer:"%s"\n' "$path"
        else
            echo "compile-check: skipping missing analyzer: $path" >&2
        fi
    done < <(grep -o '<Analyzer Include="[^"]*"' "$csproj" | sed 's#<Analyzer Include="##;s#"$##')
}

define_constants() {
    grep -o '<DefineConstants>[^<]*' "$1" | sed 's#<DefineConstants>##'
}

# MARK: sources

firstpass_sources() {
    if [ -d "$ROOT/Assets/Plugins" ]; then
        find "$ROOT/Assets/Plugins" -name '*.cs' -not -path '*/Editor/*'
    fi
}

runtime_sources() {
    find "$ROOT/Assets" -name '*.cs' -not -path '*/Editor/*' -not -path "$ROOT/Assets/Plugins/*"
}

editor_sources() {
    find "$ROOT/Assets" -name '*.cs' -path '*/Editor/*'
}

# MARK: warning filter

CHANGED_FILE="$SCRATCH/changed-files.txt"
UNCHANGED_WARNINGS=0

# Anything that differs from main plus staged, unstaged and untracked work. A new
# file is "changed" by definition, which is what we want: warnings there are ours.
build_changed_list() {
    local base="" ref
    for ref in main master; do
        if git -C "$ROOT" rev-parse --verify --quiet "$ref" >/dev/null 2>&1; then
            base="$ref"
            break
        fi
    done
    {
        if [ -n "$base" ]; then
            git -C "$ROOT" diff --name-only "$base...HEAD" 2>/dev/null
        fi
        git -C "$ROOT" diff --name-only 2>/dev/null
        git -C "$ROOT" diff --name-only --cached 2>/dev/null
        git -C "$ROOT" ls-files --others --exclude-standard 2>/dev/null
    } | LC_ALL=C sort -u > "$CHANGED_FILE"
}

report_log() {
    local log="$1" line file
    while IFS= read -r line; do
        case "$line" in
            ''|'Microsoft (R)'*|'Copyright (C)'*) continue ;;
        esac
        case "$line" in
            *': warning '*)
                file="${line%%(*}"
                if [ "$ALL" = 1 ] || grep -qxF "$file" "$CHANGED_FILE" 2>/dev/null; then
                    printf '%s\n' "$line"
                else
                    UNCHANGED_WARNINGS=$((UNCHANGED_WARNINGS + 1))
                fi
                ;;
            *)
                printf '%s\n' "$line"
                ;;
        esac
    done <<< "$log"
}

# MARK: build

# compile_assembly <name> <csproj> <out-dll> [extra refs...]
compile_assembly() {
    local name="$1" csproj="$2" out="$3"
    shift 3

    local srcs="$SCRATCH/$name.sources"
    "${name}_sources" | LC_ALL=C sort > "$srcs"
    if [ ! -s "$srcs" ]; then
        # Later assemblies still reference this one, so emit an empty stub dll
        # instead of leaving a dangling -r: path.
        echo "compile-check: no source files for $name, compiling an empty stub" >&2
        printf 'internal static class CompileCheckEmpty_%s { }\n' "$name" > "$SCRATCH/$name.empty.cs"
        printf '%s\n' "$SCRATCH/$name.empty.cs" > "$srcs"
    fi

    MISSING_REFS=()
    local refs_raw="$SCRATCH/$name.refs.raw" refs="$SCRATCH/$name.refs"
    : > "$refs_raw"
    resolve_refs "$csproj" "$refs_raw"
    note_main_checkout_refs
    if [ "${#MISSING_REFS[@]}" -gt 0 ]; then
        fail_missing_refs
    fi
    LC_ALL=C sort -u "$refs_raw" > "$refs"

    local rsp="$SCRATCH/$name.rsp" extra
    {
        printf -- '-nologo\n'
        printf -- '-target:library\n'
        printf -- '-langversion:9.0\n'
        # 0649 is [SerializeField] noise: Unity assigns those fields, Roslyn can't see it.
        printf -- '-nowarn:0169,USG0001,0649\n'
        printf -- '-out:"%s"\n' "$out"
        cat "$refs"
        for extra in "$@"; do
            printf -- '-r:"%s"\n' "$extra"
        done
        printf -- '-define:%s\n' "$(define_constants "$csproj")"
        analyzer_lines "$csproj"
        while IFS= read -r f; do
            printf '"%s"\n' "$f"
        done < "$srcs"
    } > "$rsp"

    local log rc
    log="$("${CSC_CMD[@]}" @"$rsp" 2>&1)"
    rc=$?
    log="$(printf '%s\n' "$log" | sed "s|$ROOT/||g; s|$SCRATCH/|scratch/|g")"
    report_log "$log"
    return $rc
}

build_changed_list

# MARK: run

FAILED=""
if ! compile_assembly firstpass "$FIRSTPASS_CSPROJ" "$SCRATCH/Assembly-CSharp-firstpass.dll"; then
    FAILED="firstpass"
elif ! compile_assembly runtime "$RUNTIME_CSPROJ" "$SCRATCH/Assembly-CSharp.dll" "$SCRATCH/Assembly-CSharp-firstpass.dll"; then
    FAILED="runtime"
elif ! compile_assembly editor "$EDITOR_CSPROJ" "$SCRATCH/Assembly-CSharp-Editor.dll" "$SCRATCH/Assembly-CSharp-firstpass.dll" "$SCRATCH/Assembly-CSharp.dll"; then
    FAILED="editor"
fi

if [ "$UNCHANGED_WARNINGS" -gt 0 ]; then
    echo "compile-check: (+$UNCHANGED_WARNINGS warnings in unchanged files)"
fi

if [ -n "$FAILED" ]; then
    echo "compile-check: FAILED in $FAILED"
    exit 1
fi

echo "compile-check: OK (firstpass, runtime, editor)"
exit 0
