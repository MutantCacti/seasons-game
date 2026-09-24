using System.Collections.Generic;
using UnityEngine;


// The Animals tab: registry animals, three per page, with undiscovered animals as silhouettes and
// newly discovered ones pulsing with an outline until the player hovers over them. Built the same way
// as GameItemTabManager: an animal is discovered while at least one of its kind is in the world.
public class AnimalTabManager : CompendiumTabManager
{
    private const int ENTRIES_PER_PAGE = 3;

    private static readonly Color SILHOUETTE_COLOR = new Color(0.15f, 0.15f, 0.15f, 1.0f);


    [SerializeField] private AnimalRegistry registry;
    [SerializeField] private GameObject pagePrefab;
    [SerializeField] private Shader silhouetteShader;
    [SerializeField] private Color discoveredColour = new Color(237.0f / 255.0f, 196.0f / 255.0f, 59.0f / 255.0f, 1.0f);  // #EDC43B

    private Material silhouetteMaterial;

    // Presence is runtime-only and rebuilt from the world on every load, so it rolls back with a
    // checkpoint. Unseen animals are saved, through the base class, keyed by Animal.Id with kind 0.
    private readonly HashSet<Animal> present = new HashSet<Animal>();
    private readonly List<UnseenSave> savedUnseen = new List<UnseenSave>();
    private bool baselineEmpty;

    private AnimalManager animalManager;


    public override GameObject PagePrefab { get { return pagePrefab; } }


    public override int PageCount
    {
        get { return (Count + ENTRIES_PER_PAGE - 1) / ENTRIES_PER_PAGE; }
    }


    private void Awake()
    {
        if (registry == null) Debug.LogError("AnimalTabManager: no AnimalRegistry, so the tab has nothing to show", this);
        if (pagePrefab == null) Debug.LogError("AnimalTabManager: no page prefab, so the tab cannot render pages", this);
        if (silhouetteShader == null) Debug.LogError("AnimalTabManager: no silhouette shader, so undiscovered animals render normally instead of as silhouettes", this);

        // Captured now because EntityManager clears IsConstructingNewWorld later this same frame,
        // once it has finished seeding the world fixtures (this compendium included).
        baselineEmpty = SaveDataManager.Instance != null && SaveDataManager.Instance.IsConstructingNewWorld;
    }


    // AnimalManager loads its animals in Awake, so they are already live by any Start
    private void Start()
    {
        animalManager = AnimalManager.Instance;

        if (animalManager != null) animalManager.AnimalsChanged += OnAnimalsChanged;
        else Debug.LogError("AnimalTabManager: no AnimalManager, so no animal can ever be discovered", this);

        // A new world starts from an empty baseline, so an animal present from the start is unseen.
        // A loaded world keeps what it saved as unseen, minus anything no longer present.
        if (baselineEmpty) RecomputePresence();
        else TakeBaselineSnapshot();
    }


    private void OnDestroy()
    {
        if (animalManager != null) animalManager.AnimalsChanged -= OnAnimalsChanged;
    }


    // MARK: registry


    public int Count { get { return registry != null ? registry.Count : 0; } }


    // False for a null registry, an out of range index or a hole in the list
    public bool TryGetAnimal(int index, out Animal animal)
    {
        animal = null;

        if (registry == null) return false;
        if (index < 0 || index >= registry.Count) return false;

        animal = registry.Entries[index];

        return animal != null;
    }


    // MARK: binding


    // the page is the items tab's page: GameItemPageContainer is only a list of CompendiumEntry
    public override void BindPage(GameObject page, int pageIndex)
    {
        GameItemPageContainer container = page != null ? page.GetComponent<GameItemPageContainer>() : null;
        if (container == null) return;

        CompendiumEntry[] entries = container.Entries;
        if (entries == null) return;

        int count = Count;
        int first = pageIndex * ENTRIES_PER_PAGE;
        bool hasContent = pageIndex >= 0 && first < count;

        for (int i = 0; i < entries.Length; i++)
        {
            CompendiumEntry entry = entries[i];
            if (entry == null) continue;

            Animal animal = null;
            if (hasContent) TryGetAnimal(first + i, out animal);

            BindEntry(entry, animal);
            entry.gameObject.SetActive(animal != null);
        }
    }


