// G00dS0ul Dungeon Generator
// Turns your GitHub contribution calendar into a roguelike dungeon map (SVG).
//   dotnet run -- <username> [outputPath]
// Needs env var PROFILE_TOKEN (or GITHUB_TOKEN) to call the GitHub GraphQL API.
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var user = args.Length > 0 ? args[0] : "G00dS0ul";
var output = args.Length > 1 ? args[1] : "assets/dungeon.svg";
var token = Environment.GetEnvironmentVariable("PROFILE_TOKEN")
         ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
         ?? throw new InvalidOperationException("Set PROFILE_TOKEN or GITHUB_TOKEN.");

var profile = await GitHub.FetchAsync(user, token);
var svg = new DungeonRenderer(profile).Render();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
await File.WriteAllTextAsync(output, svg);
Console.WriteLine($"Dungeon written to {output} ({profile.Weeks.Count} weeks, {profile.Total} XP).");

// ---------------- Data ----------------
record Day(DateOnly Date, int Count);
record Profile(List<List<Day?>> Weeks, int Total, int Stars, int Repos, int Followers)
{
    public IEnumerable<Day> AllDays => Weeks.SelectMany(w => w).OfType<Day>().OrderBy(d => d.Date);
}

static class GitHub
{
    const string Query = """
    query($login: String!) {
      user(login: $login) {
        followers { totalCount }
        repositories(ownerAffiliations: OWNER, isFork: false, privacy: PUBLIC, first: 100) {
          totalCount
          nodes { stargazerCount }
        }
        contributionsCollection {
          contributionCalendar {
            totalContributions
            weeks { contributionDays { date contributionCount weekday } }
          }
        }
      }
    }
    """;

    public static async Task<Profile> FetchAsync(string login, string token)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("G00dS0ul-DungeonGen");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", token);
        var body = JsonSerializer.Serialize(new { query = Query, variables = new { login } });
        var res = await http.PostAsync("https://api.github.com/graphql",
            new StringContent(body, Encoding.UTF8, "application/json"));
        res.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;
        if (json["errors"] is JsonArray errs)
            throw new Exception("GraphQL error: " + string.Join("; ", errs.Select(e => e?["message"])));

        var u = json["data"]!["user"]!;
        var cal = u["contributionsCollection"]!["contributionCalendar"]!;
        var weeks = new List<List<Day?>>();
        foreach (var w in cal["weeks"]!.AsArray())
        {
            var col = new Day?[7];
            foreach (var d in w!["contributionDays"]!.AsArray())
                col[(int)d!["weekday"]!] = new Day(
                    DateOnly.Parse((string)d["date"]!, CultureInfo.InvariantCulture),
                    (int)d["contributionCount"]!);
            weeks.Add(col.ToList());
        }
        var stars = u["repositories"]!["nodes"]!.AsArray().Sum(n => (int)n!["stargazerCount"]!);
        return new Profile(weeks, (int)cal["totalContributions"]!, stars,
            (int)u["repositories"]!["totalCount"]!, (int)u["followers"]!["totalCount"]!);
    }
}

