using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace AgentArcade;

// The zip has no installer, so Windows search found the zip's .json files instead of the app. Each start puts
// "Agent Arcade" in the user's own Start menu (Start Menu\Programs, no administrator rights) through the shell's
// IShellLink; no PowerShell or script host starts. A shortcut whose copy is gone (its folder was deleted or moved) is
// pointed at the copy that runs now. One that starts another copy that is still there, such as install.ps1's in
// %LOCALAPPDATA%\Programs\AgentArcade, is left alone: that may be the copy you chose, and Settings → About says which
// one it starts. The shortcut gets no AppUserModelID: the app sets none (its notifications are the tray icon's
// balloons), so Windows ties the window, its notifications and the shortcut together by the exe's path, and an ID on
// the shortcut alone would split them.
static class StartMenu
{
    public enum Outcome { Skipped, Made, Repointed, Same, Other, Failed }

    const string Exe = "AgentArcade.exe";

    // At start, off the UI thread; never throws. Target is what the shortcut starts afterwards, or the error. A test
    // copy (AGENT_ARCADE_HOME), a run through dotnet.exe and a copy inside another app's package leave the Start menu
    // alone: Windows redirects such a copy's AppData, so its shortcut would land where the Start menu never looks.
    public static (Outcome Outcome, string Target) AtStart()
    {
        try
        {
            var exe = Environment.ProcessPath;
            var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (AppSettings.Home != null || exe == null || !Path.GetFileName(exe).Equals(Exe, StringComparison.OrdinalIgnoreCase)
                || programs.Length == 0 || Packaged())
                return (Outcome.Skipped, "");
            return Ensure(Path.Combine(programs, "Agent Arcade.lnk"), exe);
        }
        catch (Exception ex)
        {
            return (Outcome.Failed, ex.Message);
        }
    }

    // Makes link start exe, unless it starts something that is still there. The self-check runs this in a temp folder.
    public static (Outcome Outcome, string Target) Ensure(string link, string exe)
    {
        try
        {
            if (!File.Exists(link))
            {
                Write(link, exe);
                return (Outcome.Made, exe);
            }
            // A shortcut to something that isn't a file on disk (a shell folder, say) was made by someone else.
            var target = Target(link);
            if (target.Length == 0) return (Outcome.Other, "");
            if (!File.Exists(target) && !Directory.Exists(target))
            {
                Write(link, exe);
                return (Outcome.Repointed, exe);
            }
            var same = string.Equals(Path.GetFullPath(target), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase);
            return (same ? Outcome.Same : Outcome.Other, target);
        }
        catch (Exception ex)
        {
            return (Outcome.Failed, ex.Message);
        }
    }

    // The folder of the other copy a shortcut starts, for Settings → About; null when it isn't Agent Arcade.
    public static string? CopyFolder(string target)
        => Path.GetFileName(target).Equals(Exe, StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(target) : null;

    // A new shortcut, also in place of one whose copy is gone, so nothing of the old copy (its icon path) stays.
    static void Write(string link, string exe)
    {
        var shell = (IShellLinkW)new ShellLink();
        try
        {
            shell.SetPath(exe);
            shell.SetWorkingDirectory(Path.GetDirectoryName(exe)!);
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            ((IPersistFile)shell).Save(link, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    // Where link points, as the shortcut stores it. It isn't resolved, so a missing target doesn't make Windows search.
    public static string Target(string link)
    {
        var shell = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)shell).Load(link, 0);
            var path = new StringBuilder(1024);
            shell.GetPath(path, path.Capacity, IntPtr.Zero, 0);
            return path.ToString();
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    // Started from a packaged (MSIX) app, this process runs inside that app's package.
    static bool Packaged()
    {
        var length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) != AppModelErrorNoPackage;
    }

    const int AppModelErrorNoPackage = 15700;

    [DllImport("kernel32.dll")]
    static extern int GetCurrentPackageFullName(ref int packageFullNameLength, IntPtr packageFullName);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLink { }

    // IShellLinkW in vtable order; only GetPath, SetWorkingDirectory and SetPath are used.
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int size);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder folder, int size);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string folder);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int size);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int size, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}

// StartMenu.Ensure in a temp folder, never the real Start menu: a new shortcut, one that starts this copy already,
// one that starts another copy that is still there (left alone), and one whose copy is gone (pointed at this one).
static class StartMenuCheck
{
    public static void Run(Action<string, bool, string> check)
    {
        var temp = Path.Combine(Path.GetTempPath(), "AgentArcade-startmenu-" + Environment.ProcessId);
        try
        {
            var first = Path.Combine(temp, "first", "AgentArcade.exe");
            var second = Path.Combine(temp, "second", "AgentArcade.exe");
            foreach (var exe in new[] { first, second })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
                File.WriteAllBytes(exe, []);
            }
            var link = Path.Combine(temp, "Programs", "Agent Arcade.lnk");
            var made = StartMenu.Ensure(link, first);
            var again = StartMenu.Ensure(link, first);
            var other = StartMenu.Ensure(link, second);
            var kept = StartMenu.Target(link);
            Directory.Delete(Path.GetDirectoryName(first)!, true);
            var moved = StartMenu.Ensure(link, second);
            var now = StartMenu.Target(link);
            var pass = made.Outcome == StartMenu.Outcome.Made && again.Outcome == StartMenu.Outcome.Same
                && other.Outcome == StartMenu.Outcome.Other && Same(other.Target, first) && Same(kept, first)
                && StartMenu.CopyFolder(other.Target) == Path.GetDirectoryName(first) && StartMenu.CopyFolder(Path.Combine(temp, "other.exe")) == null
                && moved.Outcome == StartMenu.Outcome.Repointed && Same(now, second);
            check("start-menu", pass, pass
                ? "a missing shortcut is made; one that starts this copy, or another copy that is there, is left alone; one whose copy is gone starts this copy"
                : made.Outcome + ", " + again.Outcome + ", " + other.Outcome + ", " + moved.Outcome +
                    (made.Outcome == StartMenu.Outcome.Failed ? " (" + made.Target + ")" : ""));
        }
        catch (Exception ex)
        {
            check("start-menu", false, ex.Message);
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { /* temp folder */ }
        }
    }

    static bool Same(string a, string b) => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
