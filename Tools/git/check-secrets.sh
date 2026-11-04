#!/usr/bin/env bash
# Fails if anything that looks like a Google API key is tracked or staged.
# The pattern is written as AI[z]a so this file does not match itself.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"
pattern='AI[z]a[0-9A-Za-z_-]{20,}'
if git grep -l -E "$pattern" -- . ; then
  echo "ERROR: a Google API key appears in the files above (working tree). Remove it before committing." >&2
  exit 1
fi
if git grep --cached -l -E "$pattern" -- . ; then
  echo "ERROR: a Google API key is staged in the files above. Unstage it before committing." >&2
  exit 1
fi
for f in Assets/Config/secrets.json .env; do
  if git ls-files --error-unmatch "$f" >/dev/null 2>&1; then
    echo "ERROR: $f is tracked by git; it must stay ignored." >&2
    exit 1
  fi
done
echo "Secret scan clean."
