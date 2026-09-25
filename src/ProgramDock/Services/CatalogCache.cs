using System.IO;
using System.Text;
using System.Text.Json;
using ProgramDock.Models;

namespace ProgramDock.Services;

public sealed class CatalogCache
{
    private readonly string _catalogPath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    public CatalogCache()
    {
        AppPaths.MigrateLegacyData();
        _catalogPath = AppPaths.CatalogPath;
    }

    public string CatalogPath => _catalogPath;

    public IReadOnlyList<ProgramEntry> Load()
    {
        try
        {
            if (!File.Exists(_catalogPath))
            {
                return [];
            }

            var records = JsonSerializer.Deserialize<List<CatalogRecord>>(
                File.ReadAllText(_catalogPath, Encoding.UTF8),
                _jsonOptions) ?? [];
            return records
                .Where(record => !string.IsNullOrWhiteSpace(record.Path))
                .Select(record =>
                {
                    var source = Enum.IsDefined(typeof(ProgramSource), record.Source)
                        ? (ProgramSource)record.Source
                        : ProgramSource.Disk;
                    var entry = new ProgramEntry(
                        record.Path,
                        record.Name,
                        source,
                        record.Size,
                        record.ModifiedAt,
                        record.Description,
                        record.IconPath,
                        record.IsSystemComponent,
                        record.IsCommandLineTool);
                    var autoCategory = Enum.IsDefined(typeof(ProgramCategory), record.AutoCategory)
                        ? (ProgramCategory)record.AutoCategory
                        : ProgramCategory.Unclassified;
                    entry.ApplyClassification(
                        autoCategory,
                        record.CategoryReason,
                        autoCategory,
                        hasManualOverride: false);
                    return entry;
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public void Save(IEnumerable<ProgramEntry> entries)
    {
        try
        {
            var directory = Path.GetDirectoryName(_catalogPath)!;
            Directory.CreateDirectory(directory);
            var records = entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
                .Select(entry => new CatalogRecord
                {
                    Path = entry.Path,
                    Name = entry.Name,
                    Source = (int)entry.Source,
                    Size = entry.Size,
                    ModifiedAt = entry.ModifiedAt,
                    Description = entry.Description,
                    IconPath = entry.IconPath,
                    AutoCategory = (int)entry.AutoCategory,
                    CategoryReason = entry.CategoryReason,
                    IsSystemComponent = entry.IsSystemComponent,
                    IsCommandLineTool = entry.IsCommandLineTool,
                    ClassificationVersion = 1
                })
                .ToList();
            var temporaryPath = _catalogPath + ".tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(records, _jsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, _catalogPath, overwrite: true);
        }
        catch
        {
            // A cache failure must never stop scanning or launching programs.
        }
    }

    private sealed class CatalogRecord
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Source { get; set; }
        public long Size { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public string Description { get; set; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;
        public int AutoCategory { get; set; }
        public string CategoryReason { get; set; } = string.Empty;
        public bool IsSystemComponent { get; set; }
        public bool IsCommandLineTool { get; set; }
        public int ClassificationVersion { get; set; }
    }
}
