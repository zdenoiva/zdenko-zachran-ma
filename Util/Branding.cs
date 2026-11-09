using System.Drawing;
using System.Reflection;

namespace NtfsRescue.Util;

public static class Branding
{
    private static Bitmap? _logo;
    private static Icon? _icon;
    private static bool _logoLoaded;
    private static bool _iconLoaded;

    public static Bitmap? Logo
    {
        get
        {
            if (_logoLoaded) return _logo;
            _logoLoaded = true;
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("NtfsRescue.app-icon-64.png");
                if (stream is not null) _logo = new Bitmap(stream);
            }
            catch
            {
                _logo = null;
            }
            return _logo;
        }
    }

    public static Icon? AppIcon
    {
        get
        {
            if (_iconLoaded) return _icon;
            _iconLoaded = true;
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("NtfsRescue.app.ico");
                if (stream is not null) _icon = new Icon(stream);
            }
            catch
            {
                _icon = null;
            }
            return _icon;
        }
    }
}
