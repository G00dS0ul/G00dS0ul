using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

// Rewrites only the block between <!-- QUESTS:START --> and <!-- QUESTS:END --> in README.md,
// so each quest card links to the right repo even after you change your pinned repos.
static class ReadmeUpdater
{
    const string Start = "<!-- QUESTS:START -->", End = "<!-- QUESTS:END -->";

    public static async Task UpdateQuestLinksAsync(string readmePath, string assetsDir, List<Repo?> quests)
    {
        var readme = await File.ReadAllTextAsync(readmePath);
        if (!readme.Contains(Start) || !readme.Contains(End))
        {
            Console.WriteLine("README has no QUESTS markers; skipping link update.");
            return;
        }

        var rel = "./" + assetsDir.Replace('\\', '/').Trim('/');
        var sb = new StringBuilder(Start + "\n");
        for (int i = 0; i < quests.Count; i += 2)
        {
            // Two cards per line, no whitespace between them, so they sit side by side at 50% each.
            sb.Append(Card(quests[i], rel, i));
            if (i + 1 < quests.Count) sb.Append(Card(quests[i + 1], rel, i + 1));
            sb.Append('\n');
        }
        sb.Append(End);

        var updated = Regex.Replace(readme, Regex.Escape(Start) + ".*?" + Regex.Escape(End), sb.ToString(), RegexOptions.Singleline);
        updated = BustCache(updated, rel, assetsDir);
        if (updated != readme) await File.WriteAllTextAsync(readmePath, updated);
    }

    // GitHub caches README images, so a changed SVG can keep showing the old version for a while.
    // Adding ?v=<content hash> gives each new version a new URL. Unchanged files keep the same
    // hash, so this never creates pointless commits.
    static string BustCache(string readme, string rel, string assetsDir) =>
        Regex.Replace(readme, Regex.Escape(rel) + @"/([\w.\-]+\.svg)(\?v=[0-9a-f]+)?", m =>
        {
            var file = Path.Combine(assetsDir, m.Groups[1].Value);
            if (!File.Exists(file)) return m.Value;
            var hash = Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(file)))[..8].ToLowerInvariant();
            return $"{rel}/{m.Groups[1].Value}?v={hash}";
        });

    static string Card(Repo? repo, string rel, int i)
    {
        var img = $"<img src=\"{rel}/quest-{i + 1}.svg\" width=\"50%\" align=\"top\" alt=\"{(repo?.Name ?? "Locked quest")}\">";
        return repo is null ? img : $"<a href=\"{repo.Url}\">{img}</a>";
    }
}
