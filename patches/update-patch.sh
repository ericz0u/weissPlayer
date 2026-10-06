#!/bin/bash
# Keeps engine-hooks.patch in sync with your edits to the simulator's own files.
#
#   bash patches/update-patch.sh                 regenerate the patch after editing patched files
#   bash patches/update-patch.sh --add <file>    start tracking another simulator file (run BEFORE editing it)
#
# Unmodified originals are cached in patches/.originals/ (not committed, so no simulator code is published).
# If the cache is missing, e.g. on a fresh clone, it is rebuilt by reverse-applying the current patch.
set -e
cd "$(dirname "$0")/.."
PATCH=patches/engine-hooks.patch
ORIG=patches/.originals

tracked_files(){
	grep '^diff --git' "$PATCH" | awk '{print substr($3, 3)}'
	[ -f "$ORIG/.extra" ] && cat "$ORIG/.extra"
}

# Rebuild the cache of originals from the current files and patch.
if [ ! -d "$ORIG" ]; then
	mkdir -p "$ORIG"
	for f in $(grep '^diff --git' "$PATCH" | awk '{print substr($3, 3)}'); do
		mkdir -p "$ORIG/$(dirname "$f")"
		cp "$f" "$ORIG/$f"
	done
	(cd "$ORIG" && patch -p1 -R -s --no-backup-if-mismatch < "../../$PATCH")
	echo "Rebuilt $ORIG from the current patch"
fi

if [ "$1" == "--add" ]; then
	[ -f "$2" ] || { echo "No such file: $2"; exit 1; }
	mkdir -p "$ORIG/$(dirname "$2")"
	cp "$2" "$ORIG/$2"
	echo "$2" >> "$ORIG/.extra"
	echo "Saved the original of $2. Edit it, then run this script again without arguments."
	exit 0
fi

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
(cd "$TMP" && git init -q)
for f in $(tracked_files | sort -u); do
	mkdir -p "$TMP/$(dirname "$f")"
	cp "$ORIG/$f" "$TMP/$f"
done
# Some simulator files are marked executable; ignore modes so the patch only holds content changes.
(cd "$TMP" && git config core.fileMode false && git -c core.autocrlf=false add -A && git -c user.email=x@x -c user.name=x commit -qm original)
for f in $(tracked_files | sort -u); do
	cp "$f" "$TMP/$f"
done
(cd "$TMP" && git -c core.autocrlf=false diff) > "$PATCH"
echo "Updated $PATCH ($(grep -c '^diff --git' "$PATCH") files)"
