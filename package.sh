#!/bin/bash
set -euf -o pipefail

zip_name=""
dst=""
dst_css=""

setup_zip() {
	zip_name="$1"
	dst="./artifacts/$zip_name"
	dst_css="$dst/addons/counterstrikesharp"
}

copy_dir() {
	mkdir -p "$dst_css/$2/"
	cp -r "$1/." "$dst_css/$2/"
}

move_file() {
	mkdir -p "$dst_css/$2/"
	mv "$dst_css/$1" "$dst_css/$2/"
}

commit_zip() {
	pushd "$dst"
	7z a "../$zip_name.zip" ./
	popd
	rm -rf "$dst"
}

setup_zip SharpModMenu
copy_dir src/SharpModMenu/bin/Release/net8.0/publish plugins/SharpModMenu
commit_zip
