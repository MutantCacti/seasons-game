// The time of day and day counter, held in the world so a checkpoint reset rolls them back with
// everything else. elapsedInCycle is seconds since the start of the current day (sunrise).
[System.Serializable]
public class TimeSaveData
{
    public int day;              // 1-based; Day 1 is the first sunrise
    public float elapsedInCycle; // seconds since sunrise, [0, dayDuration + nightDuration)


    // default constructor
    public TimeSaveData()
    {
        day = 1;
        elapsedInCycle = 0f;
    }
}
