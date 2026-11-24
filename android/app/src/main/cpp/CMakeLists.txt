cmake_minimum_required(VERSION 3.22.1)
project(spectra_whisper LANGUAGES C CXX)

# whisper.cpp is downloaded, not committed: run android\tools\Get-WhisperCpp.ps1 first.
set(WHISPER_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../../../third_party/whisper.cpp")
if (NOT EXISTS "${WHISPER_DIR}/CMakeLists.txt")
    message(FATAL_ERROR "whisper.cpp is missing. Run android\\tools\\Get-WhisperCpp.ps1 (see the README).")
endif()

# a plain CPU build: static libraries, no examples, no OpenMP (it needs an extra runtime on Android)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(WHISPER_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(WHISPER_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(WHISPER_BUILD_SERVER OFF CACHE BOOL "" FORCE)
set(GGML_OPENMP OFF CACHE BOOL "" FORCE)
set(GGML_NATIVE OFF CACHE BOOL "" FORCE)
set(GGML_LLAMAFILE OFF CACHE BOOL "" FORCE)

add_subdirectory(${WHISPER_DIR} whisper_build)

add_library(spectra_whisper SHARED whisper_jni.cpp)
target_include_directories(spectra_whisper PRIVATE ${WHISPER_DIR}/include ${WHISPER_DIR}/ggml/include)
target_link_libraries(spectra_whisper PRIVATE whisper log android)
