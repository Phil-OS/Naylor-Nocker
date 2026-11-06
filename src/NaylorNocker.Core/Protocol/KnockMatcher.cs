using System.Security.Cryptography;
namespace NaylorNocker.Core.Protocol;
public sealed class KnockMatcher
{
    private readonly byte[] _expected;
    public KnockMatcher(ReadOnlySpan<byte> expected)
    {
        if (expected.Length is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(expected));
        _expected = expected.ToArray();
    }
    public bool Matches(ReadOnlySpan<byte> payload, bool truncated) =>
        !truncated && payload.Length == _expected.Length && CryptographicOperations.FixedTimeEquals(payload, _expected);
}
