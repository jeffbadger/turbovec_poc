using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LocalRag.Wpf.Rag;

public sealed class LocalHashEmbeddingService
{
    private readonly int _dimensions;

    public LocalHashEmbeddingService(int dimensions)
    {
        if (dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Embedding dimensions must be positive.");
        }

        _dimensions = dimensions;
    }

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var vector = new float[_dimensions];
        foreach (Match match in Regex.Matches(text.ToLowerInvariant(), "[a-z0-9]+"))
        {
            var token = match.Value;
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var bucket = BitConverter.ToUInt32(bytes, 0) % _dimensions;
            var sign = (bytes[4] & 1) == 0 ? 1.0f : -1.0f;
            vector[bucket] += sign;
        }

        var norm = MathF.Sqrt(vector.Sum(value => value * value));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return Task.FromResult(vector);
    }
}
