using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Tobii.Interaction;

namespace Interaction_Streams_101
{
    public class Program
    {
        // Global Variables
        private static Host _host;
        private static bool _stopOnDwell;
        private static bool _moveOnDwell;
        private static bool _loggingMode;
        private static bool _wKeyUp;

        // Windows API helper functions
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out Point position);

        [DllImport("user32.dll")]
        private static extern int SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr windowPtr, out Rectangle rect);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr windowPtr, ref Point windowPos);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);
        
        private const int KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const int KEYEVENTF_KEYUP = 0x0002;
        private const int VK_W = 0x57;

        public static void Main(string[] args)
        {
            // Everything starts with initializing Host, which manages connection to the 
            // Tobii Engine and provides all the Tobii Core SDK functionality.
            // NOTE: Make sure that Tobii.EyeX.exe is running.
            _host = new Host();
            var collect = new Stopwatch();
            var dwell = new Stopwatch();
            var prevEyePos = new Point(-1, -1);
            bool moved;

            if (args.Length > 0)
            {
                if (args[0].Equals("-d"))
                    _stopOnDwell = true;
                else if (args[0].Equals("-m"))
                    _moveOnDwell = true;
                else if (args[0].Equals("-l"))
                    _loggingMode = true;
            }
            
            // Get the currently running MultiCraft window. If no window found, end the program.
            var minecraftWindow = FindWindow(null, "Minecraft 1.9");
            if (!GetWindowRect(minecraftWindow, out var rect))
            {
                Console.WriteLine("No Minecraft 1.9 window found.");
                EndHostConnection();
            }
            var windowWidth = rect.Right - rect.Left;
            var windowHeight = rect.Bottom - rect.Top;

            // Create stream. 
            var gazePointDataStream = _host.Streams.CreateGazePointDataStream();
            collect.Start();

            // Get the gaze data.
            gazePointDataStream.GazePoint((x, y, _) =>
            {
                var t = DateTime.UtcNow.ToString("hh:mm:ss.fff");
                x = (int)x;
                y = (int)y;

                if (dwell.IsRunning && dwell.Elapsed > TimeSpan.FromSeconds(3))
                {
                    Console.WriteLine("{0},{1},{2},{3}", t, x, y, "dwelling...");
                    if (_stopOnDwell)
                    {
                        EndHostConnection();
                    }
                    else if (_moveOnDwell && _wKeyUp)
                    {
                        _wKeyUp = false;
                        keybd_event(VK_W, 0, KEYEVENTF_EXTENDEDKEY, 0);
                    }
                }

                if (collect.Elapsed > TimeSpan.FromMilliseconds(16))
                {
                    Console.WriteLine("{0},{1},{2},{3}", t, x, y, "");
                    var eyePos = new Point((int)x, (int)y);

                    if (!_loggingMode && GetCursorPos(out var cursorPos) && ScreenToClient(minecraftWindow, ref eyePos))
                    {
                        moved = MoveCursor(eyePos, cursorPos, windowWidth, windowHeight);
                        if (!moved && !dwell.IsRunning)
                        {
                            dwell.Start();
                            Console.WriteLine("{0},{1},{2},{3}", t, x, y, "starting dwell.");
                        }
                        else if (moved && dwell.IsRunning)
                        {
                            Console.WriteLine("{0},{1},{2},{3}", t, x, y, ("ending dwell. length: " + dwell.Elapsed.TotalMilliseconds.ToString() + " ms"));
                            dwell.Reset();
                            if (_moveOnDwell && !_wKeyUp)
                            {
                                _wKeyUp = true;
                                keybd_event(VK_W, 0, KEYEVENTF_KEYUP, 0);
                            }
                        }
                    }
                    else if (_loggingMode && prevEyePos.X != -1 && prevEyePos.Y != -1)
                    {
                        moved = CheckDisplacement(eyePos, prevEyePos);
                        if (!moved && !dwell.IsRunning)
                        {
                            dwell.Start();
                            Console.WriteLine("{0},{1},{2},{3}", t, x, y, "starting dwell.");
                        }
                        else if (moved && dwell.IsRunning)
                        {
                            Console.WriteLine("{0},{1},{2},{3}", t, x, y, ("ending dwell. length: " + dwell.Elapsed.TotalMilliseconds.ToString() + " ms."));
                            dwell.Reset();
                        }
                    }
                    prevEyePos.X = eyePos.X;
                    prevEyePos.Y = eyePos.Y;

                    collect.Reset();
                    collect.Start();
                }
            });

            Console.CancelKeyPress += new ConsoleCancelEventHandler(CancelHandler);
            while (_loggingMode || (!_loggingMode && GetKeyState((int)Keys.OemPeriod) == 0))
            {
                // Wait for user to press . key to end if not logging
            }
            EndHostConnection();
        }

        private static bool CheckDisplacement(Point eyePos, Point prevEyePos)
        {
            // Get displacement from previous eye position
            var displaceX = eyePos.X - prevEyePos.X;
            var displaceY = eyePos.Y - prevEyePos.Y;

            // Set up bounding box for dwell
            if (Math.Abs(displaceX) < 25 && Math.Abs(displaceY) < 25)
                return false;

            // Return true if eye position has significant displacement
            return true;
        }

        private static bool MoveCursor(Point eyePos, Point cursorPos,  int windowWidth, int windowHeight)
        {
            // Get displacement from center of window
            var displaceX = eyePos.X - (windowWidth / 2);
            var displaceY = eyePos.Y - (windowHeight / 2);

            // Set up bounding box for dwell
            if (Math.Abs(displaceX) < windowWidth / 25) displaceX = 0;
            if (displaceY < windowHeight / 20 && displaceY > -(windowHeight / 5)) displaceY = 0;

            // If eyePos is within bounding box, don't move cursor
            if (displaceX == 0 && displaceY == 0)
                return false;
            SetCursorPos(cursorPos.X + displaceX / 40, cursorPos.Y + displaceY / 30);
            return true;
        }

        private static void CancelHandler(object sender, ConsoleCancelEventArgs args)
        {
            EndHostConnection();
        }

        private static void EndHostConnection()
        {
            // Close connection to the Tobii Engine before exit.
            _host.DisableConnection();
            Environment.Exit(0);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int X;           // x position of cursor
            public int Y;           // y position of cursor

            public Point (int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rectangle
        {
            public int Left;        // x position of upper-left corner
            public int Top;         // y position of upper-left corner
            public int Right;       // x position of lower-right corner
            public int Bottom;      // y position of lower-right corner
        }
    }
}
