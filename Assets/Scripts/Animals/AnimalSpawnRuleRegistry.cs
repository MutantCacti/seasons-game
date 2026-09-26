using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(
    fileName = "AnimalSpawnRuleRegistry",
    menuName = "Animals/Animal Spawn Rule Registry"
)]
public class AnimalSpawnRuleRegistry : ScriptableObject, Registry<AnimalSpawnRule>
{
    [SerializeField] private List<AnimalSpawnRule> rules = new List<AnimalSpawnRule>();


    [System.NonSerialized] private Dictionary<string, AnimalSpawnRule> ruleById;


    public IReadOnlyList<AnimalSpawnRule> Entries { get { return rules; } }
    public int Count { get { return rules != null ? rules.Count : 0; } }


    // MARK: lookup


    public bool TryGetById(string id, out AnimalSpawnRule rule)
    {
        rule = null;
        if (string.IsNullOrEmpty(id)) return false;

        EnsureLookup();

        return ruleById.TryGetValue(id, out rule) && rule != null;
    }


    public bool TryGetIdByEntry(AnimalSpawnRule rule, out string id)
    {
        id = null;
        if (rule == null) return false;

        EnsureLookup();

        if (!string.IsNullOrEmpty(rule.Id) &&
            ruleById.TryGetValue(rule.Id, out AnimalSpawnRule registered) &&
            registered == rule)
        {
            id = rule.Id;
            return true;
        }

        Debug.LogError($"AnimalSpawnRuleRegistry: '{rule.name}' is not registered or its id '{rule.Id}' is empty or duplicated, so it cannot be written to a save", this);

        return false;
    }


    // MARK: construction


    // the lookup goes stale whenever the editor changes the rules list
    private void OnValidate()
    {
        ruleById = null;
    }


    public void Rebuild()
    {
        ruleById = new Dictionary<string, AnimalSpawnRule>();
        if (rules == null) return;

        for (int i = 0; i < rules.Count; i++)
        {
            AnimalSpawnRule rule = rules[i];

            if (rule == null)
            {
                Debug.LogError($"AnimalSpawnRuleRegistry: entry {i} is empty", this);
                continue;
            }

            // Id falls back to the asset name, so it is only ever empty for a rule with no name
            if (string.IsNullOrEmpty(rule.Id))
            {
                Debug.LogError($"AnimalSpawnRuleRegistry: '{rule.name}' has no Id, so it can never be saved or loaded", rule);
                continue;
            }

            // first entry wins on duplicate
            if (ruleById.TryGetValue(rule.Id, out AnimalSpawnRule existing))
            {
                Debug.LogError($"AnimalSpawnRuleRegistry: duplicate Id '{rule.Id}' on '{rule.name}', keeping '{existing.name}' and ignoring this one", rule);
                continue;
            }

            ruleById.Add(rule.Id, rule);
        }
    }


    private void EnsureLookup()
    {
        if (ruleById != null) return;

        Rebuild();
    }
}
