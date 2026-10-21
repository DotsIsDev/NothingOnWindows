using Microsoft.Win32;

namespace NothingOnWindows.UI;

internal interface IStartupRegistration
{
    bool Enabled {get;}
    void SetEnabled(bool enabled);
}

internal sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string KeyPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName="NothingOnWindows";
    private static string Executable=>Path.Combine(AppContext.BaseDirectory,"NothingOnWindows.exe");
    internal static string CommandFor(string executable)
    {
        if(!Path.IsPathFullyQualified(executable)||executable.Contains('"'))throw new IOException("Use an extracted application folder to enable startup.");
        string command=$"\"{executable}\" --startup";
        if(command.Length>260)throw new IOException("Move the app to a shorter folder path before enabling startup.");
        return command;
    }
    public bool Enabled
    {
        get
        {
            using var key=Registry.CurrentUser.OpenSubKey(KeyPath);
            return string.Equals(key?.GetValue(ValueName) as string,CommandFor(Executable),StringComparison.OrdinalIgnoreCase);
        }
    }
    public void SetEnabled(bool enabled)
    {
        if(enabled&&!File.Exists(Executable))throw new IOException("Extract the complete app before enabling startup.");
        using var key=Registry.CurrentUser.CreateSubKey(KeyPath,true)??throw new IOException("Windows could not update startup settings.");
        if(enabled)key.SetValue(ValueName,CommandFor(Executable),RegistryValueKind.String);
        else key.DeleteValue(ValueName,false);
    }
}
