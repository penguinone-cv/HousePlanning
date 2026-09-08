using System.IO.Compression;
using System.Text.Json;
namespace HousePlanning.Core;
public static class HouseArchive
{
    public static void Save(House house, string path)
    {
        house.Validate();
        var h = house.Clone();
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var z = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                for (int i = 0; i < h.Floors.Count; i++)
                    if (h.Floors[i].Blueprint is { } b)
                    {
                        b.Asset = $"blueprints/{i}.png";
                        using var stream = z.CreateEntry(b.Asset).Open();
                        stream.Write(b.Image);
                    }
                using var json = z.CreateEntry("project.json").Open();
                JsonSerializer.Serialize(json, h, House.JsonOptions);
            }
            if (File.Exists(path))
                File.Replace(temp, path, path + ".bak");
            else
                File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static House Load(string path)
    {
        try
        {
            using var z = ZipFile.OpenRead(path);
            var entry = z.GetEntry("project.json") ?? throw new InvalidDataException("project.jsonがありません。");
            if (entry.Length > 16 * 1024 * 1024)
                throw new InvalidDataException("プロジェクトが大きすぎます。");
            using var stream = entry.Open();
            var h = JsonSerializer.Deserialize<House>(stream, House.JsonOptions) ?? throw new InvalidDataException("空のプロジェクトです。");
            foreach (var f in h.Floors)
                if (f.Blueprint is { } b)
                {
                    var img = z.GetEntry(b.Asset) ?? throw new InvalidDataException("下敷き画像がありません。");
                    if (img.Length > 64 * 1024 * 1024)
                        throw new InvalidDataException("画像が大きすぎます。");
                    using var s = img.Open();
                    using var m = new MemoryStream();
                    s.CopyTo(m);
                    b.Image = m.ToArray();
                }
            h.Validate();
            return h;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or NullReferenceException) { throw new InvalidDataException("保存ファイルの内容が不正です。" + e.Message, e); }
    }
}
