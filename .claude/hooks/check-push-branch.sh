#!/usr/bin/env bash
# PreToolUse hook: blocks `git push` unless current branch matches feat/claude-<title>.
# Reads tool input JSON from stdin and self-gates on `git push` commands.

input=$(cat)

# Only act on Bash commands that start with `git push`. Allow everything else.
case "$input" in
  *'"command":"git push'*|*'"command": "git push'*) ;;
  *) exit 0 ;;
esac

branch=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || true)

deny() {
  printf '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"%s"}}' "$1"
  exit 0
}

if [ -z "$branch" ] || [ "$branch" = "HEAD" ]; then
  deny "Cannot push: not on a branch (detached HEAD)."
fi

if [ "$branch" = "main" ] || [ "$branch" = "master" ]; then
  deny "Cannot push to '$branch'. Switch to a branch named feat/claude-<title> first."
fi

case "$branch" in
  feat/claude-*) exit 0 ;;
  *) deny "Branch '$branch' does not match feat/claude-<title>. Rename or switch branches before pushing." ;;
esac
