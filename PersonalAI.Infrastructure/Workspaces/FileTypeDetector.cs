using System.Text;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Workspaces;

public sealed class FileTypeDetector(WorkspaceToolOptions options)
{
    private const int SampleBytes = 8192;

    public TextDetection Detect(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new WorkspaceAccessException(
                "io_error",
                "Workspace file could not be inspected.");
        }

        if (stream.Length > options.MaxReadableFileBytes)
        {
            throw new WorkspaceAccessException(
                "file_too_large",
                "Workspace file was too large to read.");
        }

        var originalPosition = stream.Position;
        var sampleLength = (int)Math.Min(SampleBytes, stream.Length);
        var sample = new byte[sampleLength];
        var offset = 0;
        while (offset < sample.Length)
        {
            var read = stream.Read(sample, offset, sample.Length - offset);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        stream.Position = originalPosition;
        return DetectSample(sample.AsSpan(0, offset), stream.Length);
    }

    public TextDetection DetectSample(ReadOnlySpan<byte> sample, long totalLength)
    {
        if (totalLength > options.MaxReadableFileBytes)
        {
            throw new WorkspaceAccessException(
                "file_too_large",
                "Workspace file was too large to read.");
        }

        var encoding = DetectEncoding(sample);
        if (encoding is UTF8Encoding && sample.Contains((byte)0))
        {
            throw new WorkspaceAccessException(
                "binary_file",
                "Workspace file appeared to be binary.");
        }

        if (LooksBinary(sample, encoding))
        {
            throw new WorkspaceAccessException(
                "binary_file",
                "Workspace file appeared to be binary.");
        }

        return new TextDetection(encoding);
    }

    private static Encoding DetectEncoding(ReadOnlySpan<byte> sample)
    {
        if (sample.Length >= 3 &&
            sample[0] == 0xEF &&
            sample[1] == 0xBB &&
            sample[2] == 0xBF)
        {
            return new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: true,
                throwOnInvalidBytes: true);
        }

        if (sample.Length >= 2 &&
            sample[0] == 0xFF &&
            sample[1] == 0xFE)
        {
            return new UnicodeEncoding(
                bigEndian: false,
                byteOrderMark: true,
                throwOnInvalidBytes: true);
        }

        if (sample.Length >= 2 &&
            sample[0] == 0xFE &&
            sample[1] == 0xFF)
        {
            return new UnicodeEncoding(
                bigEndian: true,
                byteOrderMark: true,
                throwOnInvalidBytes: true);
        }

        return new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
    }

    private static bool LooksBinary(ReadOnlySpan<byte> sample, Encoding encoding)
    {
        if (sample.Length == 0)
        {
            return false;
        }

        try
        {
            var text = encoding.GetString(sample);
            var control = text.Count(character =>
                char.IsControl(character) &&
                character is not '\r' and not '\n' and not '\t' and not '\f');
            return control > Math.Max(8, text.Length / 20);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}

public sealed record TextDetection(Encoding Encoding);
