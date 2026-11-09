using System.Text.Json;

namespace NtfsRescue.Util;

public static class AppSettings
{
    private sealed class Model
    {
        public string EnvironmentCheckedFor { get; set; } = "";
    }

    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine(AppPaths.Base, "settings.json");
    private static Model _model = new();

    public static string EnvironmentCheckedFor
    {
        get { lock (Gate) return _model.EnvironmentCheckedFor; }
        set { lock (Gate) _model.EnvironmentCheckedFor = value; }
    }

    public static void Load()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    _model = JsonSerializer.Deserialize<Model>(json) ?? new Model();
                }
            }
            catch
            {
                _model = new Model();
            }
        }
    }

    public static void Save()
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.Base);
                var json = JsonSerializer.Serialize(_model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // nastavenia nesmu zhodiť aplikáciu
            }
        }
    }
}
