#pragma warning disable CA1422
#pragma warning disable CA1416
using ABufferInfo = global::Android.Media.MediaCodec.BufferInfo;
using AExtractor = global::Android.Media.MediaExtractor;
using AMuxer = global::Android.Media.MediaMuxer;
using AMuxerOutputFormat = global::Android.Media.MuxerOutputType;
using Microsoft.Maui.Storage;

namespace Himo.Platforms.Android;

internal static class VideoEditProcessor
{
    public static async Task<long> GetDurationMsAsync(
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var extractor = new AExtractor();
                extractor.SetDataSource(inputPath);

                long durationUs = 0;
                for (var i = 0; i < extractor.TrackCount; i++)
                {
                    using var format = extractor.GetTrackFormat(i);
                    if (format.ContainsKey("durationUs"))
                    {
                        durationUs = Math.Max(durationUs, format.GetLong("durationUs"));
                    }
                }

                return Math.Max(0L, durationUs / 1000L);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task TrimAsync(
        string inputPath,
        string outputPath,
        long startMs,
        long endMs,
        bool muteAudio,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(
            () => TrimInternal(inputPath, outputPath, startMs, endMs, muteAudio, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private static void TrimInternal(
        string inputPath,
        string outputPath,
        long startMs,
        long endMs,
        bool muteAudio,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(inputPath))
            throw new FileNotFoundException("ملف الفيديو غير موجود.", inputPath);

        startMs = Math.Max(0, startMs);
        endMs = Math.Max(startMs + 250, endMs);
        var startUs = startMs * 1000L;
        var endUs = endMs * 1000L;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? FileSystem.CacheDirectory);
        var tempPath = outputPath + ".tmp.mp4";

        using var extractor = new AExtractor();
        extractor.SetDataSource(inputPath);

        var muxer = new AMuxer(tempPath, AMuxerOutputFormat.Mpeg4);
        try
        {
            var destinationTracks = new Dictionary<int, int>();
            for (var i = 0; i < extractor.TrackCount; i++)
            {
                using var format = extractor.GetTrackFormat(i);
                var mime = format.GetString("mime") ?? string.Empty;
                var isAudio = mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
                if (muteAudio && isAudio)
                    continue;

                if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                    || isAudio)
                {
                    destinationTracks[i] = muxer.AddTrack(format);
                }
            }

            if (destinationTracks.Count == 0)
                throw new InvalidOperationException("تعذر العثور على مسارات فيديو قابلة للتحرير.");

            muxer.Start();

            foreach (var pair in destinationTracks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CopyTrack(
                    extractor,
                    muxer,
                    pair.Key,
                    pair.Value,
                    startUs,
                    endUs,
                    cancellationToken);
            }

            muxer.Stop();
        }
        finally
        {
            muxer.Release();
            extractor.Release();
        }

        File.Move(tempPath, outputPath, overwrite: true);
    }

    private static void CopyTrack(
        AExtractor extractor,
        AMuxer muxer,
        int sourceTrack,
        int destinationTrack,
        long startUs,
        long endUs,
        CancellationToken cancellationToken)
    {
        extractor.SelectTrack(sourceTrack);
        extractor.SeekTo(startUs, global::Android.Media.MediaExtractorSeekTo.ClosestSync);

        const int bufferSize = 4 * 1024 * 1024;
        using var buffer = Java.Nio.ByteBuffer.Allocate(bufferSize);
        var info = new ABufferInfo();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            buffer.Clear();
            var sampleSize = extractor.ReadSampleData(buffer, 0);
            if (sampleSize < 0)
                break;

            var sampleTime = extractor.SampleTime;
            if (sampleTime < 0 || sampleTime > endUs)
                break;

            info.Set(0, sampleSize, Math.Max(0L, sampleTime - startUs), (global::Android.Media.MediaCodecBufferFlags)(int)extractor.SampleFlags);
            muxer.WriteSampleData(destinationTrack, buffer, info);

            if (!extractor.Advance())
                break;
        }

        extractor.UnselectTrack(sourceTrack);
    }
}
