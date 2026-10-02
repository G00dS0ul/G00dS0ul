// G00dS0ul Profile Generator
// Turns your GitHub activity into a cyberpunk/roguelike console for your profile README.
//
//   dotnet run -- <username> [--assets assets] [--html docs/index.html] [--readme README.md]
//
// Writes:
//   assets/dungeon.svg      contribution dungeon (README image)
//   assets/inventory.svg    languages + character stats
//   assets/quest-1..6.svg   one card per pinned repo
//   docs/index.html         interactive dungeon (GitHub Pages, hover any tile)
//   README.md               refreshes the quest links between <!-- QUESTS:START/END -->
//
// Uses PROFILE_TOKEN (personal token) or GITHUB_TOKEN for the GitHub GraphQL API.

var user = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "G00dS0ul";
string Opt(string name, string fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}
var assets = Opt("--assets", "assets");
var htmlPath = Opt("--html", "docs/index.html");
var readmePath = Opt("--readme", "README.md");

var token = Environment.GetEnvironmentVariable("PROFILE_TOKEN")
         ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
         ?? throw new InvalidOperationException("Set PROFILE_TOKEN or GITHUB_TOKEN.");

var profile = await GitHub.FetchAsync(user, token);

var dungeon = new DungeonRenderer(profile);
await Write(Path.Combine(assets, "dungeon.svg"), dungeon.Render(interactive: false));
await Write(htmlPath, HtmlPage.Build(user, dungeon.Render(interactive: true)));
await Write(Path.Combine(assets, "inventory.svg"), InventoryRenderer.Render(profile));

var quests = QuestRenderer.Slots(profile);
for (int i = 0; i < quests.Count; i++)
    await Write(Path.Combine(assets, $"quest-{i + 1}.svg"), QuestRenderer.Render(quests[i], i, user));

if (File.Exists(readmePath))
    await ReadmeUpdater.UpdateQuestLinksAsync(readmePath, assets, quests);

Console.WriteLine($"Done: {profile.Total} XP, {profile.Languages.Count} languages, {profile.Pinned.Count} quests.");

static async Task Write(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    await File.WriteAllTextAsync(path, content);
}
