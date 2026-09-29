using System.Net;
using System.Text;
using System.Text.Json;
using Castorice.Core.Bancho;
using Castorice.Core.Configuration;
using Castorice.Core.MappoolBuilder;
using Castorice.Core.Tournament;

namespace Castorice.Core.Tests;

public class MappoolBuilderTests : IDisposable
{
    private static readonly MappoolBuilderConnection Public = new("https://pools.jansel.dev");

    private static readonly MappoolBuilderConnection WithToken = new("https://pools.jansel.dev", "tpz_secret-token-value");

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "castorice-builder-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static string PoolJson => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mappool-builder-pool.json"));

    private static RemotePool Pool() =>
        JsonSerializer.Deserialize<RemotePool>(PoolJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    // ---- client --------------------------------------------------------------

    [Fact]
    public async Task Reads_a_pool_in_the_documented_shape()
    {
        var handler = new RecordingHandler(PoolJson);
        using var client = new MappoolBuilderClient(new HttpClient(handler));

        var pool = await client.GetPoolAsync(Public, "Xb3kQ9aZ");

        Assert.Equal("https://pools.jansel.dev/api/pools/Xb3kQ9aZ", handler.LastUri);
        Assert.Equal("Showmatch Finals", pool.Name);
        Assert.Equal("peppy", pool.Owner?.Username);
        Assert.Equal(9, pool.Slots.Count);
        Assert.Equal(129891, pool.Slots[3].BeatmapId);
        Assert.Equal(8.52, pool.Slots[3].Sr);
        Assert.Equal("Warmup", pool.Slots[1].Note);
        Assert.Null(pool.Slots[0].Note);
    }

    [Fact]
    public async Task Sends_the_token_only_when_there_is_one()
    {
        var handler = new RecordingHandler("""{ "user": null, "configured": true, "missing": [] }""");
        using var client = new MappoolBuilderClient(new HttpClient(handler));

        Assert.Null(await client.GetMeAsync(Public));
        Assert.Null(handler.LastAuthorization);
        Assert.Equal("en", handler.LastAcceptLanguage);

        await client.GetMeAsync(WithToken);
        Assert.Equal("Bearer tpz_secret-token-value", handler.LastAuthorization);
    }

    [Fact]
    public async Task Names_the_user_a_token_belongs_to()
    {
        var handler = new RecordingHandler(
            """{ "user": { "id": 2, "username": "peppy", "avatarUrl": "https://a.ppy.sh/2", "countryCode": "AU", "isSupporter": true }, "configured": true, "missing": [] }""");
        using var client = new MappoolBuilderClient(new HttpClient(handler));

        var user = await client.GetMeAsync(WithToken);

        Assert.Equal("peppy", user?.Username);
        Assert.Equal("https://pools.jansel.dev/api/me", handler.LastUri);
    }

    [Fact]
    public async Task Lists_own_and_public_pools()
    {
        var handler = new RecordingHandler($$"""{ "mine": [ {{PoolJson}} ], "discover": [] }""");
        using var client = new MappoolBuilderClient(new HttpClient(handler));

        var list = await client.ListPoolsAsync(WithToken);

        Assert.Single(list.Mine);
        Assert.Empty(list.Discover);
    }

    [Fact]
    public async Task A_revoked_token_gets_a_message_that_says_so_without_the_token()
    {
        var handler = new RecordingHandler(
            """{ "error": true, "statusCode": 401, "statusMessage": "Unauthorized" }""", HttpStatusCode.Unauthorized);
        using var client = new MappoolBuilderClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<MappoolBuilderException>(() => client.ListPoolsAsync(WithToken));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Contains("revoked", ex.Message);
        Assert.DoesNotContain("tpz_", ex.Message);
    }

    [Fact]
    public async Task A_missing_or_private_pool_is_one_message()
    {
        var handler = new RecordingHandler(
            """{ "error": true, "statusCode": 404, "statusMessage": "Pool not found" }""", HttpStatusCode.NotFound);
        using var client = new MappoolBuilderClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<MappoolBuilderException>(() => client.GetPoolAsync(Public, "Xb3kQ9aZ"));

        Assert.Contains("private", ex.Message);
    }

    [Fact]
    public async Task Passes_the_servers_own_message_through_for_other_errors()
    {
        var handler = new RecordingHandler(
            """{ "error": true, "statusCode": 400, "statusMessage": "The pool is full." }""", HttpStatusCode.BadRequest);
        using var client = new MappoolBuilderClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<MappoolBuilderException>(() => client.GetPoolAsync(Public, "Xb3kQ9aZ"));

        Assert.Equal("The pool is full.", ex.Message);
    }

    [Fact]
    public async Task A_page_that_is_not_the_api_is_reported_as_a_wrong_address()
    {
        var handler = new RecordingHandler("<html>Welcome</html>");
        using var client = new MappoolBuilderClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<MappoolBuilderException>(() => client.ListPoolsAsync(Public));

        Assert.Contains("address", ex.Message);
    }

    [Fact]
    public async Task Rejects_input_that_holds_no_pool_id_before_asking_the_server()
    {
        var handler = new RecordingHandler(PoolJson);
        using var client = new MappoolBuilderClient(new HttpClient(handler));

        await Assert.ThrowsAsync<MappoolBuilderException>(() => client.GetPoolAsync(Public, "https://osu.ppy.sh/b/129891"));
        Assert.Null(handler.LastUri);
    }

    [Theory]
    [InlineData("Xb3kQ9aZ", "Xb3kQ9aZ")]
    [InlineData("  Xb3kQ9aZ ", "Xb3kQ9aZ")]
    [InlineData("https://pools.jansel.dev/pools/Xb3kQ9aZ", "Xb3kQ9aZ")]
    [InlineData("https://pools.jansel.dev/pools/Xb3k-9_Z/", "Xb3k-9_Z")]
    [InlineData("pools.jansel.dev/pools/Xb3kQ9aZ?tab=maps", "Xb3kQ9aZ")]
    [InlineData("https://pools.jansel.dev/api/pools/Xb3kQ9aZ", "Xb3kQ9aZ")]
    [InlineData("https://pools.jansel.dev/pools/Xb3kQ9aZ#slots", "Xb3kQ9aZ")]
    [InlineData("https://osu.ppy.sh/b/129891", null)]
    [InlineData("Xb3kQ9aZZ", null)]
    [InlineData("", null)]
    public void Finds_the_pool_id_in_what_is_pasted(string input, string? expected) =>
        Assert.Equal(expected, MappoolBuilderClient.ParsePoolId(input));

    [Theory]
    [InlineData("https://pools.jansel.dev", "https://pools.jansel.dev/")]
    [InlineData("https://pools.jansel.dev/", "https://pools.jansel.dev/")]
    [InlineData("pools.jansel.dev", "https://pools.jansel.dev/")]
    [InlineData("http://localhost:3000", "http://localhost:3000/")]
    [InlineData("", "https://pools.jansel.dev/")]
    public void Normalises_the_base_address(string baseUrl, string expected) =>
        Assert.Equal(expected, new MappoolBuilderConnection(baseUrl).Origin.ToString());

    [Fact]
    public void Refuses_an_address_that_is_not_a_web_address() =>
        Assert.Throws<MappoolBuilderException>(() => new MappoolBuilderConnection("ftp://pools.jansel.dev").Origin);

    // ---- import --------------------------------------------------------------

    [Fact]
    public void Labels_picks_the_way_the_builder_does_and_groups_them_by_bracket()
    {
        var pool = MappoolBuilderImport.ToMappool(Pool(), Public);

        // Counted per bracket in the Builder's order, then grouped NM, HD, HR, DT, FM, …, SD, TB.
        Assert.Equal(
            ["NM1", "NM2", "HD1", "HR1", "DT1", "FM1", "FM2", "SD1", "TB"],
            pool.Slots.Select(s => s.Label));
    }

    [Fact]
    public void Carries_the_map_details_over()
    {
        var pool = MappoolBuilderImport.ToMappool(Pool(), Public);
        var hr = pool.Slots.Single(s => s.Label == "HR1");

        Assert.Equal("Showmatch Finals", pool.Name);
        Assert.Equal("Grand Finals", pool.Stage);
        Assert.Equal(129891, hr.BeatmapId);
        Assert.Equal("FREEDOM DiVE", hr.Title);
        Assert.Equal("xi", hr.Artist);
        Assert.Equal("FOUR DIMENSIONS", hr.Difficulty);
        Assert.Equal("Nakagawa-Kanon", hr.Mapper);
        Assert.Equal(8.52, hr.StarRating);
        Assert.Equal(222, hr.Bpm);
        Assert.Equal(257, hr.LengthSeconds);
        Assert.Equal("https://assets.ppy.sh/beatmaps/39804/covers/list@2x.jpg", hr.CoverUrl);
        Assert.Equal("hr0001", hr.RemoteId);
        Assert.Equal("Warmup", pool.Slots.Single(s => s.Label == "HD1").Notes);
    }

    [Fact]
    public void Remembers_where_the_pool_came_from()
    {
        var pool = MappoolBuilderImport.ToMappool(Pool(), new MappoolBuilderConnection("pools.jansel.dev/"));

        Assert.NotNull(pool.Source);
        Assert.Equal("https://pools.jansel.dev", pool.Source.BaseUrl);
        Assert.Equal("Xb3kQ9aZ", pool.Source.PoolId);
        Assert.Equal("peppy", pool.Source.OwnerName);
        Assert.Equal(1790000500000, pool.Source.UpdatedAt);
        Assert.Equal("https://pools.jansel.dev/pools/Xb3kQ9aZ", pool.Source.PageUrl);
    }

    [Theory]
    [InlineData("NM1", Mods.None, "NoMod", "!mp mods NF")]
    [InlineData("HD1", Mods.Hidden, "Hidden", "!mp mods NF HD")]
    [InlineData("HR1", Mods.HardRock, "HardRock", "!mp mods NF HR")]
    [InlineData("DT1", Mods.DoubleTime, "DoubleTime", "!mp mods NF DT")]
    [InlineData("FM1", Mods.FreeMod, "FreeMod", "!mp mods Freemod")]
    [InlineData("SD1", Mods.SuddenDeath, "SuddenDeath", "!mp mods SD")]
    [InlineData("TB", Mods.FreeMod, "Tiebreaker", "!mp mods Freemod")]
    public void Turns_each_bracket_into_the_mods_it_is_played_with(string label, Mods mods, string category, string command)
    {
        var pool = MappoolBuilderImport.ToMappool(Pool(), Public);
        var slot = pool.Slots.Single(s => s.Label == label);

        Assert.Equal(mods, slot.Mods);
        Assert.Equal(category, slot.Category);
        Assert.Equal(command, MpCommands.SetMods(pool.ModsToSend(slot)));
    }

    [Theory]
    [InlineData("EZ", Mods.Easy, "EZ1")]
    [InlineData("FL", Mods.Flashlight, "FL1")]
    [InlineData("HT", Mods.HalfTime, "HT1")]
    [InlineData("hd", Mods.Hidden, "HD1")]
    [InlineData("XX", Mods.None, "NM1")]
    public void Handles_the_rarer_brackets_and_unknown_ones(string bracket, Mods mods, string label)
    {
        var slot = Assert.Single(MappoolBuilderImport.BuildSlots([new RemotePoolSlot { Id = "a", Mod = bracket, BeatmapId = 1 }]));

        Assert.Equal(mods, slot.Mods);
        Assert.Equal(label, slot.Label);
    }

    [Fact]
    public void Several_tiebreakers_keep_their_numbers()
    {
        var slots = MappoolBuilderImport.BuildSlots(
        [
            new RemotePoolSlot { Id = "a", Mod = "TB", BeatmapId = 1 },
            new RemotePoolSlot { Id = "b", Mod = "TB", BeatmapId = 2 },
        ]);

        Assert.Equal(["TB1", "TB2"], slots.Select(s => s.Label));
    }

    [Fact]
    public void An_update_keeps_local_settings_and_reports_what_moved()
    {
        var local = MappoolBuilderImport.ToMappool(Pool(), Public);
        local.Name = "Renamed here";
        local.BestOf = 9;
        local.Slots.Single(s => s.RemoteId == "fm0001").EasyMultiplier = 1.4;

        var remote = Pool();
        remote.UpdatedAt += 1000;
        remote.Slots.RemoveAll(s => s.Id == "nm0002");
        remote.Slots.Single(s => s.Id == "dt0001").BeatmapId = 4242;
        remote.Slots.Add(new RemotePoolSlot { Id = "hd0002", Mod = "HD", BeatmapId = 7777, Title = "New" });

        var result = MappoolBuilderImport.Refresh(local, remote, Public);

        Assert.Equal(new PoolRefreshResult(Added: 1, Removed: 1, Changed: 1, Kept: 7), result);
        Assert.Equal("1 added, 1 removed, 1 changed", result.Describe());
        Assert.Equal("Renamed here", local.Name);
        Assert.Equal(9, local.BestOf);
        Assert.Equal(1.4, local.Slots.Single(s => s.RemoteId == "fm0001").EasyMultiplier);
        Assert.Equal(["NM1", "HD1", "HD2", "HR1", "DT1", "FM1", "FM2", "SD1", "TB"], local.Slots.Select(s => s.Label));
        Assert.Equal(1790000501000, local.Source!.UpdatedAt);
    }

    [Fact]
    public void An_update_without_changes_says_so()
    {
        var local = MappoolBuilderImport.ToMappool(Pool(), Public);

        var result = MappoolBuilderImport.Refresh(local, Pool(), Public);

        Assert.False(result.HasChanges);
        Assert.Equal("no map changes", result.Describe());
    }

    [Fact]
    public void The_source_survives_the_pool_file()
    {
        var store = new MappoolStore(_directory);
        var fileName = store.Save(MappoolBuilderImport.ToMappool(Pool(), Public));

        var loaded = store.Load(fileName)!;

        Assert.Equal("Xb3kQ9aZ", loaded.Source?.PoolId);
        Assert.Equal("hr0001", loaded.Slots.Single(s => s.Label == "HR1").RemoteId);
        Assert.Equal(fileName, store.FindBySource("https://pools.jansel.dev/", "Xb3kQ9aZ"));
        Assert.Null(store.FindBySource("https://pools.jansel.dev", "Other123"));
        Assert.Null(store.FindBySource("https://another.example", "Xb3kQ9aZ"));
    }

    [Fact]
    public void A_pool_made_here_has_no_source_in_its_file()
    {
        var json = JsonSerializer.Serialize(new Mappool(), CastoriceJson.Options);

        Assert.DoesNotContain("source", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_import_never_overwrites_a_pool_with_the_same_name()
    {
        var store = new MappoolStore(_directory);
        store.Save(new Mappool { Name = "Showmatch Finals" });

        Assert.Equal("showmatch-finals-2.json", store.SuggestUnusedFileName("Showmatch Finals"));
    }

    [Fact]
    public void Sudden_death_and_perfect_never_get_NoFail()
    {
        var pool = new Mappool();

        Assert.Equal(Mods.SuddenDeath, pool.ModsToSend(new MappoolSlot { Mods = Mods.SuddenDeath }));
        Assert.Equal(Mods.Perfect, pool.ModsToSend(new MappoolSlot { Mods = Mods.Perfect }));
    }

    private sealed class RecordingHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? LastUri { get; private set; }

        public string? LastAuthorization { get; private set; }

        public string? LastAcceptLanguage { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri?.ToString();
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastAcceptLanguage = request.Headers.AcceptLanguage.ToString();

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
