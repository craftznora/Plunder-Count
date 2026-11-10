namespace PlunderCount;

/// <summary>Made-up but realistic history, used only by --demo to render previews of the overlay and the report.</summary>
public static class Demo
{
    public static TrackerData Data()
    {
        var rng = new Random(7);
        var d = new TrackerData();
        var playlists = new[] { "Playlist_DefaultSquad", "Playlist_NoBuildBR_Squad", "Playlist_DefaultDuo", "Playlist_NoBuildBR_Solo", "Playlist_Habanero_Squad" };
        var kinds = new[] { ChestKinds.Normal, ChestKinds.Normal, ChestKinds.Normal, ChestKinds.Normal, ChestKinds.Rare, ChestKinds.Buried, ChestKinds.Extraction, ChestKinds.PosterChest };
        var treasureItems = new[] { "Feu d'artifice doré", "Extracteur portable", "Localisateur d'esprit", "Localisateur chanceux", "Trésor" };
        var now = DateTime.UtcNow;

        for (var day = 13; day >= 0; day--)
        {
            var games = day % 4 == 2 ? 0 : rng.Next(2, 9);
            for (var g = 0; g < games; g++)
            {
                // Today's games are recent enough to count as "this session".
                var start = day == 0 ? now.AddMinutes(-25 * (games - g)) : now.Date.AddDays(-day).AddHours(17 + g * 0.45);
                var chestsOpened = Math.Max(0, (int)Math.Round(9 + rng.NextDouble() * 12 + (rng.NextDouble() < 0.1 ? 10 : 0) - (rng.NextDouble() < 0.12 ? 9 : 0)));
                var m = new MatchRecord
                {
                    SessionId = Guid.NewGuid().ToString("N"),
                    ReplayFile = $"UnsavedReplay-{start:yyyy.MM.dd-HH.mm.ss}.replay",
                    AnalyzedAtUtc = start.AddMinutes(22),
                    MatchStartUtc = start,
                    DurationSeconds = 900 + rng.Next(600),
                    Playlist = playlists[rng.Next(playlists.Length)],
                    PlayerName = "Satou",
                    IsReplayOwner = true,
                    Placement = rng.NextDouble() < 0.08 ? 1 : rng.Next(2, 60),
                    TotalPlayers = 100,
                    Eliminations = rng.Next(0, 9),
                    ChestsOpened = chestsOpened,
                    AmmoBoxesOpened = rng.Next(0, 6),
                };
                for (var c = 0; c < chestsOpened; c++)
                {
                    m.Chests.Add(new ChestRecord { TimeSeconds = 60 + c * 55, Kind = kinds[rng.Next(kinds.Length)] });
                }
                if (rng.NextDouble() < 0.35)
                {
                    m.TreasureFinds.Add(new TreasureFind
                    {
                        Item = treasureItems[rng.Next(treasureItems.Length)],
                        SourceKind = ChestKinds.Normal,
                        SourceCertain = true,
                        TimeExact = true,
                        ObtainedAtSeconds = 300 + rng.Next(600),
                    });
                }
                d.Matches.Add(m);
            }
        }
        return d;
    }
}
