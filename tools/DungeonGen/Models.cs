record Day(DateOnly Date, int Count);

record Language(string Name, string Color, long Bytes);

record Repo(string Name, string NameWithOwner, string Url, string? Description,
            string? Language, string? LanguageColor, int Stars, int Forks, DateTime PushedAt);

record Profile(
    List<List<Day?>> Weeks, int Total,
    int Stars, int Repos, int Followers,
    int PullRequests, int Issues, int ContributedTo, DateTime CreatedAt,
    List<Language> Languages, bool LanguagesByCommits, List<Repo> Pinned)
{
    public IEnumerable<Day> AllDays => Weeks.SelectMany(w => w).OfType<Day>().OrderBy(d => d.Date);
}

// Hand-written profile text, edited in about.json (no code changes needed).
record AboutConfig(
    string Name, string Handle, string Class, string Origin, string Specialty, string Bio,
    List<Effect> Effects, List<string> Gear, List<string> Skills, List<LinkButton> Links);

record Effect(string Tag, string Label, string Text);

record LinkButton(string Key, string Label, string Url);
