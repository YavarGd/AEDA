namespace PersonalAI.Core.Chat;

public sealed record ChatImage
{
    public ChatImage(string mediaType, string base64Data)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            throw new ArgumentException(
                "An image media type is required.",
                nameof(mediaType));
        }

        if (string.IsNullOrWhiteSpace(base64Data))
        {
            throw new ArgumentException(
                "Image data is required.",
                nameof(base64Data));
        }

        MediaType = mediaType;
        Base64Data = base64Data;
    }

    public string MediaType { get; }

    public string Base64Data { get; }
}
