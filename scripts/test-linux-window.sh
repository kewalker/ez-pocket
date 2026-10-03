#!/usr/bin/env bash
set -euo pipefail

mkdir -p artifacts/linux-ui
GDK_BACKEND=x11 GTK_A11Y=none dotnet Linux/bin/Debug/net10.0/EzPocket.Linux.dll &
app_pid=$!
trap 'kill "$app_pid" 2>/dev/null || true' EXIT

window_id=""
for attempt in {1..30}; do
    window_id="$(xdotool search --onlyvisible --name '^EzPocket$' 2>/dev/null | head -n 1 || true)"
    if [ -n "$window_id" ]; then break; fi
    if ! kill -0 "$app_pid" 2>/dev/null; then
        echo "Linux app exited before opening a window." >&2
        exit 1
    fi
    sleep 1
done

if [ -z "$window_id" ]; then
    echo "Linux app did not open a window." >&2
    exit 1
fi

sleep 2
scrot -o artifacts/linux-ui/home.png
xdotool windowsize "$window_id" 860 640
sleep 2
scrot -o artifacts/linux-ui/resized.png

geometry="$(xdotool getwindowgeometry --shell "$window_id")"
width="$(sed -n 's/^WIDTH=//p' <<< "$geometry")"
height="$(sed -n 's/^HEIGHT=//p' <<< "$geometry")"
echo "Resized GTK window: ${width}x${height}"
if [ "$width" -gt 900 ] || [ "$height" -gt 680 ]; then
    echo "GTK window did not shrink to the requested size." >&2
    exit 1
fi

kill "$app_pid"
wait "$app_pid" 2>/dev/null || true

EZPOCKET_NAVIGATION_SMOKE_MARKER="$PWD/artifacts/linux-ui/navigation-ok.txt" \
    GDK_BACKEND=x11 GTK_A11Y=none dotnet Linux/bin/Debug/net10.0/EzPocket.Linux.dll &
app_pid=$!
for attempt in {1..20}; do
    if [ -f artifacts/linux-ui/navigation-ok.txt ]; then break; fi
    if ! kill -0 "$app_pid" 2>/dev/null; then
        echo "Linux app exited during navigation." >&2
        exit 1
    fi
    sleep 1
done

if [ ! -f artifacts/linux-ui/navigation-ok.txt ]; then
    echo "Linux navigation did not reach CorePage." >&2
    exit 1
fi
sleep 2
scrot -o artifacts/linux-ui/navigated.png

kill "$app_pid"
wait "$app_pid" 2>/dev/null || true

EZPOCKET_INVENTORY_SMOKE_MARKER="$PWD/artifacts/linux-ui/inventory-ok.txt" \
    EZPOCKET_INVENTORY_FIXTURE_PATH="$PWD/Tests/Fixtures/core-inventory-linux-smoke.json" \
    GDK_BACKEND=x11 GTK_A11Y=none dotnet Linux/bin/Debug/net10.0/EzPocket.Linux.dll &
app_pid=$!
for attempt in {1..55}; do
    if [ -f artifacts/linux-ui/inventory-ok.txt ]; then break; fi
    if ! kill -0 "$app_pid" 2>/dev/null; then
        echo "Linux app exited while loading core inventory." >&2
        wait "$app_pid" || true
        exit 1
    fi
    sleep 1
done

if [ ! -f artifacts/linux-ui/inventory-ok.txt ]; then
    echo "Linux core inventory did not remain open." >&2
    exit 1
fi
scrot -o artifacts/linux-ui/inventory.png
inventory_window_id="$(xdotool search --onlyvisible --name '^EzPocket$' 2>/dev/null | head -n 1 || true)"
if [ -z "$inventory_window_id" ]; then
    echo "Populated inventory window is not visible." >&2
    exit 1
fi
xdotool windowsize "$inventory_window_id" 860 640
sleep 2
scrot -o artifacts/linux-ui/inventory-resized.png
xdotool windowsize "$inventory_window_id" 760 480
sleep 2
small_geometry="$(xdotool getwindowgeometry --shell "$inventory_window_id")"
small_width="$(sed -n 's/^WIDTH=//p' <<< "$small_geometry")"
small_height="$(sed -n 's/^HEIGHT=//p' <<< "$small_geometry")"
echo "Small GTK inventory window: ${small_width}x${small_height}"
if [ "$small_width" -gt 800 ] || [ "$small_height" -gt 520 ]; then
    echo "GTK inventory window did not shrink to the small size." >&2
    exit 1
fi
scrot -o artifacts/linux-ui/inventory-small.png

kill "$app_pid"
wait "$app_pid" 2>/dev/null || true

EZPOCKET_RETURN_SMOKE_MARKER="$PWD/artifacts/linux-ui/return-ok.txt" \
    EZPOCKET_INVENTORY_FIXTURE_PATH="$PWD/Tests/Fixtures/core-inventory-linux-smoke.json" \
    GDK_BACKEND=x11 GTK_A11Y=none dotnet Linux/bin/Debug/net10.0/EzPocket.Linux.dll &
app_pid=$!
for attempt in {1..30}; do
    if [ -f artifacts/linux-ui/return-ok.txt.review ]; then break; fi
    if ! kill -0 "$app_pid" 2>/dev/null; then
        echo "Linux app exited before review opened." >&2
        exit 1
    fi
    sleep 1
done
if [ ! -f artifacts/linux-ui/return-ok.txt.review ]; then
    echo "Linux core review did not open." >&2
    exit 1
fi
return_window_id="$(xdotool search --onlyvisible --name '^EzPocket$' 2>/dev/null | head -n 1 || true)"
if [ -z "$return_window_id" ]; then
    echo "Linux review window is not visible." >&2
    exit 1
fi
xdotool windowsize "$return_window_id" 760 480
sleep 2
scrot -o artifacts/linux-ui/review-small.png
touch artifacts/linux-ui/return-ok.txt.back
for attempt in {1..35}; do
    if [ -f artifacts/linux-ui/return-ok.txt ]; then break; fi
    if ! kill -0 "$app_pid" 2>/dev/null; then
        echo "Linux app exited while returning to inventory." >&2
        exit 1
    fi
    sleep 1
done
if [ ! -f artifacts/linux-ui/return-ok.txt ]; then
    echo "Linux inventory did not return after review." >&2
    exit 1
fi
scrot -o artifacts/linux-ui/returned-inventory.png
