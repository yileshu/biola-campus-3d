#!/usr/bin/env bash
# Installs a pre-commit hook that runs the secret scan before every commit.
set -euo pipefail
root="$(git rev-parse --show-toplevel)"
hook="$root/.git/hooks/pre-commit"
cat > "$hook" <<'HOOK'
#!/usr/bin/env bash
exec "$(git rev-parse --show-toplevel)/Tools/git/check-secrets.sh"
HOOK
chmod +x "$hook"
echo "Installed $hook"
