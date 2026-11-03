using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using CustomSnipping;

internal static class VerificationHarness
{
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(MainForm form, string name) { return typeof(MainForm).GetField(name, Flags).GetValue(form); }
    private static object Call(MainForm form, string name, params object[] arguments)
    {
        try { return typeof(MainForm).GetMethod(name, Flags).Invoke(form, arguments); }
        catch (TargetInvocationException ex) { throw ex.InnerException; }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void CaptureRectangle(MainForm form)
    {
        using (var timer = new System.Windows.Forms.Timer { Interval = 40 })
        {
            timer.Tick += delegate
            {
                foreach (Form candidate in Application.OpenForms)
                {
                    if (!(candidate is CaptureOverlay)) continue;
                    timer.Stop();
                    Native.SendMessage(candidate.Handle, 0x201, new IntPtr(1), new IntPtr((100 << 16) | 100));
                    Native.SendMessage(candidate.Handle, 0x200, new IntPtr(1), new IntPtr((190 << 16) | 260));
                    Native.SendMessage(candidate.Handle, 0x202, IntPtr.Zero, new IntPtr((190 << 16) | 260));
                    break;
                }
            };
            timer.Start();
            Call(form, "CaptureScreen", "Rectangle");
        }
    }
    [STAThread]
    public static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        int result = 1;
        using (var form = new MainForm(false))
        {
            typeof(MainForm).GetField("keysEnabled", Flags).SetValue(form, false);
            form.Shown += async delegate { result = await Run(form); };
            Application.Run(form);
        }
        return result;
    }

    private static async Task<int> Run(MainForm form)
    {
        var files = new System.Collections.Generic.List<string>();
        {
            try
            {
                typeof(MainForm).GetField("keysEnabled", Flags).SetValue(form, false);
                var settings = (Preferences)Field(form, "preferences");
                Check(form.FormBorderStyle == FormBorderStyle.None, "Native window border remains.");
                foreach (bool auto in new[] { false, true })
                foreach (bool show in new[] { false, true })
                {
                    settings.AutoSave = auto; settings.ShowAfterCapture = show;
                    form.Show(); Application.DoEvents();
                    CaptureRectangle(form);
                    Check(form.Visible == show, "Wrong post-capture visibility.");
                    string path = (string)Field(form, "currentPath");
                    Check(auto ? path != null && File.Exists(path) : path == null, "Wrong auto-save result.");
                    if (path != null) files.Add(path);
                    Check(Clipboard.ContainsImage(), "Capture was not copied.");
                    using (var copied = Clipboard.GetImage())
                        Check(copied.Width == 160 && copied.Height == 90, "Dragged rectangle dimensions are wrong.");
                    if (!auto)
                    {
                        Call(form, "SaveCurrent");
                        path = (string)Field(form, "currentPath"); files.Add(path);
                        Check(File.Exists(path), "Manual save failed.");
                    }
                    Call(form, "ClearContent");
                    using (var saved = Image.FromFile(path))
                        Check(saved.Width == 160 && saved.Height == 90, "Saved rectangle dimensions are wrong.");
                    Check(Field(form, "current") == null && ((TextBox)Field(form, "text")).Text.Length == 0 && File.Exists(path), "Clear failed or deleted a file.");
                    Console.WriteLine("PASS: AutoSave={0}, ShowAfterCapture={1}, clipboard, manual save, Clear.", auto, show);
                }
                using (var button = new StudioButton { BackColor = Color.FromArgb(34, 44, 60), Size = new Size(110, 38) })
                {
                    form.Controls.Add(button);
                    StudioButton.AnimationsEnabled = true;
                    typeof(StudioButton).GetMethod("OnMouseEnter", Flags).Invoke(button, new object[] { EventArgs.Empty });
                    await Task.Delay(220);
                    float glow = (float)typeof(StudioButton).GetField("glow", Flags).GetValue(button);
                    Check(glow > 0.5f && glow <= 0.66f, "Hover fade did not reach its target.");
                    typeof(StudioButton).GetMethod("OnMouseLeave", Flags).Invoke(button, new object[] { EventArgs.Empty });
                    await Task.Delay(220);
                    glow = (float)typeof(StudioButton).GetField("glow", Flags).GetValue(button);
                    Check(glow < 0.04f, "Hover fade did not return to rest.");
                    Console.WriteLine("PASS: Button hover fades reach their target and return to rest.");
                    form.Controls.Remove(button);
                }
                settings.AutoSave = false;
                var image = new Bitmap(800, 140);
                using (Graphics graphics = Graphics.FromImage(image))
                using (var font = new Font("Arial", 32))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawString("UNSAVED OCR TEST 123", font, Brushes.Black, 15, 25);
                }
                Call(form, "SetCurrent", image, null);
                Task task = (Task)Call(form, "ExtractText");
                Task finished = await Task.WhenAny(task, Task.Delay(65000));
                Check(finished == task, "OCR did not complete.");
                await task;
                Check(((TextBox)Field(form, "text")).Text.Contains("UNSAVED OCR TEST 123"), "Unsaved OCR text mismatch.");
                Check(Field(form, "currentPath") == null && Clipboard.ContainsText(), "Unsaved OCR unexpectedly saved or did not copy text.");
                Call(form, "ClearContent");
                Check(!Clipboard.ContainsText(), "Owned OCR text was not cleared.");
                Console.WriteLine("PASS: Unsaved screenshot OCR and owned-text Clear.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally
            {
                typeof(MainForm).GetField("exiting", Flags).SetValue(form, true); form.Close();
                foreach (string file in files) File.Delete(file);
            }
        }
    }
}