// ---------------- Rendering ----------------
// Each day is one tile, laid out like GitHub's contribution graph:
//   no commits  -> dark stone wall
//   commits     -> lit floor tile (brighter = more commits), busiest days hold treasure
//   '@' sprite  -> you, walking your current streak
class DungeonRenderer(Profile p)
{
    const int W = 960, H = 320;           // multiple of 40 so the console grid lines up
    const int Cell = 16, Tile = 14;
    const int GridX = 76, GridY = 76;
    const string Green = "#00FF00", Dim = "#00a83a", Label = "#2f8f45";
    static readonly string[] FloorColors = ["#0e5a22", "#16912f", "#22cc46", "#7dff8f"];
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string Render()
    {
        var days = p.AllDays.ToList();
        int max = Math.Max(1, days.Count == 0 ? 1 : days.Max(d => d.Count));
        var (current, best, streakDays) = Streaks(days);
        var sb = new StringBuilder();

        sb.Append($$"""
        <svg xmlns="http://www.w3.org/2000/svg" width="{{W}}" height="{{H}}" viewBox="0 0 {{W}} {{H}}">
        <defs>
          <filter id="glow" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="2" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
          <pattern id="grid" width="40" height="40" patternUnits="userSpaceOnUse"><path d="M40 0H0V40" fill="none" stroke="{{Green}}" stroke-opacity=".07"/></pattern>
          <pattern id="scan" width="4" height="4" patternUnits="userSpaceOnUse"><rect width="4" height="2" fill="#000" fill-opacity=".3"/></pattern>
          <g id="wall"><rect width="{{Tile}}" height="{{Tile}}" fill="#0a1f0f"/><rect width="{{Tile}}" height="2" fill="#163a1e"/><rect y="7" width="{{Tile}}" height="1" fill="#050f07"/><rect x="6" y="2" width="1" height="5" fill="#050f07"/><rect x="3" y="8" width="1" height="6" fill="#050f07"/><rect x="10" y="8" width="1" height="6" fill="#050f07"/></g>
          <g id="chest"><rect x="3" y="5" width="8" height="6" fill="#c98a1b"/><rect x="3" y="5" width="8" height="2" fill="#ffd84d"/><rect x="6" y="7" width="2" height="2" fill="#3a2500"/></g>
          <g id="hero"><rect x="5" y="1" width="4" height="4" fill="#fff"/><rect x="3" y="6" width="8" height="2" fill="#fff"/><rect x="6" y="5" width="2" height="5" fill="#fff"/><rect x="4" y="10" width="2" height="3" fill="#fff"/><rect x="8" y="10" width="2" height="3" fill="#fff"/><rect x="11" y="3" width="1" height="6" fill="#9ff"/></g>
        </defs>
        <style>
          text{font-family:"Courier New",Consolas,"DejaVu Sans Mono",monospace;font-weight:700;white-space:pre}
          .ui{font-size:15px} .lbl{font-size:12px} .big{font-size:22px} .small{font-size:12px}
          .torch{animation:t 1.3s infinite} @keyframes t{0%,100%{opacity:1}40%{opacity:.55}70%{opacity:.85} }
          .bob{animation:bob .8s steps(2) infinite} @keyframes bob{50%{transform:translateY(-1px)} }
          .flick{animation:f 7s infinite} @keyframes f{0%,95%,100%{opacity:1}96%{opacity:.6}97%{opacity:1} }
        </style>
        <rect width="{{W}}" height="{{H}}" fill="#000"/>
        <rect x="12" width="{{W - 24}}" height="{{H}}" fill="url(#grid)"/>
        <g class="flick" filter="url(#glow)">
          <line x1="14" y1="0" x2="14" y2="{{H}}" stroke="{{Green}}" stroke-width="3"/>
          <line x1="{{W - 14}}" y1="0" x2="{{W - 14}}" y2="{{H}}" stroke="{{Green}}" stroke-width="3"/>
        </g>
        <text class="ui" x="40" y="30" fill="{{Green}}" filter="url(#glow)">&gt; ./explore --dungeon "my last 365 days"</text>
        <text class="small" x="40" y="50" fill="{{Label}}">Every tile is one day. Dark stone = no commits. Lit floor = I was coding. Treasure = my biggest days.</text>

        """);

        // Month labels + weekday labels
        string? lastMonth = null;
        for (int c = 0; c < p.Weeks.Count; c++)
        {
            var first = p.Weeks[c].OfType<Day>().FirstOrDefault();
            if (first is null) continue;
            var m = first.Date.ToString("MMM", Inv);
            if (m != lastMonth && first.Date.Day <= 7 && c < p.Weeks.Count - 2)
                sb.Append($"""<text class="lbl" x="{GridX + c * Cell}" y="{GridY - 6}" fill="{Label}">{m}</text>""");
            lastMonth = m;
        }
        foreach (var (row, name) in new[] { (1, "Mon"), (3, "Wed"), (5, "Fri") })
            sb.Append($"""<text class="lbl" x="40" y="{GridY + row * Cell + 11}" fill="{Label}">{name}</text>""");
        sb.Append('\n');

        // Tiles
        var rng = new Random(1337); // seeded: same data -> same image -> no pointless commits
        var torches = new List<(int X, int Y)>();
        for (int c = 0; c < p.Weeks.Count; c++)
        for (int r = 0; r < 7; r++)
        {
            var day = p.Weeks[c][r];
            if (day is null) continue;
            int x = GridX + c * Cell, y = GridY + r * Cell;
            int lvl = Level(day.Count, max);
            if (lvl == 0)
            {
                sb.Append($"""<use href="#wall" x="{x}" y="{y}"/>""");
                if (NextToFloor(c, r, max) && rng.NextDouble() < 0.06) torches.Add((x, y));
                continue;
            }
            var glow = lvl >= 3 ? " filter=\"url(#glow)\"" : "";
            sb.Append($"""<rect x="{x}" y="{y}" width="{Tile}" height="{Tile}" fill="{FloorColors[lvl - 1]}"{glow}/>""");
            if (lvl == 4) sb.Append($"""<use href="#chest" x="{x}" y="{y}"/>""");
        }
        foreach (var (x, y) in torches)
            sb.Append($"""<g class="torch" filter="url(#glow)"><rect x="{x + 6}" y="{y + 6}" width="2" height="6" fill="#7a4a12"/><rect x="{x + 5}" y="{y + 2}" width="4" height="4" fill="#ffae2b"/></g>""");
        sb.Append('\n');

        // The hero walks the current streak (or stands on the latest active day)
        var path = streakDays.Count > 0 ? streakDays : days.Where(d => d.Count > 0).TakeLast(1).ToList();
        if (path.Count > 0)
        {
            var pts = path.Select(TilePos).ToList();
            var last = pts[^1];
            sb.Append($"""<g filter="url(#glow)" transform="translate({last.X} {last.Y})"><rect width="{Tile}" height="{Tile}" fill="#000" fill-opacity=".55"/><g class="bob"><use href="#hero"/></g>""");
            if (pts.Count > 1)
            {
                var values = string.Join(";", pts.Select(pt => $"{pt.X} {pt.Y}"));
                sb.Append($"""<animateTransform attributeName="transform" type="translate" values="{values}" dur="{(pts.Count * 0.6).ToString("0.#", Inv)}s" calcMode="discrete" repeatCount="indefinite"/>""");
            }
            sb.Append("</g>\n");
        }

        // Legend (Less -> More, like GitHub)
        int ly = GridY + 7 * Cell + 22;
        sb.Append($"""<text class="lbl" x="{GridX}" y="{ly + 11}" fill="{Label}">no commits</text><use href="#wall" x="{GridX + 84}" y="{ly}"/>""");
        sb.Append($"""<text class="lbl" x="{GridX + 116}" y="{ly + 11}" fill="{Label}">few</text>""");
        for (int i = 0; i < 4; i++)
            sb.Append($"""<rect x="{GridX + 146 + i * Cell}" y="{ly}" width="{Tile}" height="{Tile}" fill="{FloorColors[i]}"/>""");
        sb.Append($"""<use href="#chest" x="{GridX + 146 + 3 * Cell}" y="{ly}"/><text class="lbl" x="{GridX + 220}" y="{ly + 11}" fill="{Label}">many (treasure)</text>""");
        sb.Append($"""<use href="#hero" x="{GridX + 370}" y="{ly}"/><text class="lbl" x="{GridX + 390}" y="{ly + 11}" fill="{Label}">me, walking my current streak</text>""");
        sb.Append('\n');

        // Stat boxes
        int by = ly + 34, bw = 280, gap = 20, bx = (W - (3 * bw + 2 * gap)) / 2;
        string[] titles = ["XP · contributions", "STREAK · days in a row", "LOOT"];
        string[] values2 = [$"{p.Total}", $"{current} (best {best})", $"★{p.Stars}  ·  {p.Repos} repos"];
        for (int i = 0; i < 3; i++)
        {
            int x = bx + i * (bw + gap);
            sb.Append($"""<rect x="{x}" y="{by}" width="{bw}" height="62" rx="4" fill="#001a06" stroke="{Dim}" stroke-opacity=".7"/>""");
            sb.Append($"""<text class="lbl" x="{x + 14}" y="{by + 20}" fill="{Label}">{titles[i]}</text>""");
            sb.Append($"""<text class="big" x="{x + 14}" y="{by + 48}" fill="{Green}" filter="url(#glow)">{values2[i]}</text>""");
        }

        sb.Append($"""

        <rect width="{W}" height="{H}" fill="url(#scan)"/>
        </svg>
        """);
        return sb.ToString();
    }

