package org.webrtc.audio;

import android.content.Context;
import android.media.AudioFormat;
import android.media.AudioManager;
import android.media.AudioRecord;
import android.media.AudioTrack;

/**
 * Device-safe audio parameter bridge for WebRTC.
 *
 * The stock WebRTC implementation prefers Android's
 * PROPERTY_OUTPUT_FRAMES_PER_BUFFER whenever FEATURE_AUDIO_LOW_LATENCY is
 * present. Some OEM builds advertise that feature but expose an invalid
 * frames-per-buffer value. WebRTC then passes that value into AudioParameters
 * and aborts on output_parameters->is_valid().
 *
 * We deliberately use AudioTrack/AudioRecord min-buffer sizing instead of the
 * unreliable OEM low-latency property. The rest of the WebRTC audio stack is
 * unchanged.
 */
public final class WebRtcAudioManager {
    private static final int DEFAULT_SAMPLE_RATE_HZ = 48000;
    private static final int DEFAULT_FRAME_PER_BUFFER = 256;
    private static final int BITS_PER_SAMPLE = 16;
    private static final int MAX_REASONABLE_FRAME_COUNT = 16384;

    private WebRtcAudioManager() {}

    static AudioManager getAudioManager(Context context) {
        return (AudioManager) context.getSystemService(Context.AUDIO_SERVICE);
    }

    static int getOutputBufferSize(
            Context context,
            AudioManager audioManager,
            int sampleRate,
            int numberOfOutputChannels) {
        // Intentionally bypass FEATURE_AUDIO_LOW_LATENCY. The OEM property
        // behind that feature is unreliable on affected devices.
        return getSafeMinOutputFrameSize(sampleRate, numberOfOutputChannels);
    }

    static int getInputBufferSize(
            Context context,
            AudioManager audioManager,
            int sampleRate,
            int numberOfInputChannels) {
        return getSafeMinInputFrameSize(sampleRate, numberOfInputChannels);
    }

    static boolean isLowLatencyOutputSupported(Context context) {
        return false;
    }

    static boolean isLowLatencyInputSupported(Context context) {
        return false;
    }

    static int getSampleRate(AudioManager audioManager) {
        if (audioManager == null) {
            return DEFAULT_SAMPLE_RATE_HZ;
        }

        try {
            String value = audioManager.getProperty(AudioManager.PROPERTY_OUTPUT_SAMPLE_RATE);
            int parsed = Integer.parseInt(value);
            if (parsed >= 8000 && parsed <= 192000) {
                return parsed;
            }
        } catch (Throwable ignored) {
        }

        return DEFAULT_SAMPLE_RATE_HZ;
    }

    private static int getSafeMinOutputFrameSize(int sampleRateInHz, int numChannels) {
        int channels = (numChannels == 2) ? 2 : 1;
        int bytesPerFrame = channels * (BITS_PER_SAMPLE / 8);
        int channelConfig =
                (channels == 1)
                        ? AudioFormat.CHANNEL_OUT_MONO
                        : AudioFormat.CHANNEL_OUT_STEREO;

        try {
            int bytes = AudioTrack.getMinBufferSize(
                    sampleRateInHz,
                    channelConfig,
                    AudioFormat.ENCODING_PCM_16BIT);

            if (bytes > 0) {
                int frames = bytes / bytesPerFrame;
                if (frames > 0 && frames <= MAX_REASONABLE_FRAME_COUNT) {
                    return frames;
                }
            }
        } catch (Throwable ignored) {
        }

        return DEFAULT_FRAME_PER_BUFFER;
    }

    private static int getSafeMinInputFrameSize(int sampleRateInHz, int numChannels) {
        int channels = (numChannels == 2) ? 2 : 1;
        int bytesPerFrame = channels * (BITS_PER_SAMPLE / 8);
        int channelConfig =
                (channels == 1)
                        ? AudioFormat.CHANNEL_IN_MONO
                        : AudioFormat.CHANNEL_IN_STEREO;

        try {
            int bytes = AudioRecord.getMinBufferSize(
                    sampleRateInHz,
                    channelConfig,
                    AudioFormat.ENCODING_PCM_16BIT);

            if (bytes > 0) {
                int frames = bytes / bytesPerFrame;
                if (frames > 0 && frames <= MAX_REASONABLE_FRAME_COUNT) {
                    return frames;
                }
            }
        } catch (Throwable ignored) {
        }

        return DEFAULT_FRAME_PER_BUFFER;
    }
}
