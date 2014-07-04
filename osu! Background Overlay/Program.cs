﻿using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace osu__Background_Overlay
{
    class Program
    {
        #region Pinvoke objects

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;        // x position of upper-left corner
            public int Top;         // y position of upper-left corner
            public int Right;       // x position of lower-right corner
            public int Bottom;      // y position of lower-right corner
        }

        public enum TernaryRasterOperations : uint
        {
            /// <summary>dest = source</summary>
            SRCCOPY = 0x00CC0020,
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public int mouseData;
            public int dwFlags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public short wVk;
            public short wScan;
            public int dwFlags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT
        {
            public int uMsg;
            public short wParamL;
            public short wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct INPUT
        {
            [FieldOffset(0)]
            public int type;
            [FieldOffset(4)]
            public MOUSEINPUT mi;
            [FieldOffset(4)]
            public KEYBDINPUT ki;
            [FieldOffset(4)]
            public HARDWAREINPUT hi;
        }
        #endregion

        #region Pinvokes
        /// <summary>
        /// Gets the client rectangle of a window.
        /// </summary>
        /// <param name="hWnd">The wiindow handle.</param>
        /// <param name="lpRect">The rectangle to palce the coordinates.</param>
        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        /// <summary>
        /// Gets the desktop background handle.
        /// </summary>
        /// <returns>A pointer to the desktop background window.</returns>
        [DllImport("user32.dll")]
        static extern IntPtr GetShellWindow();

        /// <summary>
        ///Performs a bit-block transfer of the color data corresponding to a
        ///rectangle of pixels from the specified source device context into
        ///a destination device context.
        /// </summary>
        /// <param name="hdc">Handle to the destination device context.</param>
        /// <param name="nXDest">The leftmost x-coordinate of the destination rectangle (in pixels).</param>
        /// <param name="nYDest">The topmost y-coordinate of the destination rectangle (in pixels).</param>
        /// <param name="nWidth">The width of the source and destination rectangles (in pixels).</param>
        /// <param name="nHeight">The height of the source and the destination rectangles (in pixels).</param>
        /// <param name="hdcSrc">Handle to the source device context.</param>
        /// <param name="nXSrc">The leftmost x-coordinate of the source rectangle (in pixels).</param>
        /// <param name="nYSrc">The topmost y-coordinate of the source rectangle (in pixels).</param>
        /// <param name="dwRop">A raster-operation code.</param>
        /// <returns>
        ///    <c>true</c> if the operation succeedes, <c>false</c> otherwise. To get extended error information, call <see cref="System.Runtime.InteropServices.Marshal.GetLastWin32Error"/>.
        /// </returns>
        [DllImport("gdi32.dll", EntryPoint = "BitBlt", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool BitBlt([In] IntPtr hdc, int nXDest, int nYDest, int nWidth, int nHeight, [In] IntPtr hdcSrc, int nXSrc, int nYSrc, TernaryRasterOperations dwRop);

        [DllImport("user32.dll")]
        static extern bool ClientToScreen(IntPtr hwnd, out Point lpPoint);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true)]
        public static extern IntPtr ReleaseDC(IntPtr hWnd, IntPtr hDC);

        /// <summary>
        /// The GetForegroundWindow function returns a handle to the foreground window.
        /// </summary>
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// Synthesizes keystrokes, mouse motions, and button clicks.
        /// </summary>
        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray), In] INPUT[] pInputs, int cbSize);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        #endregion

        /// <summary>
        /// Rough thread delay in milliseconds.
        /// </summary>
        private const int PROCESS_UPDATE_DELAY = 100;
        /// <summary>
        /// Skin files modified by this program will backup previous files
        /// under this name.
        /// </summary>
        private const string SKIN_BACKUP_PREFIX = "BGOBackup_";
        private const string OSU_BACKGROUND_NAME = "menu-background.jpg";

        private static RECT ClipRectangle;
        private static Point OsuLocation = new Point(int.MinValue, int.MinValue);
        private static Process OsuProcess;
        private static FileSystemWatcher BackgroundWatcher;

        //Use a global variable so we can lock the file
        private static Bitmap clippedBG = new Bitmap(1, 1);

        private static bool IsShown;

        static void Main()
        {
            //Initialize the system tray
            //Build the menu
            ContextMenu TrayStrip = new ContextMenu();
            MenuItem ShowHideMenuItem = new MenuItem("Show the console");
            MenuItem ExitMenuItem = new MenuItem("Exit o!BGO");
            ShowHideMenuItem.Click += ShowHideMenuItem_Click;
            ExitMenuItem.Click += (sender, e) => Environment.Exit(0);

            TrayStrip.MenuItems.Add(ShowHideMenuItem);
            TrayStrip.MenuItems.Add("-");
            TrayStrip.MenuItems.Add(ExitMenuItem);

            //Build the icon
            NotifyIcon TrayIcon = new NotifyIcon();
            TrayIcon.ContextMenu = TrayStrip;
            TrayIcon.Icon = Properties.Resources.Icon;
            TrayIcon.Text = @"osu! Background Overlay";
            TrayIcon.Visible = true;

            //Application.Run() doesn't return so we move all code to
            //A separate thread
            Thread mainThread = new Thread(ProgramMain)
            {
                IsBackground = true
            };
            mainThread.Start();

            //This is required to show the menu
            Application.Run();
        }

        static void ShowHideMenuItem_Click(object sender, EventArgs e)
        {
            if (IsShown)
            {
                ShowWindow(GetConsoleWindow(), 0);
                ((MenuItem)sender).Text = @"Show the console";
            }
            else
            {
                ((MenuItem)sender).Text = @"Hide the console";
                ShowWindow(GetConsoleWindow(), 5);
            }
            IsShown = !IsShown;
        }

        private static void ProgramMain()
        {
            //Initially hide the console
            ShowWindow(GetConsoleWindow(), 0);

            Console.SetWindowSize(60, 10);
            Console.SetBufferSize(60, 500);
            Log("Determining your Windows wallpaper directory...");
            string wallpaperFolder = (string)Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop").GetValue("Wallpaper");
            if (wallpaperFolder == null)
            {
                Log("Failed to determine the operating system wallpaper directory.\n" +
                                    "Press any key to exit.");
                Console.ReadKey();
                Environment.Exit(1);
            }

            BackgroundWatcher = new FileSystemWatcher(Path.GetDirectoryName(wallpaperFolder));
            BackgroundWatcher.Changed += (sender, e) => WallpaperChanged(1000);
            BackgroundWatcher.EnableRaisingEvents = true;

            Thread monitorThread = new Thread(MonitorOsu)
            {
                IsBackground = true
            };
            monitorThread.Start();

            while (true)
            {
                Console.ReadKey();
            }
        }

        private static void WallpaperChanged(int sleepTime)
        {
            BackgroundWatcher.EnableRaisingEvents = false;
            Thread.Sleep(sleepTime);

            if (OsuProcess != null)
            {
                string osuDirectory = Path.GetDirectoryName(OsuProcess.Modules[0].FileName);
                string currentSkinDirectory = "";
                //Read the user's skin file
                using (StreamReader sR = new StreamReader(Path.Combine(osuDirectory, "osu!." + Environment.UserName + ".cfg")))
                {
                    while (sR.Peek() != -1)
                    {
                        string line = sR.ReadLine();

                        //Check for empty lines
                        if (line.Length == 0)
                            continue;

                        //Check for comment
                        if (line.StartsWith("#"))
                            continue;

                        string lKey = line.Substring(0, line.IndexOf('=')).TrimEnd();
                        string lValue = line.Substring(line.IndexOf('=') + 1).TrimStart();

                        if (lKey == "Skin")
                        {
                            if (lValue == "default")
                            {
                                //Can't do anything with the default skin
                                Log("Can't change the background of the default skin. Please select another skin inside osu!");
                                continue;
                            }
                            currentSkinDirectory = Path.Combine(osuDirectory, @"Skins\" + lValue);
                            break;
                        }
                    }
                }
                if (currentSkinDirectory != "")
                {
                    //Backup the current background
                    //if it exists and hasn't already been backed up
                    if (File.Exists(Path.Combine(currentSkinDirectory, OSU_BACKGROUND_NAME))
                        && !File.Exists(Path.Combine(currentSkinDirectory, SKIN_BACKUP_PREFIX + OSU_BACKGROUND_NAME)))
                    {
                        File.Copy(Path.Combine(currentSkinDirectory, OSU_BACKGROUND_NAME), Path.Combine(currentSkinDirectory, SKIN_BACKUP_PREFIX + OSU_BACKGROUND_NAME));
                    }

                    if (ClipRectangle.Right != 0 && OsuLocation.X != int.MinValue)
                    {
                        lock (clippedBG)
                        {
                            //GC sucks for this type of work
                            //So we'll prematurely dispose the bitmap
                            //to prevent memory leaks
                            clippedBG.Dispose();

                            clippedBG = CaptureFromScreen(OsuLocation.X,
                                OsuLocation.Y,
                                ClipRectangle.Right - ClipRectangle.Left,
                                ClipRectangle.Bottom - ClipRectangle.Top);
                            
                            //Save to file
                            try
                            {
                                clippedBG.Save(Path.Combine(currentSkinDirectory, OSU_BACKGROUND_NAME));
                            }
                            catch
                            {
                                //Generic GDI error, skip frame
                                return;
                            }
                        }
                         
                        IntPtr currentWindow = GetForegroundWindow();

                        //SendInput sends commands to the foreground window
                        SetForegroundWindow(OsuProcess.MainWindowHandle);

                        //Sleep while foreground window is being shown
                        Thread.Sleep(100);

                        INPUT[] InputData = new INPUT[4];
                        short[] keys = { 0x38, 0x1D, 0x2A, 0x1F };
                        for (int i = 0; i < 4; i++ )
                        {
                            InputData[i].type = 1;
                            InputData[i].ki.wScan = keys[i];
                            InputData[i].ki.dwFlags = 0x0008; //SCANCODE
                        }
                        SendInput((uint)InputData.Length, InputData, Marshal.SizeOf(typeof(INPUT)));

                        //We don't want to send keyup instantly
                        //100ms delay should be enough in any situation
                        Thread.Sleep(100);

                        for (int i = 0; i < 4; i++)
                            InputData[i].ki.dwFlags |= 0x0002; //KEYUP
                        SendInput((uint)InputData.Length, InputData, Marshal.SizeOf(typeof(INPUT)));

                        SetForegroundWindow(currentWindow);

                        Log("Wallpaper changed!");
                    }
                }
            }
            BackgroundWatcher.EnableRaisingEvents = true;
        }

        private static Bitmap CaptureFromScreen(int x, int y, int width, int height)
        {
            //We'll copy the screen into this bitmap
            Bitmap destinationBitmap = new Bitmap(width, height);
            Graphics g = Graphics.FromImage(destinationBitmap);

            IntPtr graphicsDC = g.GetHdc();
            IntPtr screenDC = GetDC(GetShellWindow());

            //Copy the screen
            BitBlt(graphicsDC, 0, 0, width, height, screenDC, x, y, TernaryRasterOperations.SRCCOPY);

            ReleaseDC(GetShellWindow(), screenDC);
            g.ReleaseHdc();
            g.Dispose();

            return destinationBitmap;
        }

        private static void MonitorOsu()
        {
            while (true)
            {
                //Find the osu! process if not previously found
                if (OsuProcess == null)
                {
                    Log("Attempting to find the osu! process...");

                    //Normal osu! process
                    Process[] processes = Process.GetProcessesByName("osu!");
                    if (processes.Length == 0)
                    {
                        //We haven't found a normal osu! process
                        //See if the user is using osu! test
                        processes = Process.GetProcessesByName("osu!test");
                    }
                    if (processes.Length == 0)
                    {
                        Log("Failed to find the osu! process.\n" +
                                          "Retrying in 1 second");
                        Thread.Sleep(1000);
                        continue;
                    }
                    OsuProcess = processes[0];

                    Log("Found osu! process!");
                }

                //Temporary point so we can force re-draw if position's changed
                Point p = Point.Empty;

                //Get the window rectangle
                if (!GetClientRect(OsuProcess.MainWindowHandle, out ClipRectangle)
                    || !ClientToScreen(OsuProcess.MainWindowHandle, out p))
                {
                    //Couldn't find window, reset to null
                    OsuProcess = null;
                }

                if (OsuLocation != p)
                {
                    OsuLocation = p;
                    WallpaperChanged(0);
                }

                Thread.Sleep(PROCESS_UPDATE_DELAY);
            }
        }

        private static void Log(string text)
        {
            Console.WriteLine(@"{0} - {1}", DateTime.Now.ToLongTimeString(), text);
        }
    }
}