    private void BindEntry(CompendiumEntry entry, Animal animal)
    {
        if (animal == null)
        {
            entry.Clear();
            HideHighlight(entry);
            return;
        }

        if (IsDiscovered(animal))
        {
            // CompendiumEntry.SetItem only takes a GameItem, so the same four calls by hand
            entry.SetImage(animal.Icon);
            entry.SetIconMaterial(null);
            entry.SetNameText(animal.Name);
            entry.SetDescriptionText(animal.Description);

            if (TryGetUnseen(animal.Id, out int _)) ShowHighlight(entry, animal);
            else HideHighlight(entry);

            return;
        }

        entry.SetImage(animal.Icon);
        entry.SetIconMaterial(SilhouetteMaterial);
        entry.SetNameText("Undiscovered");
        entry.SetDescriptionText(string.Empty);
        HideHighlight(entry);
    }


    private void ShowHighlight(CompendiumEntry entry, Animal animal)
    {
        UnseenHighlight highlight = entry.GetComponent<UnseenHighlight>();
        if (highlight == null) return;

        highlight.Show(discoveredColour, () => ClearUnseen(animal.Id));
    }


    private void HideHighlight(CompendiumEntry entry)
    {
        UnseenHighlight highlight = entry.GetComponent<UnseenHighlight>();
        if (highlight != null) highlight.Hide();
    }


    private bool IsDiscovered(Animal animal)
    {
        return animalManager != null && animalManager.HasAnimalOf(animal);
    }


    // MARK: presence


    private void OnAnimalsChanged()
    {
        RecomputePresence();
    }


    // Diffs the live world against the last known snapshot, marking every absent -> present
    // transition as unseen. Absent animals drop out of unseen so a later re-discovery pulses again.
    private void RecomputePresence()
    {
        IReadOnlyList<Animal> animals = registry != null ? registry.Entries : null;
        if (animals == null) return;

        bool changed = false;

        for (int i = 0; i < animals.Count; i++)
        {
            Animal animal = animals[i];
            if (animal == null) continue;

            if (IsDiscovered(animal))
            {
                if (present.Add(animal))
                {
                    MarkUnseen(animal.Id, 0, false);
                    changed = true;
                }
            }
            else
            {
                if (present.Remove(animal)) changed = true;

                if (TryGetUnseen(animal.Id, out int _))
                {
                    ClearUnseen(animal.Id, false);
                    changed = true;
                }
            }
        }

        // one save for the whole diff rather than one per animal
        if (!changed) return;

        SaveCompendium();
        RaiseChanged();
    }


    // The first snapshot of a loaded world. What was saved as unseen stays unseen, except animals
    // that are no longer present, which drop out exactly as they would have at runtime.
    private void TakeBaselineSnapshot()
    {
        present.Clear();

        IReadOnlyList<Animal> animals = registry != null ? registry.Entries : null;
        if (animals == null) return;

        for (int i = 0; i < animals.Count; i++)
        {
            Animal animal = animals[i];
            if (animal == null) continue;

            if (IsDiscovered(animal)) present.Add(animal);
        }

        PruneSavedUnseen();
    }


    private void PruneSavedUnseen()
    {
        CopyUnseen(savedUnseen);

        bool pruned = false;

        for (int i = 0; i < savedUnseen.Count; i++)
        {
            string id = savedUnseen[i].id;

            if (!registry.TryGetById(id, out Animal animal))
            {
                Debug.LogWarning($"AnimalTabManager: the save holds unseen animal '{id}', but no animal in " +
                    $"'{registry.name}' has that id - dropping it", this);

                ClearUnseen(id, false);
                pruned = true;
                continue;
            }

            if (present.Contains(animal)) continue;

            ClearUnseen(id, false);
            pruned = true;
        }

        savedUnseen.Clear();

        if (pruned) SaveCompendium();
    }


    private Material SilhouetteMaterial
    {
        get
        {
            if (silhouetteMaterial == null && silhouetteShader != null)
            {
                silhouetteMaterial = new Material(silhouetteShader);
                silhouetteMaterial.SetColor("_SilhouetteColor", SILHOUETTE_COLOR);
            }

            return silhouetteMaterial;
        }
    }
}