    static int Level(int count, int max) => count == 0 ? 0
        : count <= max * 0.25 ? 1 : count <= max * 0.5 ? 2 : count <= max * 0.75 ? 3 : 4;

    bool NextToFloor(int c, int r, int max)
    {
        foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int nc = c + dc, nr = r + dr;
            if (nc < 0 || nr < 0 || nc >= p.Weeks.Count || nr >= 7) continue;
            if (p.Weeks[nc][nr] is { Count: > 0 }) return true;
        }
        return false;
    }

    (int X, int Y) TilePos(Day d)
    {
        for (int c = 0; c < p.Weeks.Count; c++)
        for (int r = 0; r < 7; r++)
            if (p.Weeks[c][r]?.Date == d.Date) return (GridX + c * Cell, GridY + r * Cell);
        return (0, 0);
    }

    static (int Current, int Best, List<Day> CurrentDays) Streaks(List<Day> days)
    {
        int best = 0, run = 0;
        foreach (var d in days) { run = d.Count > 0 ? run + 1 : 0; best = Math.Max(best, run); }
        var cur = new List<Day>();
        int i = days.Count - 1;
        if (i >= 0 && days[i].Count == 0) i--;          // today not done yet doesn't break the streak
        for (; i >= 0 && days[i].Count > 0; i--) cur.Insert(0, days[i]);
        return (cur.Count, best, cur);
    }
}
