// A thin JNI bridge to whisper.cpp for the Spectraplayer Android app.
//
// Kotlin side: com.neuspectra.spectraplayer.transcribe.WhisperNative.
// Progress and cancellation are polled from Kotlin (atomics here) rather than called back into Java: whisper.cpp may invoke
// its callbacks from worker threads, which have no JNIEnv.

#include <jni.h>
#include <android/log.h>

#include <atomic>
#include <string>

#include "whisper.h"

#define LOG_TAG "SpectraWhisper"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO, LOG_TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, LOG_TAG, __VA_ARGS__)

namespace {

struct Engine {
    whisper_context * ctx = nullptr;
    std::atomic<int>  progress{0};
    std::atomic<bool> abort{false};
};

bool abort_callback(void * user_data) {
    return static_cast<Engine *>(user_data)->abort.load();
}

void progress_callback(whisper_context *, whisper_state *, int progress, void * user_data) {
    static_cast<Engine *>(user_data)->progress.store(progress);
}

} // namespace

extern "C" {

JNIEXPORT jlong JNICALL
Java_com_neuspectra_spectraplayer_transcribe_WhisperNative_nInit(JNIEnv * env, jobject, jstring path) {
    const char * model_path = env->GetStringUTFChars(path, nullptr);
    whisper_context_params cparams = whisper_context_default_params();
    cparams.use_gpu = false;
    whisper_context * ctx = whisper_init_from_file_with_params(model_path, cparams);
    env->ReleaseStringUTFChars(path, model_path);
    if (ctx == nullptr) {
        LOGE("could not load the model");
        return 0;
    }
    auto * engine = new Engine();
    engine->ctx = ctx;
    return reinterpret_cast<jlong>(engine);
}

JNIEXPORT void JNICALL
Java_com_neuspectra_spectraplayer_transcribe_WhisperNative_nFree(JNIEnv *, jobject, jlong handle) {
    auto * engine = reinterpret_cast<Engine *>(handle);
    if (engine == nullptr) return;
    whisper_free(engine->ctx);
    delete engine;
}

JNIEXPORT void JNICALL
Java_com_neuspectra_spectraplayer_transcribe_WhisperNative_nCancel(JNIEnv *, jobject, jlong handle) {
    auto * engine = reinterpret_cast<Engine *>(handle);
    if (engine != nullptr) engine->abort.store(true);
}

JNIEXPORT jint JNICALL
Java_com_neuspectra_spectraplayer_transcribe_WhisperNative_nProgress(JNIEnv *, jobject, jlong handle) {
    auto * engine = reinterpret_cast<Engine *>(handle);
    return engine == nullptr ? 0 : engine->progress.load();
}

/**
 * Transcribes 16 kHz mono float samples. Returns UTF-8 bytes of one line per segment, "t0_ms|t1_ms|text\n", or null on
 * failure (including cancellation). Bytes rather than a Java string: a segment boundary can split a multi-byte character,
 * and NewStringUTF must not be given invalid UTF-8.
 */
JNIEXPORT jbyteArray JNICALL
Java_com_neuspectra_spectraplayer_transcribe_WhisperNative_nTranscribe(
        JNIEnv * env, jobject, jlong handle, jfloatArray samples, jint threads, jstring language) {
    auto * engine = reinterpret_cast<Engine *>(handle);
    if (engine == nullptr) return nullptr;

    engine->abort.store(false);
    engine->progress.store(0);

    const jsize count = env->GetArrayLength(samples);
    jfloat * data = env->GetFloatArrayElements(samples, nullptr);
    const char * lang = env->GetStringUTFChars(language, nullptr);

    whisper_full_params params = whisper_full_default_params(WHISPER_SAMPLING_GREEDY);
    params.n_threads = threads > 0 ? threads : 2;
    params.language = lang;
    params.translate = false;
    params.no_context = true;          // every chunk stands alone: the caller pads and stitches
    params.print_realtime = false;
    params.print_progress = false;
    params.print_timestamps = false;
    params.print_special = false;
    params.progress_callback = progress_callback;
    params.progress_callback_user_data = engine;
    params.abort_callback = abort_callback;
    params.abort_callback_user_data = engine;

    const int rc = whisper_full(engine->ctx, params, data, count);

    env->ReleaseStringUTFChars(language, lang);
    env->ReleaseFloatArrayElements(samples, data, JNI_ABORT);

    if (rc != 0 || engine->abort.load()) {
        LOGE("whisper_full returned %d (aborted=%d)", rc, engine->abort.load() ? 1 : 0);
        return nullptr;
    }

    std::string out;
    const int n = whisper_full_n_segments(engine->ctx);
    for (int i = 0; i < n; ++i) {
        const int64_t t0 = whisper_full_get_segment_t0(engine->ctx, i) * 10; // whisper counts in 10 ms
        const int64_t t1 = whisper_full_get_segment_t1(engine->ctx, i) * 10;
        std::string text = whisper_full_get_segment_text(engine->ctx, i);
        for (char & c : text) if (c == '\n' || c == '\r') c = ' ';
        out += std::to_string(t0) + "|" + std::to_string(t1) + "|" + text + "\n";
    }

    jbyteArray result = env->NewByteArray(static_cast<jsize>(out.size()));
    env->SetByteArrayRegion(result, 0, static_cast<jsize>(out.size()), reinterpret_cast<const jbyte *>(out.data()));
    return result;
}

JNIEXPORT jstring JNICALL
Java_com_neuspectra_spectraplayer_transcribe_WhisperNative_nSystemInfo(JNIEnv * env, jobject) {
    return env->NewStringUTF(whisper_print_system_info());
}

} // extern "C"
