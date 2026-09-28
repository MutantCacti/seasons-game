// Per-instance traits from an entity's guid, so an animal looks and acts the same every load.
//
// FNV-1a over the guid's characters: string.GetHashCode isn't promised to be the same across runtimes.
// Each trait then mixes the seed with its own salt through a lowbias32 finaliser, so adding a trait
// with a new salt never moves the others.
//
// Read the guid in Start, not Awake: it is minted on first read, and a loaded animal only has its
// saved one assigned after Awake.
public static class EntitySeed
{
    public static uint FromGuid(string guid)
    {
        uint seed = 2166136261u;
        if (guid == null) return seed;

        for (int i = 0; i < guid.Length; i++)
        {
            seed ^= guid[i];
            seed *= 16777619u;
        }

        return seed;
    }


    // The same hash over a cell instead of a guid. A placement ghost has no guid and no EntityId to
    // read, so the only thing the preview and the thing it spawns share is where they stand: x and z
    // in whole world units, which is the terrain cell for anything on a tile centre. y is left out on
    // purpose, FloatOnSea lifts a lily pad's height without moving it to another cell
    public static uint FromCell(int x, int z)
    {
        uint seed = 2166136261u;

        seed ^= (uint)x;
        seed *= 16777619u;

        seed ^= (uint)z;
        seed *= 16777619u;

        return seed;
    }


    // one trait, 0..1
    public static float Trait01(uint seed, uint salt)
    {
        uint x = seed ^ salt;
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;

        return (x >> 8) / 16777216.0f;
    }
}
