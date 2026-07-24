namespace MaisonCalliard.Application.Files;

public sealed class ImageValidationException : Exception
{
    public ImageValidationException(string message) : base(message)
    {
    }
}
