#!/usr/bin/env bash
set -euo pipefail

FFMPEG_VERSION="9.0"
FFMPEG_ARCHIVE="ffmpeg-${FFMPEG_VERSION}.tar.xz"
FFMPEG_SIGNATURE="${FFMPEG_ARCHIVE}.asc"
FFMPEG_BASE_URL="https://ffmpeg.org/releases"
FFMPEG_KEY_URL="https://ffmpeg.org/ffmpeg-devel.asc"
EXPECTED_RELEASE_KEY="FCF986EA15E6E293A5644F10B4322F04D67658D8"

ROOT_DIR="${PWD}"
WORK_DIR="${ROOT_DIR}/.ffmpeg-build"
OUTPUT_DIR="${ROOT_DIR}/artifacts/ffmpeg-runtime"
INSTALL_DIR="${WORK_DIR}/install"
SOURCE_DIR="${WORK_DIR}/ffmpeg-${FFMPEG_VERSION}"

rm -rf "${WORK_DIR}" "${OUTPUT_DIR}"
mkdir -p "${WORK_DIR}" "${OUTPUT_DIR}/runtime/ffmpeg" "${OUTPUT_DIR}/licenses/ffmpeg" "${OUTPUT_DIR}/source"

cd "${WORK_DIR}"

curl --fail --location --silent --show-error --output "${FFMPEG_ARCHIVE}" "${FFMPEG_BASE_URL}/${FFMPEG_ARCHIVE}"
curl --fail --location --silent --show-error --output "${FFMPEG_SIGNATURE}" "${FFMPEG_BASE_URL}/${FFMPEG_SIGNATURE}"
curl --fail --location --silent --show-error --output ffmpeg-devel.asc "${FFMPEG_KEY_URL}"

gpg --batch --import ffmpeg-devel.asc
if ! gpg --batch --with-colons --fingerprint | grep -Fq "${EXPECTED_RELEASE_KEY}"; then
    echo "Expected FFmpeg release signing key was not imported." >&2
    exit 1
fi

gpg --batch --verify "${FFMPEG_SIGNATURE}" "${FFMPEG_ARCHIVE}"

tar -xf "${FFMPEG_ARCHIVE}"
cd "${SOURCE_DIR}"

CONFIGURE_FLAGS=(
    "--prefix=${INSTALL_DIR}"
    "--target-os=mingw32"
    "--arch=x86_64"
    "--cross-prefix=x86_64-w64-mingw32-"
    "--enable-cross-compile"
    "--disable-autodetect"
    "--enable-zlib"
    "--disable-network"
    "--disable-debug"
    "--disable-doc"
    "--disable-ffplay"
    "--disable-gpl"
    "--disable-nonfree"
    "--enable-static"
    "--disable-shared"
    "--extra-ldflags=-static"
)

./configure "${CONFIGURE_FLAGS[@]}"
make -j"$(nproc)"
make install

cp "${INSTALL_DIR}/bin/ffmpeg.exe" "${OUTPUT_DIR}/runtime/ffmpeg/ffmpeg.exe"
cp "${INSTALL_DIR}/bin/ffprobe.exe" "${OUTPUT_DIR}/runtime/ffmpeg/ffprobe.exe"
cp LICENSE.md COPYING.LGPLv2.1 COPYING.LGPLv3 "${OUTPUT_DIR}/licenses/ffmpeg/"

ZLIB_COPYRIGHT="/usr/share/doc/libz-mingw-w64-dev/copyright"
if [[ ! -f "${ZLIB_COPYRIGHT}" ]]; then
    echo "zlib copyright/license notice was not found." >&2
    exit 1
fi
cp "${ZLIB_COPYRIGHT}" "${OUTPUT_DIR}/licenses/ffmpeg/ZLIB-COPYRIGHT.txt"

cp "${WORK_DIR}/${FFMPEG_ARCHIVE}" "${OUTPUT_DIR}/source/"
cp "${WORK_DIR}/${FFMPEG_SIGNATURE}" "${OUTPUT_DIR}/source/"

{
    echo "FFmpeg runtime for Dynamic Range Analyzer"
    echo "Source version: ${FFMPEG_VERSION}"
    echo "Source URL: ${FFMPEG_BASE_URL}/${FFMPEG_ARCHIVE}"
    echo "Signature URL: ${FFMPEG_BASE_URL}/${FFMPEG_SIGNATURE}"
    echo "Release signing key fingerprint: ${EXPECTED_RELEASE_KEY}"
    echo "zlib cross-build package: $(dpkg-query -W -f='${Version}' libz-mingw-w64-dev)"
    echo
    echo "Configure command:"
    printf './configure'
    printf ' %q' "${CONFIGURE_FLAGS[@]}"
    printf '\n'
    echo
    echo "Executable DLL dependencies:"
    for executable in ffmpeg.exe ffprobe.exe; do
        echo "[${executable}]"
        x86_64-w64-mingw32-objdump -p "${OUTPUT_DIR}/runtime/ffmpeg/${executable}" \
            | grep -F "DLL Name:" || true
        echo
    done
} > "${OUTPUT_DIR}/FFMPEG-BUILD.txt"

for executable in ffmpeg.exe ffprobe.exe; do
    if x86_64-w64-mingw32-objdump -p "${OUTPUT_DIR}/runtime/ffmpeg/${executable}" \
        | grep -Eiq 'DLL Name:.*(libgcc|libwinpthread|libstdc\+\+)'; then
        echo "Unexpected MinGW runtime DLL dependency in ${executable}." >&2
        exit 1
    fi
done

sha256sum "${OUTPUT_DIR}/runtime/ffmpeg/ffmpeg.exe" \
          "${OUTPUT_DIR}/runtime/ffmpeg/ffprobe.exe" \
          "${OUTPUT_DIR}/source/${FFMPEG_ARCHIVE}" \
    > "${OUTPUT_DIR}/SHA256SUMS.txt"
