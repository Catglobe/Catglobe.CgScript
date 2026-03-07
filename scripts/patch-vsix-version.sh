#!/usr/bin/env sh
set -eu

VERSION="$1"
DEST="cgscript-vs-$VERSION.vsix"
WORKDIR=$(mktemp -d)
DESTABS="$(pwd)/$DEST"

unzip -q vs-extension.vsix -d "$WORKDIR"
sed -i 's/\(<Identity\b[^>]*\bVersion="\)[^"]*"/\1'"$VERSION"'"/' "$WORKDIR/extension.vsixmanifest"
(cd "$WORKDIR" && zip -qr "$DESTABS" .)
rm -rf "$WORKDIR"

echo "Created $DEST"
