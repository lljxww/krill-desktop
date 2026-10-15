#!/bin/bash
set -euo pipefail
if [[ "$(uname -s)" != Darwin ]]; then echo "Verification requires macOS and Xcode." >&2; exit 1; fi
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
package_dir="$repo_root/apps/macos"
swift --version
/usr/bin/plutil -lint "$package_dir/Support/Info.plist"
swift test --package-path "$package_dir"
swift build --package-path "$package_dir" -c release --product KrillMac
echo "PASS Swift core tests and macOS application compilation. GUI/login still require manual verification."
