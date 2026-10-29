#!/usr/bin/env bash
# Builds dnSpy on Linux/macOS.
#
#   ./build.sh [console]          Build the cross-platform dnSpy.Console (net10.0). This is the default.
#                                 It's framework dependent and needs a .NET 10 runtime to run.
#   ./build.sh publish [rid...]   Publish a self-contained dnSpy.Console for each runtime identifier,
#                                 eg. linux-x64, linux-arm64, linux-musl-x64, osx-x64, osx-arm64, win-x64,
#                                 win-x86, win-arm64. Defaults to the current OS/arch. The output is in
#                                 dnSpy/dnSpy/bin/Release/net10.0/<rid>/publish and doesn't need .NET installed.
#                                 osx-* must be published on macOS so the executable gets signed.
#   ./build.sh avalonia           Build the cross-platform dnSpy.Avalonia browser (net10.0, preview). It's
#                                 framework dependent like dnSpy.Console and is in the same output dir.
#   ./build.sh avalonia-publish [rid...]
#                                 Publish a self-contained dnSpy.Avalonia, same rids and output dir as 'publish'.
#   ./build.sh avalonia-test [--screenshots dir]
#                                 Build and run the dnSpy.Avalonia smoke test. It drives the real window with
#                                 Avalonia's headless platform, no display is needed.
#   ./build.sh all                Build the whole solution and dnSpy.Avalonia. The WPF GUI targets (net48,
#                                 net10.0-windows) are compiled but only run on Windows.
#
# Set DOTNET to use a .NET 10 SDK that isn't the first 'dotnet' in PATH, eg. DOTNET=~/.dotnet/dotnet
set -euo pipefail

cd "$(dirname "$0")"

configuration=Release
cross_platform_tfm=net10.0
required_sdk_major=10
console_project=dnSpy/dnSpy.Console/dnSpy.Console.csproj
avalonia_project=dnSpy/dnSpy.Avalonia/dnSpy.Avalonia.csproj
avalonia_test_project=dnSpy/dnSpy.Avalonia.SmokeTest/dnSpy.Avalonia.SmokeTest.csproj
console_baseoutput=dnSpy/dnSpy/bin/$configuration/$cross_platform_tfm

has_required_sdk() {
	command -v "$1" > /dev/null 2>&1 && "$1" --list-sdks 2>/dev/null | grep -q "^$required_sdk_major\."
}

host_rid() {
	local os arch
	case "$(uname -s)" in
	Linux)
		os=linux
		if ldd --version 2>&1 | grep -qi musl; then
			os=linux-musl
		fi
		;;
	Darwin) os=osx ;;
	*) echo "Unknown OS '$(uname -s)', pass a runtime identifier" >&2; return 1 ;;
	esac
	case "$(uname -m)" in
	x86_64 | amd64) arch=x64 ;;
	aarch64 | arm64) arch=arm64 ;;
	armv7l) arch=arm ;;
	*) echo "Unknown architecture '$(uname -m)', pass a runtime identifier" >&2; return 1 ;;
	esac
	echo "$os-$arch"
}

dotnet=${DOTNET:-}
if [ -z "$dotnet" ]; then
	for candidate in dotnet "$HOME/.dotnet/dotnet"; do
		if has_required_sdk "$candidate"; then
			dotnet=$candidate
			break
		fi
	done
fi
if [ -z "$dotnet" ] || ! has_required_sdk "$dotnet"; then
	echo "A .NET $required_sdk_major SDK is required. Install it or set DOTNET to its 'dotnet' executable." >&2
	exit 1
fi
# Set when the SDK isn't the .NET install that the dnSpy.Console apphost finds by default
dotnet_root_hint=
if [ -z "${DOTNET_ROOT:-}" ]; then
	DOTNET_ROOT=$(dirname "$(readlink -f "$(command -v "$dotnet")")")
	export DOTNET_ROOT
	if [ "$(dirname "$(readlink -f "$(command -v dotnet 2>/dev/null || echo /)")")" != "$DOTNET_ROOT" ]; then
		dotnet_root_hint=$DOTNET_ROOT
	fi
fi

command=${1:-console}
[ $# -gt 0 ] && shift

case "$command" in
console)
	"$dotnet" build -v:m -c "$configuration" -f "$cross_platform_tfm" "$console_project"
	echo "Built $console_baseoutput/dnSpy.Console"
	if [ -n "$dotnet_root_hint" ]; then
		echo "The .NET $required_sdk_major runtime isn't your default 'dotnet'. Run it with DOTNET_ROOT=$dotnet_root_hint or use './build.sh publish' to get a self-contained build."
	fi
	;;
avalonia)
	"$dotnet" build -v:m -c "$configuration" -f "$cross_platform_tfm" "$avalonia_project"
	echo "Built $console_baseoutput/dnSpy.Avalonia"
	if [ -n "$dotnet_root_hint" ]; then
		echo "The .NET $required_sdk_major runtime isn't your default 'dotnet'. Run it with DOTNET_ROOT=$dotnet_root_hint or use './build.sh avalonia-publish' to get a self-contained build."
	fi
	;;
avalonia-test)
	"$dotnet" build -v:m -c "$configuration" -f "$cross_platform_tfm" "$avalonia_test_project"
	"dnSpy/dnSpy.Avalonia.SmokeTest/bin/$configuration/$cross_platform_tfm/dnSpy.Avalonia.SmokeTest" "$@"
	;;
publish | avalonia-publish)
	project=$console_project
	exe=dnSpy.Console
	if [ "$command" = avalonia-publish ]; then
		project=$avalonia_project
		exe=dnSpy.Avalonia
	fi
	# Avoid expanding an empty array, macOS' bash 3.2 treats it as unbound with 'set -u'
	if [ $# -eq 0 ]; then
		rids=("$(host_rid)")
	else
		rids=("$@")
	fi
	for rid in "${rids[@]}"; do
		if [[ "$rid" == osx-* && "$(uname -s)" != Darwin ]]; then
			echo "Warning: $rid isn't being published on macOS so $exe won't be signed. macOS on Apple silicon refuses to run unsigned executables." >&2
		fi
		"$dotnet" publish -v:m -c "$configuration" -f "$cross_platform_tfm" -r "$rid" --self-contained "$project"
		echo "Published $console_baseoutput/$rid/publish"
	done
	;;
all)
	"$dotnet" build -v:m -c "$configuration" dnSpy.sln
	# dnSpy.sln doesn't build it since it only targets net10.0
	"$dotnet" build -v:m -c "$configuration" -f "$cross_platform_tfm" "$avalonia_project"
	;;
*)
	echo "Usage: $0 [console | publish [rid...] | avalonia | avalonia-publish [rid...] | avalonia-test [--screenshots dir] | all]" >&2
	exit 1
	;;
esac
