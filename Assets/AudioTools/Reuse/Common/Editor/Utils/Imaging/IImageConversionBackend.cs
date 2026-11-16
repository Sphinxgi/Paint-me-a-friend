namespace ImpossibleRobert.Common
{
    /// <summary>Optional image format conversion supplied by Common Imaging.</summary>
    public interface IImageConversionBackend
    {
        /// <summary>Decodes an encoded image in any supported format and returns it as PNG. Implementations must be thread-safe.</summary>
        bool TryConvertToPng(byte[] source, out byte[] png);
    }
}
