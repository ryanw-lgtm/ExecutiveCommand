#!/usr/bin/env bash
# Cross-compiles llama-server.exe for Windows x64 (single static executable, CPU with AVX2).
# Usage: tools/build_windows_llama.sh <llama.cpp source dir> <output dir>
set -euo pipefail
SRC=${1:?llama.cpp source}; OUT=${2:?output dir}
HERE=$(cd "$(dirname "$0")" && pwd)
cmake -S "$SRC" -B "$SRC/build-win" -DCMAKE_SYSTEM_NAME=Windows -DCMAKE_SYSTEM_PROCESSOR=x86_64 \
  -DCMAKE_C_COMPILER=x86_64-w64-mingw32-gcc-posix -DCMAKE_CXX_COMPILER=x86_64-w64-mingw32-g++-posix \
  -DCMAKE_C_FLAGS="-include $HERE/mingw/win_compat.h" -DCMAKE_CXX_FLAGS="-include $HERE/mingw/win_compat.h" \
  -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF -DLLAMA_CURL=OFF -DLLAMA_OPENSSL=OFF -DLLAMA_BUILD_WEBUI=OFF \
  -DGGML_NATIVE=OFF -DGGML_AVX=ON -DGGML_AVX2=ON -DGGML_FMA=ON -DGGML_F16C=ON -DGGML_OPENMP=OFF \
  -DLLAMA_BUILD_TESTS=OFF -DLLAMA_BUILD_EXAMPLES=OFF \
  -DCMAKE_EXE_LINKER_FLAGS="-static -static-libgcc -static-libstdc++"
cmake --build "$SRC/build-win" --target llama-server -j"$(nproc)"
mkdir -p "$OUT" && cp "$SRC/build-win/bin/llama-server.exe" "$OUT/"
cp "$SRC/LICENSE" "$OUT/LICENSE-llama.cpp.txt"
