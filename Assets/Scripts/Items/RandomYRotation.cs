using UnityEngine;


public class RandomYRotation : MonoBehaviour
{
    public void Start()
    {
        float randomYRotation = Random.Range(-180f, 180f);
        transform.Rotate(new Vector3(0, randomYRotation, 0), Space.World);
    }
}