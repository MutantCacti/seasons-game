using System;
using UnityEngine;


[System.Serializable]
public class SaveData
{
    public long lastSaveTime;       // milliseconds since unix epoch
    public long playTimeElapsed;    // milliseconds


    // default constructor
    public SaveData()
    {
        lastSaveTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        playTimeElapsed = 0L;
    }
}
