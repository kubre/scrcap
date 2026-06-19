using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Scrcap.Core;

namespace Scrcap.UiAutomation.Tests;

public sealed class EditorProcessAutomationTests
{
    [Fact]
    public void ProcessDrivenEditorDrawsToolsSavesPngAndDumpsState()
    {
        var appDll = Path.Combine(AppContext.BaseDirectory, "Scrcap.Windows.UI.dll");
        Assert.True(File.Exists(appDll), $"Could not find app DLL at {appDll}.");

        using var temp = new TempDirectory();
        var sample = Path.Combine(temp.Path, "sample.png");
        var settingsDir = Path.Combine(temp.Path, "settings");
        var outputDir = Path.Combine(temp.Path, "output");
        var statePath = Path.Combine(temp.Path, "editor-state.json");
        Directory.CreateDirectory(settingsDir);
        Directory.CreateDirectory(outputDir);
        WriteSamplePng(sample, 320, 220);
        WriteSettings(settingsDir, outputDir);

        using var process = StartEditor(appDll, sample, settingsDir, statePath);
        try
        {
            var root = WaitForMainWindow(process);
            Click(root, "ToolArrow");
            PressKey(VirtualKey.Q);
            Drag(root, "EditorCanvas", 32, 36, 180, 64);
            Click(root, "ToolRectangle");
            PressKey(VirtualKey.W);
            Drag(root, "EditorCanvas", 42, 82, 156, 154);
            Click(root, "ToolCounter");
            PressKey(VirtualKey.E);
            Drag(root, "EditorCanvas", 218, 78, 218, 78);
            Click(root, "ToolText");
            PressKey(VirtualKey.R);
            ClickCanvas(root, 44, 178);
            TypeText("QA");
            PressKey(VirtualKey.Shift, down: true);
            PressKey(VirtualKey.Enter);
            PressKey(VirtualKey.Shift, down: false);
            Click(root, "ToolPixelate");
            PressKey(VirtualKey.T);
            Drag(root, "EditorCanvas", 204, 116, 292, 188);
            PressKey(VirtualKey.Control, down: true);
            PressKey(VirtualKey.Alt, down: true);
            PressKey(VirtualKey.D);
            PressKey(VirtualKey.Alt, down: false);
            PressKey(VirtualKey.Control, down: false);
            var preUndoShapes = WaitForStateKinds(statePath);
            Assert.Contains("pixelate", preUndoShapes);

            PressKey(VirtualKey.Control, down: true);
            PressKey(VirtualKey.Z);
            PressKey(VirtualKey.Control, down: false);
            Click(root, "ToolCrop");
            PressKey(VirtualKey.Y);
            Drag(root, "EditorCanvas", 10, 10, 300, 200);
            Click(root, "SaveButton");

            Assert.True(process.WaitForExit(15_000), "Editor process did not exit after Save.");
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }

        var saved = Assert.Single(Directory.EnumerateFiles(outputDir, "process-editor*.png"));
        using (var stream = File.OpenRead(saved))
        {
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            Assert.Equal(290, frame.PixelWidth);
            Assert.Equal(190, frame.PixelHeight);
            Assert.Equal(96, frame.DpiX, precision: 0);
            Assert.Equal(96, frame.DpiY, precision: 0);
        }

        using var state = JsonDocument.Parse(File.ReadAllText(statePath));
        var shapes = state.RootElement.GetProperty("shapes").EnumerateArray().ToArray();
        Assert.Equal(["arrow", "rectangle", "counter", "text"], shapes.Select(shape => shape.GetProperty("kind").GetString()));
        Assert.DoesNotContain(shapes, shape => shape.GetProperty("kind").GetString() == "pixelate");
        Assert.Contains(shapes, shape => string.Equals(shape.GetProperty("text").GetString(), "qa", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(290, state.RootElement.GetProperty("document").GetProperty("width").GetDouble());
        Assert.Equal(190, state.RootElement.GetProperty("document").GetProperty("height").GetDouble());
    }

    private static Process StartEditor(string appDll, string sample, string settingsDir, string statePath)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        startInfo.ArgumentList.Add(appDll);
        startInfo.ArgumentList.Add("--test-mode");
        startInfo.ArgumentList.Add("--open-sample-editor");
        startInfo.ArgumentList.Add(sample);
        startInfo.ArgumentList.Add("--test-settings-dir");
        startInfo.ArgumentList.Add(settingsDir);
        startInfo.ArgumentList.Add("--test-editor-state-json");
        startInfo.ArgumentList.Add(statePath);

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start scrcap test process.");
    }

    private static AutomationElement WaitForMainWindow(Process process)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"scrcap editor process exited before UI Automation found the window. ExitCode={process.ExitCode} stdout={process.StandardOutput.ReadToEnd()} stderr={process.StandardError.ReadToEnd()}");
            }

            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
            {
                try
                {
                    var match = AutomationElement.FromHandle(process.MainWindowHandle);
                    _ = match.Current.BoundingRectangle;
                    return match;
                }
                catch (ElementNotAvailableException)
                {
                    // WPF can publish then replace the HWND during startup; retry until it stabilizes.
                }
                catch (COMException)
                {
                    // UI Automation can briefly time out while the WPF HWND is still initializing.
                }
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException("Could not locate scrcap editor window through UI Automation.");
    }

    private static void Click(AutomationElement root, string automationId)
    {
        var element = WaitForElement(root, automationId);
        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
        {
            ((InvokePattern)pattern).Invoke();
            Thread.Sleep(100);
            return;
        }

        ClickCenter(element);
    }

    private static AutomationElement WaitForElement(AutomationElement root, string automationId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var match = root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
            if (match is not null)
            {
                return match;
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException($"Could not locate AutomationId '{automationId}'.");
    }

    private static IReadOnlyList<string> WaitForStateKinds(string statePath)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(statePath))
            {
                using var state = JsonDocument.Parse(File.ReadAllText(statePath));
                return state.RootElement
                    .GetProperty("shapes")
                    .EnumerateArray()
                    .Select(shape => shape.GetProperty("kind").GetString() ?? string.Empty)
                    .ToArray();
            }

            Thread.Sleep(50);
        }

        throw new TimeoutException("Editor did not write test state JSON.");
    }

    private static void Drag(AutomationElement root, string automationId, int startX, int startY, int endX, int endY)
    {
        var rect = WaitForElement(root, automationId).Current.BoundingRectangle;
        var start = new System.Drawing.Point((int)Math.Round(rect.Left + startX), (int)Math.Round(rect.Top + startY));
        var end = new System.Drawing.Point((int)Math.Round(rect.Left + endX), (int)Math.Round(rect.Top + endY));
        SetCursorPos(start.X, start.Y);
        Mouse(MouseEventFlags.LeftDown);
        Thread.Sleep(50);
        SetCursorPos(end.X, end.Y);
        Thread.Sleep(50);
        Mouse(MouseEventFlags.LeftUp);
        Thread.Sleep(150);
    }

    private static void ClickCanvas(AutomationElement root, int x, int y)
    {
        var rect = WaitForElement(root, "EditorCanvas").Current.BoundingRectangle;
        ClickAt((int)Math.Round(rect.Left + x), (int)Math.Round(rect.Top + y));
    }

    private static void ClickCenter(AutomationElement element)
    {
        var rect = element.Current.BoundingRectangle;
        ClickAt((int)Math.Round(rect.Left + rect.Width / 2), (int)Math.Round(rect.Top + rect.Height / 2));
    }

    private static void ClickAt(int x, int y)
    {
        SetCursorPos(x, y);
        Mouse(MouseEventFlags.LeftDown);
        Thread.Sleep(30);
        Mouse(MouseEventFlags.LeftUp);
        Thread.Sleep(120);
    }

    private static void TypeText(string text)
    {
        foreach (var character in text)
        {
            var code = VkKeyScan(character);
            if (code == -1)
            {
                throw new InvalidOperationException($"Cannot type '{character}'.");
            }

            if ((code & 0x0100) != 0)
            {
                PressKey(VirtualKey.Shift, down: true);
            }

            PressKey((VirtualKey)(code & 0xFF));
            if ((code & 0x0100) != 0)
            {
                PressKey(VirtualKey.Shift, down: false);
            }
        }
    }

    private static void PressKey(VirtualKey key, bool? down = null)
    {
        if (down is null or true)
        {
            KeybdEvent((byte)key, 0, 0, UIntPtr.Zero);
        }

        if (down is null or false)
        {
            KeybdEvent((byte)key, 0, KeyEventFlags.KeyUp, UIntPtr.Zero);
        }

        Thread.Sleep(40);
    }

    private static void Mouse(MouseEventFlags flags) => MouseEvent(flags, 0, 0, 0, UIntPtr.Zero);

    private static void WriteSamplePng(string path, int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, System.Drawing.Color.FromArgb(255, x % 255, y % 255, 240));
            }
        }

        bitmap.Save(path, ImageFormat.Png);
    }

    private static void WriteSettings(string settingsDir, string outputDir)
    {
        var settings = Settings.Defaults();
        settings.SaveFolder = outputDir;
        settings.FilenamePattern = "process-editor";
        settings.ExportScale = 1;
        settings.TextEnterBehavior = TextEnterBehavior.Newline;
        settings.EscBehavior = EscBehavior.CloseOnly;
        File.WriteAllText(
            Path.Combine(settingsDir, "settings.json"),
            JsonSerializer.Serialize(settings, SettingsStore.JsonOptions));
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void MouseEvent(MouseEventFlags flags, int dx, int dy, int data, UIntPtr extraInfo);

    [DllImport("user32.dll", EntryPoint = "keybd_event")]
    private static extern void KeybdEvent(byte virtualKey, byte scanCode, KeyEventFlags flags, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    [Flags]
    private enum MouseEventFlags : uint
    {
        LeftDown = 0x0002,
        LeftUp = 0x0004,
    }

    [Flags]
    private enum KeyEventFlags : uint
    {
        KeyUp = 0x0002,
    }

    private enum VirtualKey : byte
    {
        Shift = 0x10,
        Control = 0x11,
        Alt = 0x12,
        Enter = 0x0D,
        D = 0x44,
        E = 0x45,
        Q = 0x51,
        R = 0x52,
        T = 0x54,
        W = 0x57,
        Y = 0x59,
        Z = 0x5A,
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scrcap-editor-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }
}
