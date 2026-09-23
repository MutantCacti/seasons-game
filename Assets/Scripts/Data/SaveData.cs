using System;
using System.Collections.Generic;
using UnityEngine;


[System.Serializable]
public class SaveData
{
    public const int CURRENT_VERSION = 4;


    // field order is the order the JSON is written in, so it is kept this way on purpose
    public int saveVersion;
    public long lastSaveTime;       // milliseconds since unix epoch
    public long playTimeElapsed;    // milliseconds

    // Each one is a return point: the puzzle that was completed to reach it, and the world as it was.
    // Currently always empty; the checkpoints branch fills it.
    public List<Checkpoint> checkpoints;

    public WorldState world;


    // default constructor
    public SaveData()
    {
        saveVersion = CURRENT_VERSION;
        lastSaveTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        playTimeElapsed = 0L;
        checkpoints = new List<Checkpoint>();
        world = new WorldState();
    }


    public void PrepareForWrite()
    {
        if (world != null) world.Sort();

        if (checkpoints != null)
        {
            for (int i = 0; i < checkpoints.Count; i++)
            {
                if (checkpoints[i] != null && checkpoints[i].world != null) checkpoints[i].world.Sort();
            }
        }

        saveVersion = CURRENT_VERSION;
    }
}


// A world the player can be returned to, named after the puzzle whose completion created it
[System.Serializable]
public class Checkpoint
{
    public string puzzleId;
    public WorldState world;
}
