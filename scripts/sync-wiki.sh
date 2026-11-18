#!/bin/bash
# ================================================================
# NCMapper Wiki Sync Script
# ================================================================
# Syncs docs/wiki/ to GitHub + Gitee Wiki repositories.
# Usage: bash scripts/sync-wiki.sh
# ================================================================
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
WIKI_SRC="$REPO_ROOT/docs/wiki"

# --- Configuration ---
GITHUB_WIKI_REPO="git@github.com:USER/NCMapper.wiki.git"
GITEE_WIKI_REPO="git@gitee.com:USER/NCMapper.wiki.git"

TMP_DIR=$(mktemp -d)
trap 'rm -rf "$TMP_DIR"' EXIT

echo "=== NCMapper Wiki Sync ==="
echo "Source: $WIKI_SRC"

# Check if wiki source exists
if [ ! -d "$WIKI_SRC" ]; then
    echo "ERROR: Wiki source directory not found: $WIKI_SRC"
    exit 1
fi

sync_to_wiki() {
    local repo_url="$1"
    local name="$2"

    echo ""
    echo "--- Syncing to $name ---"

    git clone --depth 1 "$repo_url" "$TMP_DIR/$name" 2>/dev/null || {
        echo "WARNING: Failed to clone $name wiki. Skipping."
        return 0
    }

    cd "$TMP_DIR/$name"

    # Remove existing files (keep .git)
    find . -maxdepth 1 -not -name '.git' -not -name '.' -exec rm -rf {} \;

    # Copy wiki source files (recursive — wiki 内容在 zh-CN/ 子目录，顶层无 .md；
    # 原 glob 只拷顶层，set -e 下 cp 失败同步永远无法完成)
    cp -r "$WIKI_SRC"/. .

    # Add all and commit
    git add -A
    if git diff --cached --quiet; then
        echo "No changes to $name wiki."
    else
        git commit -m "sync wiki $(date +%Y-%m-%d)"
        git push origin master || {
            echo "ERROR: Failed to push to $name wiki. Check credentials."
            return 1
        }
        echo "Successfully synced to $name wiki."
    fi
}

# Read-only notice
echo ""
echo "NOTE: This script is read-only for the source repo."
echo "      Edits to wiki pages should be made in docs/wiki/ within the main repo."

# Sync to both wikis
# Uncomment when credentials are configured
# sync_to_wiki "$GITHUB_WIKI_REPO" "GitHub"
# sync_to_wiki "$GITEE_WIKI_REPO" "Gitee"

echo ""
echo "=== Wiki sync complete ==="
echo "Next: Uncomment sync_to_wiki lines after configuring wiki repo URLs."
