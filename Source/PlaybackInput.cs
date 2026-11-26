using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FrameDeck
{
    internal sealed partial class DeckWindow : IMessageFilter
    {
        private const int RightButtonUpMessage = 0x0205;
        private const int MouseWheelMessage = 0x020A;
        private const int KeyDownMessage = 0x0100;
        private const int AppCommandMessage = 0x0319;
        private int wheelRemainder;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsChild(IntPtr parent, IntPtr child);

        bool IMessageFilter.PreFilterMessage(ref Message message)
        {
            if (closingPlayback || section != 2 || !IsHandleCreated || trackMenu.Visible ||
                (message.Msg != MouseWheelMessage && message.Msg != KeyDownMessage && message.Msg != RightButtonUpMessage))
                return false;
            if (message.HWnd != Handle && !IsChild(Handle, message.HWnd)) return false;

            if (message.Msg == RightButtonUpMessage)
            {
                if (!cleanView || message.HWnd != video.Handle) return false;
                SetCleanView(false);
                return true;
            }

            if (message.Msg == MouseWheelMessage)
            {
                long position = message.LParam.ToInt64();
                return HandlePlaybackWheel(new Point(unchecked((short)position), unchecked((short)(position >> 16))),
                    unchecked((short)(message.WParam.ToInt64() >> 16)));
            }

            var key = (Keys)message.WParam.ToInt32() | ModifierKeys;
            bool repeat = (message.LParam.ToInt64() & (1L << 30)) != 0;
            return HandlePlaybackKey(key, repeat);
        }

        private bool HandlePlaybackWheel(Point location, int delta)
        {
            if (closingPlayback || section != 2 || !attemptedPlayback || trackMenu.Visible || delta == 0)
                return false;
            if (!player.RectangleToScreen(player.ClientRectangle).Contains(location) ||
                (timeline.Visible && timeline.RectangleToScreen(timeline.ClientRectangle).Contains(location)))
                return false;

            int notch = SystemInformation.MouseWheelScrollDelta;
            wheelRemainder += delta;
            int steps = wheelRemainder / notch;
            wheelRemainder %= notch;
            if (steps != 0) ChangeVolume(state.Volume + steps * 5);
            return true;
        }

        private void ForwardPlaybackWheel(object sender, MouseEventArgs args)
        {
            if (HandlePlaybackWheel(((Control)sender).PointToScreen(args.Location), args.Delta))
            {
                var handled = args as HandledMouseEventArgs;
                if (handled != null) handled.Handled = true;
            }
        }

        private bool HandlePlaybackKey(Keys key, bool repeat)
        {
            if (closingPlayback || section != 2 || trackMenu.Visible) return false;
            switch (key)
            {
                case Keys.Space:
                case Keys.K:
                case Keys.MediaPlayPause:
                    if (!repeat) TogglePause();
                    return true;
                case Keys.S:
                case Keys.MediaStop:
                    if (!repeat) StopPlayback();
                    return true;
                case Keys.F:
                case Keys.F11:
                    if (!repeat) ToggleFullScreen();
                    return true;
                case Keys.P:
                    if (!repeat) ToggleMiniPlayer();
                    return true;
                case Keys.I:
                    if (!repeat) ToggleCleanView();
                    return true;
                case Keys.A:
                    if (!repeat) CycleAspectRatio();
                    return true;
                case Keys.B:
                    if (!repeat) CycleTrack(false);
                    return true;
                case Keys.V:
                    if (!repeat) CycleTrack(true);
                    return true;
                case Keys.Escape:
                    if (!cleanView && !fullScreen && !miniPlayer) return false;
                    if (repeat) return true;
                    if (cleanView) SetCleanView(false);
                    else if (miniPlayer) LeaveMiniPlayer();
                    else ToggleFullScreen();
                    return true;
                case Keys.M:
                    if (!repeat) ToggleMute();
                    return true;
                case Keys.Left:
                case Keys.Right:
                case Keys.J:
                case Keys.L:
                    long offset = key == Keys.Left ? -5000 : key == Keys.Right ? 5000 : key == Keys.J ? -10000 : 10000;
                    Seek((queuedSeek ?? (seeking ? seekTarget : elapsed)) + offset);
                    return true;
                case Keys.Home:
                    Seek(0);
                    return true;
                case Keys.Up:
                case Keys.Down:
                    ChangeVolume(state.Volume + (key == Keys.Up ? 5 : -5));
                    return true;
                default:
                    return false;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == AppCommandMessage)
            {
                int command = (int)((message.LParam.ToInt64() >> 16) & 0x0fff);
                if ((command == 14 && HandlePlaybackKey(Keys.MediaPlayPause, false)) ||
                    (command == 13 && HandlePlaybackKey(Keys.MediaStop, false)))
                {
                    message.Result = new IntPtr(1);
                    return;
                }
            }
            base.WndProc(ref message);
        }
    }
}
