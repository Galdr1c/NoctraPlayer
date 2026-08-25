#!/bin/bash
# =============================================================================
# Rebuilds Noctra.Android/libs/ffmpeg/{arm64-v8a,x86_64}/libffmpegJNI.so with
# 16-KB ELF page alignment (-Wl,-z,max-page-size=16384).
#
# Required for Android 15+ devices running with a 16-KB page size; Google Play
# rejects updates containing non-16-KB-aligned native libs from Feb 2027.
# XA0141 must stay removed from NoWarn in Noctra.Android.csproj — fix the
# binary instead if the warning ever reappears.
#
# Prerequisites (run from repo root):
#   - Android NDK r26b at %LOCALAPPDATA%/Android/Sdk/ndk/26.1.10909125
#     (adjust NDK below if different; must match the toolchain of prior builds)
#   - Git Bash on Windows (uses cygpath + NDK .cmd wrappers), `make` on PATH
#
# Usage:
#   bash tools/android/build-16k.sh
# Then copy outputs over the vendored files:
#   cp .ffmpeg-build/out/<abi>/libffmpegJNI.so Noctra.Android/libs/ffmpeg/<abi>/
#
# IMPORTANT: link with -static-libstdc++. A shared libc++ adds a
# libc++_shared.so DT_NEEDED entry that the app does not package, so dlopen
# fails at runtime and the FFmpeg extension silently disappears (EAC3 etc.
# then fail as NO_UNSUPPORTED_TYPE in MediaCodec).
#
# The JNI wrapper source is taken from media3 decoder_ffmpeg @1.4.1 — keep it in
# sync with the ffmpeg-extension.jar version packaged via <JavaLibrary>.
# =============================================================================
set -eu

ROOT="$(pwd)"
WORK="$ROOT/.ffmpeg-build"
JNI_SRC="media3/libraries/decoder_ffmpeg/src/main/jni"
NDK="${LOCALAPPDATA}/Android/Sdk/ndk/26.1.10909125"
TOOLCHAIN="$(cygpath -u "$NDK")/toolchains/llvm/prebuilt/windows-x86_64/bin"
API=28   # Noctra.Android SupportedOSPlatformVersion
OUT="$WORK/out"
JOBS=$(nproc)

# Same decoder set as FfmpegLibrary.getCodecName advertises (keep in sync).
DECODERS=(aac mp3 ac3 eac3 truehd dca vorbis opus amrnb amrwb flac alac pcm_mulaw pcm_alaw h264 hevc)

mkdir -p "$WORK" && cd "$WORK"

[ -d media3 ] || git clone --depth 1 --branch 1.4.1 https://github.com/androidx/media.git media3
[ -d ffmpeg-6.0.1 ] || { curl -sL -o ffmpeg.tar.xz https://www.ffmpeg.org/releases/ffmpeg-6.0.1.tar.xz && tar xf ffmpeg.tar.xz; }

COMMON_OPTIONS=(
  --target-os=android
  --enable-cross-compile   # prevents configure from executing Android test binaries
  --enable-static
  --disable-shared
  --disable-doc
  --disable-programs
  --disable-everything
  --disable-avdevice
  --disable-avformat
  --disable-swscale
  --disable-postproc
  --disable-avfilter
  --disable-symver
  --enable-swresample
  --extra-ldexeflags=-pie
  --extra-ldflags="-Wl,-z,max-page-size=16384"
  --disable-v4l2-m2m
  --disable-vulkan
)
for d in "${DECODERS[@]}"; do
  COMMON_OPTIONS+=(--enable-decoder="$d")
done

build_ffmpeg () { # $1=abi  $2=target triple prefix  $3=arch  $4=cpu  $5=extra configure opts...
  local abi="$1" tgt="$2" arch="$3" cpu="$4"; shift 4
  echo "=== FFmpeg configure/build for $abi ==="
  cd "$WORK/ffmpeg-6.0.1"
  ./configure \
    --prefix="$OUT/prefix/$abi" \
    --libdir="$OUT/libs/$abi" \
    --arch="$arch" --cpu="$cpu" \
    --cc="$TOOLCHAIN/${tgt}${API}-clang.cmd" \
    --nm="$TOOLCHAIN/llvm-nm.exe" \
    --ar="$TOOLCHAIN/llvm-ar.exe" \
    --ranlib="$TOOLCHAIN/llvm-ranlib.exe" \
    --strip="$TOOLCHAIN/llvm-strip.exe" \
    "$@" "${COMMON_OPTIONS[@]}"
  make -j"$JOBS"
  make install-libs
  make clean
}

build_ffmpeg arm64-v8a aarch64-linux-android aarch64 armv8-a
build_ffmpeg x86_64 x86_64-linux-android x86_64 x86-64 --disable-asm

# Windows quirk: FFmpeg's extension-less top-level "version" file collides with
# libc++'s <version> header on case-insensitive filesystems while compiling
# ffmpeg_jni.cc. Move it out of the way before the JNI compile.
mv ffmpeg-6.0.1/version ffmpeg-6.0.1/version.ffbak

echo "=== Compiling ffmpeg_jni.cc and linking libffmpegJNI.so ==="
cd "$WORK"
for abi in arm64-v8a x86_64; do
  tgt=aarch64-linux-android
  [ "$abi" = x86_64 ] && tgt=x86_64-linux-android
  mkdir -p "$OUT/$abi"
  "$TOOLCHAIN/${tgt}${API}-clang++.cmd" \
    -std=c++11 -O2 -fPIC -fvisibility=hidden \
    -I"$WORK/ffmpeg-6.0.1" \
    -c "$WORK/$JNI_SRC/ffmpeg_jni.cc" -o "$OUT/$abi/ffmpeg_jni.o"
  EXTRA_LDFLAGS=""
  [ "$abi" = arm64-v8a ] && EXTRA_LDFLAGS="-Wl,-Bsymbolic"   # per upstream CMakeLists.txt
  "$TOOLCHAIN/${tgt}${API}-clang++.cmd" \
    -shared -static-libstdc++ -Wl,-z,max-page-size=16384 $EXTRA_LDFLAGS \
    -o "$OUT/$abi/libffmpegJNI.so" \
    "$OUT/$abi/ffmpeg_jni.o" \
    "$OUT/libs/$abi/libswresample.a" \
    "$OUT/libs/$abi/libavcodec.a" \
    "$OUT/libs/$abi/libavutil.a" \
    -llog
  "$TOOLCHAIN/llvm-strip.exe" --strip-unneeded "$OUT/$abi/libffmpegJNI.so"
done

echo "=== Verifying LOAD segment alignment (expect 0x4000) ==="
for abi in arm64-v8a x86_64; do
  echo "--- $abi ---"
  "$TOOLCHAIN/llvm-readelf.exe" -l "$OUT/$abi/libffmpegJNI.so" | grep LOAD
done
echo "DONE — copy out/<abi>/libffmpegJNI.so into Noctra.Android/libs/ffmpeg/<abi>/"
