// Deterministic hash primitives.
public static class TerrainHash
{
    // custom FNV-1a implementation for determinism
    // https://mojoauth.com/hashing/fnv-1a-in-c
    public const uint FNV_OFFSET_BASIS = 2166136261u;
    public const uint FNV_PRIME = 16777619u;


    // Add one int to the hash, little endian
    public static uint Fold(uint hash, int value)
    {
        unchecked
        {
            hash = (hash ^ (byte)(value & 0xFF)) * FNV_PRIME;
            hash = (hash ^ (byte)((value >> 8) & 0xFF)) * FNV_PRIME;
            hash = (hash ^ (byte)((value >> 16) & 0xFF)) * FNV_PRIME;
            hash = (hash ^ (byte)((value >> 24) & 0xFF)) * FNV_PRIME;
            return hash;
        }
    }


    // Add a string to the hash, length first so a pair of shorter strings cannot land on the same
    // state as one longer one where a sequence of them is folded in
    public static uint Fold(uint hash, string value)
    {
        if (string.IsNullOrEmpty(value)) return Fold(hash, 0);

        unchecked
        {
            hash = Fold(hash, value.Length);

            for (int i = 0; i < value.Length; i++) hash = (hash ^ (byte)value[i]) * FNV_PRIME;

            return hash;
        }
    }


    // https://blog.teamleadnet.com/2012/08/murmurhash3-ultra-fast-hash-algorithm.html
    // avalanching a hash means scrambling it so that if even one bit is different
    // from another hash pre-avalanche, post-avalanche average half the bits will be different.
    // this is apparently necessary to avoid structure in the low bits of the FNV-1a algorithm
    public static uint Avalanche(uint hash)
    {
        unchecked
        {
            hash ^= hash >> 16;
            hash *= 0x7feb352du;
            hash ^= hash >> 15;
            hash *= 0x846ca68bu;
            hash ^= hash >> 16;
            return hash;
        }
    }
}
