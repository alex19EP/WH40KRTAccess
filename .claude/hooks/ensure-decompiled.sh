#!/usr/bin/env bash
# ensure-decompiled.sh — RTAccess SessionStart hook.
#
# The decompiled/ game-reference dir is git-ignored (regenerable via the
# justfile), so it never checks out into a git worktree. This links it back to
# the main checkout's copy via a Windows directory junction (mklink /J — needs
# no admin). Idempotent: a silent no-op in the main checkout and in any worktree
# that already has decompiled/. Any failure is non-fatal (hook just prints).
set -u

# Claude exports CLAUDE_PROJECT_DIR to hook processes; fall back to CWD.
raw="${CLAUDE_PROJECT_DIR:-$PWD}"
proj="$(cygpath -u "$raw" 2>/dev/null || printf '%s' "$raw")"

# Already present (real dir, existing junction, or copy)? Nothing to do.
[ -e "$proj/decompiled" ] && exit 0

# The shared .git of the worktree set lives in the main checkout; its parent is
# the main checkout root.
common="$(git -C "$proj" rev-parse --git-common-dir 2>/dev/null)" || exit 0
common="$(cygpath -u "$common" 2>/dev/null || printf '%s' "$common")"
case "$common" in
  /*) ;;                        # absolute
  *)  common="$proj/$common" ;; # relative to the project dir
esac
main="$(cd "$(dirname "$common")" 2>/dev/null && pwd)" || exit 0

# In the main checkout itself, or the source dir is absent -> nothing to link.
[ "$main" = "$proj" ] && exit 0
[ -d "$main/decompiled" ] || exit 0

# Create the junction. MSYS2_ARG_CONV_EXCL keeps /J from being path-mangled.
link="$(cygpath -w "$proj/decompiled")"
target="$(cygpath -w "$main/decompiled")"
if MSYS2_ARG_CONV_EXCL='*' cmd.exe /c mklink /J "$link" "$target" >/dev/null 2>&1; then
  printf '[RTAccess] linked decompiled/ -> %s\n' "$main/decompiled"
fi
exit 0
