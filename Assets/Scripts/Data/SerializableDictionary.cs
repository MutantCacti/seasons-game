using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[System.Serializable]
public class SerializableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, ISerializationCallbackReceiver {


    [SerializeField] private List<TKey> keys = new List<TKey>();
    [SerializeField] private List<TValue> values = new List<TValue>();


    public SerializableDictionary() { }

    public SerializableDictionary(IDictionary<TKey, TValue> dictionary) : base(dictionary) {
    }


    public void OnBeforeSerialize() {

        keys.Clear();
        values.Clear();

        foreach (KeyValuePair<TKey, TValue> pair in this) {
            keys.Add(pair.Key);
            values.Add(pair.Value);
        }

    }


    public void OnAfterDeserialize() {

        Clear();

        if (keys.Count != values.Count) {
            Debug.LogError("ERROR!! Something went terribly wrong trying to deserialize a SerializableDictionary - amount of keys and values are not the same!!");
        }

        for (int i = 0; i < keys.Count; i++) {
            Add(keys[i], values[i]);
        }

    }
    
}
