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
class DungeonRenderer(Profile p)
{
    const int W = 960, H = 360;           // slice height is a multiple of 40 so the grid lines up
    const int TileW = 16, TileH = 22;
    const string Green = "#00FF00", Dim = "#00a83a", Wall = "#0f4d1a";

    // Tile glyph + colour per activity level (0 = wall)
    static readonly (string Glyph, string Color)[] Tiles =
    [
        ("#", Wall),        // 0 contributions: solid rock
        (".", Dim),         // level 1: floor
        ("+", Green),       // level 2: corridor / door
        ("$", "#ffd84d"),   // level 3: gold
        ("!", "#ff4d9d"),   // level 4: potion (your busiest days)
    ];

    public string Render()
    {
        var days = p.AllDays.ToList();
        int max = Math.Max(1, days.Count == 0 ? 1 : days.Max(d => d.Count));
        var (current, best, streakDays) = Streaks(days);

        int cols = p.Weeks.Count + 2, rows = 7 + 2;  // +2 = outer wall border
        double mapX = (W - cols * TileW) / 2.0, mapY = 56;
        var sb = new StringBuilder();

        sb.Append($$"""
        <svg xmlns="http://www.w3.org/2000/svg" width="{{W}}" height="{{H}}" viewBox="0 0 {{W}} {{H}}">
        <defs>
          <filter id="glow" x="-20%" y="-50%" width="140%" height="200%"><feGaussianBlur stdDeviation="2" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
          <pattern id="grid" width="40" height="40" patternUnits="userSpaceOnUse"><path d="M40 0H0V40" fill="none" stroke="{{Green}}" stroke-opacity=".07"/></pattern>
          <pattern id="scan" width="4" height="4" patternUnits="userSpaceOnUse"><rect width="4" height="2" fill="#000" fill-opacity=".35"/></pattern>
        </defs>
        <style>
          text{font-family:"Courier New",Consolas,"DejaVu Sans Mono",monospace;font-weight:700}
          .t{font-size:18px;text-anchor:middle}
          .ui{font-size:15px;white-space:pre}
          .blink{animation:b 1s step-end infinite} @keyframes b{50%{opacity:.25} }
          .flick{animation:f 7s infinite} @keyframes f{0%,95%,100%{opacity:1}96%{opacity:.6}97%{opacity:1} }
        </style>
        <rect width="{{W}}" height="{{H}}" fill="#000"/>
        <rect x="12" width="{{W - 24}}" height="{{H}}" fill="url(#grid)"/>
        <g class="flick" filter="url(#glow)">
          <line x1="14" y1="0" x2="14" y2="{{H}}" stroke="{{Green}}" stroke-width="3"/>
          <line x1="{{W - 14}}" y1="0" x2="{{W - 14}}" y2="{{H}}" stroke="{{Green}}" stroke-width="3"/>
        </g>
        <text class="ui" x="40" y="34" fill="{{Green}}" filter="url(#glow)">&gt; ./explore --dungeon contributions --depth 365</text>

        """);

        // Map tiles
        for (int c = 0; c < cols; c++)
        for (int r = 0; r < rows; r++)
        {
            double x = mapX + c * TileW + TileW / 2.0, y = mapY + r * TileH + 16;
            bool border = c == 0 || r == 0 || c == cols - 1 || r == rows - 1;
            if (border) { sb.Append(Glyph(x, y, "#", Wall)); continue; }
            var day = p.Weeks[c - 1][r - 1];
            if (day is null) continue;                       // days that haven't happened yet
            var (g, col) = Tiles[Level(day.Count, max)];
            sb.Append(Glyph(x, y, g, col, Level(day.Count, max) >= 3));
        }

        // The player '@' walks your current streak (or stands on your latest active day)
        var path = streakDays.Count > 0 ? streakDays
                 : days.Where(d => d.Count > 0).TakeLast(1).ToList();
        if (path.Count > 0)
        {
            var pts = path.Select(d => TilePos(d, mapX, mapY)).ToList();
            var last = pts[^1];
            sb.Append($"""<g filter="url(#glow)" transform="translate({last.X:0.#} {last.Y:0.#})">""");
            sb.Append($"""<rect x="{-TileW / 2}" y="-16" width="{TileW}" height="{TileH}" fill="#000"/>""");
            sb.Append("""<text class="t blink" x="0" y="0" fill="#ffffff">@</text>""");
            if (pts.Count > 1)
            {
                var values = string.Join(";", pts.Select(pt => $"{pt.X:0.#} {pt.Y:0.#}"));
                sb.Append($"""<animateTransform attributeName="transform" type="translate" values="{values}" dur="{pts.Count * 0.5:0.#}s" calcMode="discrete" repeatCount="indefinite"/>""");
            }
            sb.Append("</g>\n");
        }

        // Legend + HUD
        double legendY = mapY + rows * TileH + 30;
        sb.Append($"""
        <text class="ui" x="40" y="{legendY}" fill="{Dim}"><tspan fill="{Wall}">#</tspan> rock  <tspan fill="{Dim}">.</tspan> 1st commit  <tspan fill="{Green}">+</tspan> active  <tspan fill="#ffd84d">$</tspan> loot  <tspan fill="#ff4d9d">!</tspan> legendary day  <tspan fill="#fff">@</tspan> you</text>
        <text class="ui" x="40" y="{legendY + 36}" fill="{Green}" filter="url(#glow)">XP {p.Total}  ·  STREAK {current}d (best {best}d)  ·  GOLD ★{p.Stars}  ·  QUESTS {p.Repos}  ·  PARTY {p.Followers}</text>
        <rect width="{W}" height="{H}" fill="url(#scan)"/>
        </svg>
        """);
        return sb.ToString();
    }

    static int Level(int count, int max) => count == 0 ? 0
        : count <= max * 0.25 ? 1 : count <= max * 0.5 ? 2 : count <= max * 0.75 ? 3 : 4;

    (double X, double Y) TilePos(Day d, double mapX, double mapY)
    {
        for (int c = 0; c < p.Weeks.Count; c++)
        for (int r = 0; r < 7; r++)
            if (p.Weeks[c][r]?.Date == d.Date)
                return (mapX + (c + 1) * TileW + TileW / 2.0, mapY + (r + 1) * TileH + 16);
        return (0, 0);
    }

    static (int Current, int Best, List<Day> CurrentDays) Streaks(List<Day> days)
    {
        int best = 0, run = 0;
        foreach (var d in days) { run = d.Count > 0 ? run + 1 : 0; best = Math.Max(best, run); }
        // Current streak: count back from today; today with 0 commits doesn't break it yet.
        var cur = new List<Day>();
        int i = days.Count - 1;
        if (i >= 0 && days[i].Count == 0) i--;
        for (; i >= 0 && days[i].Count > 0; i--) cur.Insert(0, days[i]);
        return (cur.Count, best, cur);
    }

    static string Glyph(double x, double y, string g, string color, bool glow = false) =>
        $"""<text class="t" x="{x:0.#}" y="{y:0.#}" fill="{color}"{(glow ? " filter=\"url(#glow)\"" : "")}>{g}</text>""";
}
