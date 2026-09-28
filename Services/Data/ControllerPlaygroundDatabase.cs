using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using System.IO;
using ControllerPlayground.Models;

namespace ControllerPlayground.Services.Data {
    public sealed class ControllerPlaygroundDatabase {
        private readonly string _databasePath;

        public ControllerPlaygroundDatabase() {
            string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerPlayground");
            
            Directory.CreateDirectory(dataFolder);

            _databasePath = Path.Combine(dataFolder, "ControllerPlayground.db");
        }
        public string DatabasePath => _databasePath;

        private SqliteConnection CreateConnection() {
            return new SqliteConnection($"Data Source={_databasePath}");
        }
        public async Task InitializeAsync() {
            await using SqliteConnection connection = CreateConnection();

            await connection.OpenAsync();

            await using SqliteCommand command = connection.CreateCommand();

            command.CommandText = """
                CREATE TABLE IF NOT EXISTS SteamGames (
                AppId INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Developer TEXT NOT NULL,
                Publisher TEXT NOT NULL,
                ReleaseDate TEXT NOT NULL,
                LibraryCapsuleUrl TEXT NOT NULL,
                HeroImageUrl TEXT NOT NULL,
                LibraryLogoUrl TEXT NOT NULL,
                MetadataUpdatedUtc TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }
        public async Task UpsertSteamGamesAsync(IEnumerable<SteamLibraryGame> games) {
            await using SqliteConnection connection = CreateConnection();

            await connection.OpenAsync();

            using SqliteTransaction transaction = connection.BeginTransaction();

            string updatedUtc = DateTime.UtcNow.ToString("O");

            foreach (SteamLibraryGame game in games) {
                await using SqliteCommand command = connection.CreateCommand();

                command.Transaction = transaction;

                command.CommandText = """
                    INSERT INTO SteamGames (
                    AppId, 
                    Name, 
                    Developer, 
                    Publisher, 
                    ReleaseDate, 
                    LibraryCapsuleUrl, 
                    HeroImageUrl, 
                    LibraryLogoUrl, 
                    MetadataUpdatedUtc
                    )
                    VALUES (
                        $appId,
                        $name,
                        $developer,
                        $publisher,
                        $releaseDate,
                        $libraryCapsuleUrl,
                        $heroImageUrl,
                        $libraryLogoUrl,
                        $metadataUpdatedUtc
                    )
                    ON CONFLICT(AppId) DO UPDATE SET
                        Name = excluded.Name,
                        Developer = excluded.Developer,
                        Publisher = excluded.Publisher,
                        ReleaseDate = excluded.ReleaseDate,
                        LibraryCapsuleUrl = excluded.LibraryCapsuleUrl,
                        HeroImageUrl = excluded.HeroImageUrl,
                        LibraryLogoUrl = excluded.LibraryLogoUrl,
                        MetadataUpdatedUtc = excluded.MetadataUpdatedUtc;
                    """;

                command.Parameters.AddWithValue("$appId", (long)game.AppId);
                command.Parameters.AddWithValue("$name", game.Name ?? string.Empty);
                command.Parameters.AddWithValue("$developer", game.Developer ?? string.Empty);
                command.Parameters.AddWithValue("$publisher", game.Publisher ?? string.Empty);
                command.Parameters.AddWithValue("$releaseDate", game.ReleaseDate ?? string.Empty);
                command.Parameters.AddWithValue("$libraryCapsuleUrl", game.LibraryCapsuleUrl ?? string.Empty);
                command.Parameters.AddWithValue("$heroImageUrl", game.HeroImageUrl ?? string.Empty);
                command.Parameters.AddWithValue("$libraryLogoUrl", game.LibraryLogoUrl ?? string.Empty);
                command.Parameters.AddWithValue("$metadataUpdatedUtc", updatedUtc);

                await command.ExecuteNonQueryAsync();
            }
            await transaction.CommitAsync();
        }
        public async Task<IReadOnlyList<SteamLibraryGame>> GetSteamGamesAsync() {
            List<SteamLibraryGame> games = new();

            await using SqliteConnection connection = CreateConnection();

            await connection.OpenAsync();

            await using SqliteCommand command = connection.CreateCommand();

            command.CommandText = """
                SELECT
                    AppId, 
                    Name, 
                    Developer, 
                    Publisher, 
                    ReleaseDate, 
                    LibraryCapsuleUrl, 
                    HeroImageUrl, 
                    LibraryLogoUrl
                FROM SteamGames
                ORDER BY Name;
                """;

            await using SqliteDataReader reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync()) {
                games.Add(new SteamLibraryGame {
                    AppId = (uint)reader.GetInt64(0),
                    Name = reader.GetString(1),
                    Developer = reader.GetString(2),
                    Publisher = reader.GetString(3),
                    ReleaseDate = reader.GetString(4),
                    LibraryCapsuleUrl = reader.GetString(5),
                    HeroImageUrl = reader.GetString(6),
                    LibraryLogoUrl = reader.GetString(7)
                });
            }
            return games;
        }
    }
}
