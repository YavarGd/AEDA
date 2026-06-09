#if WINDOWS
using System.Drawing;
using System.Drawing.Imaging;

namespace PersonalAI.Infrastructure.Context;

public sealed class TemporaryScreenshotStore
{
    private readonly string _directory;

    public TemporaryScreenshotStore()
        : this(Path.Combine(Path.GetTempPath(), "PersonalAI", "Screenshots"))
    {
    }

    public TemporaryScreenshotStore(string directory)
    {
        _directory = directory;
    }

    public string Save(Bitmap bitmap)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.png");
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }
}
#endif
