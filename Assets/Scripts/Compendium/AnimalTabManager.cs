using System.Collections.Generic;
using UnityEngine;


// The Animals tab: registry animals, three per page, with undiscovered animals as silhouettes and
// newly discovered ones pulsing with an outline until the player hovers over them. Built the same way
// as GameItemTabManager: an animal is discovered the first time one of its kind is in the world, and
// stays discovered.
public class AnimalTabManager : CompendiumTabManager
{
    private const int ENTRIES_PER_PAGE = 3;

    private static readonly Color SILHOUETTE_COLOR = new Color(0.15f, 0.15f, 0.15f, 1.0f);


    [SerializeField] private AnimalRegistry registry;
    [SerializeField] private GameObject pagePrefab;
    [SerializeField] private Shader silhouetteShader;
    [SerializeField] private Color discoveredColour = new Color(237.0f / 255.0f, 196.0f / 255.0f, 59.0f / 255.0f, 1.0f);  // #EDC43B

    private Material silhouetteMaterial;

    // Registry entries the book is built from, minus the hidden ones (see Animal.HideInCompendium).
    // Paging, discovery and the unseen glow all index this, never the registry itself, so a hidden
    // animal takes no page, no slot and no glow
    private readonly List<Animal> visibleAnimals = new List<Animal>();

    // Discovered and unseen animals are saved through the base class, keyed by Animal.Id (unseen with
    // kind 0). Both are global, so a checkpoint reset never takes a discovery back
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

        RebuildVisibleAnimals();

        // A new world discovers an animal present from the start like any other, so it is unseen. A
        // save from before discovery was remembered is seeded quietly from what is present now
        if (!baselineEmpty && !DiscoverySeeded) SeedDiscovery();

        // anything present but never discovered is a discovery
        RecomputePresence();

        PruneSavedUnseen();
    }


    private void OnDestroy()
    {
        if (animalManager != null) animalManager.AnimalsChanged -= OnAnimalsChanged;
    }


    // MARK: registry


    // Once, in Start: the registry is authored data, so a hidden animal becoming visible mid-session
    // (an editor tweak during play) is not worth watching for
    private void RebuildVisibleAnimals()
    {
        visibleAnimals.Clear();

        IReadOnlyList<Animal> animals = registry != null ? registry.Entries : null;
        if (animals == null) return;

        for (int i = 0; i < animals.Count; i++)
        {
            Animal animal = animals[i];
            if (animal == null) continue;
            if (animal.HideInCompendium) continue;

            visibleAnimals.Add(animal);
        }
    }


    public int Count { get { return visibleAnimals.Count; } }


    // False for a null registry, an out of range index or a hole in the list
    public bool TryGetAnimal(int index, out Animal animal)
    {
        animal = null;

        if (index < 0 || index >= visibleAnimals.Count) return false;

        animal = visibleAnimals[index];

        return animal != null;
    }


    // MARK: unseen


    // everything here is a discovery, so the first unseen animal in range settles it
    public override UnseenPriority HighestUnseenInPages(int firstPage, int lastPage)
    {
        int first = Mathf.Max(firstPage, 0) * ENTRIES_PER_PAGE;
        int end = Mathf.Min((lastPage + 1) * ENTRIES_PER_PAGE, Count);

        for (int i = first; i < end; i++)
        {
            if (!TryGetAnimal(i, out Animal animal)) continue;
            if (TryGetUnseen(animal.Id, out int _)) return UnseenPriority.Discovery;
        }

        return UnseenPriority.None;
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

        if (IsDiscovered(animal.Id))
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


    // only ever used to discover, never to un-discover
    private bool IsPresent(Animal animal)
    {
        return animalManager != null && animalManager.HasAnimalOf(animal);
    }


    // MARK: presence


    private void OnAnimalsChanged()
    {
        RecomputePresence();
    }


    // Any animal present that has never been discovered becomes discovered and unseen. Absence
    // changes nothing: once seen, it stays in the book. Hidden animals are never walked, so they are
    // never discovered and never unseen either
    private void RecomputePresence()
    {
        bool changed = false;

        for (int i = 0; i < visibleAnimals.Count; i++)
        {
            Animal animal = visibleAnimals[i];
            if (animal == null) continue;

            if (IsDiscovered(animal.Id)) continue;
            if (!IsPresent(animal)) continue;

            MarkDiscovered(animal.Id, false);
            MarkUnseen(animal.Id, 0, false);
            changed = true;
        }

        MarkDiscoverySeeded();

        // one save for the whole diff rather than one per animal
        if (!changed) return;

        SaveCompendium();
        RaiseChanged();
    }


    // An old save's first load: what is present now counts as already seen, so it is discovered but
    // not unseen (whatever it saved as unseen stays unseen)
    private void SeedDiscovery()
    {
        for (int i = 0; i < visibleAnimals.Count; i++)
        {
            Animal animal = visibleAnimals[i];
            if (animal == null) continue;

            if (IsPresent(animal)) MarkDiscovered(animal.Id, false);
        }

        MarkDiscoverySeeded();

        SaveCompendium();

        // pages can bind before Start, while nothing was discovered yet
        RaiseChanged();
    }


    // Drops unseen ids no animal has any more, any that were never discovered (unseen is always a
    // subset of discovered, so that is only ever a hand-edited or broken save), and any belonging to
    // an animal that is now hidden: an unseen flag on a page the book no longer shows would only make
    // the tab glow for nothing
    private void PruneSavedUnseen()
    {
        if (registry == null) return;

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

            if (animal.HideInCompendium)
            {
                ClearUnseen(id, false);
                pruned = true;
                continue;
            }

            if (IsDiscovered(id)) continue;

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
