using System.Buffers;

namespace Noctra.Core.Services;

internal static class BoundedResponseReader
{
    private const int CopyBufferSize = 64 * 1024;

    public static async Task<bool> CopyToAsync(
        Stream source,
        Stream destination,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        long copied = 0;
        try
        {
            while (true)
            {
                if (copied >= maxBytes)
                {
                    // Read one byte past the limit so chunked responses are rejected
                    // even when no Content-Length header was supplied.
                    var extra = await source.ReadAsync(
                            buffer.AsMemory(0, 1),
                            cancellationToken)
                        .ConfigureAwait(false);
                    return extra == 0;
                }

                var requested = (int)Math.Min(buffer.Length, maxBytes - copied);
                var read = await source.ReadAsync(
                        buffer.AsMemory(0, requested),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    return true;
                }

                copied += read;
                await destination.WriteAsync(
                        buffer.AsMemory(0, read),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
