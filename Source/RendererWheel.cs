using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FrameDeck
{
    internal sealed partial class DeckWindow
    {
        private delegate IntPtr QueueHook(int code, IntPtr operation, IntPtr data);

        private const int GetMessageHook = 3;
        private const int RemoveMessage = 1;
        private const int FirstChild = 5;
        // The native MSG starts with a window handle followed by a 32-bit message ID.
        private static readonly int MessageIdOffset = IntPtr.Size;
        private static readonly int WordParamOffset = IntPtr.Size * 2;
        private static readonly int LongParamOffset = IntPtr.Size * 3;

        private QueueHook rendererInputCallback;
        private IntPtr rendererHook;
        private IntPtr rendererInputSurface;
        private uint rendererThread;

        private void AttachRendererInput()
        {
            IntPtr surface = video.Handle;
            IntPtr child = GetWindow(surface, FirstChild);
            if (child == IntPtr.Zero) return;
            uint process;
            uint thread = GetWindowThreadProcessId(child, out process);
            uint surfaceProcess;
            uint surfaceThread = GetWindowThreadProcessId(surface, out surfaceProcess);
            if (thread == 0 || thread == surfaceThread || process != surfaceProcess || thread == rendererThread) return;
            DetachRendererInput();
            rendererInputSurface = surface;
            if (rendererInputCallback == null) rendererInputCallback = ForwardRendererMessage;
            // Scope the hook to this process's VLC renderer thread.
            rendererHook = SetWindowsHookEx(GetMessageHook, rendererInputCallback, IntPtr.Zero, thread);
            if (rendererHook != IntPtr.Zero) rendererThread = thread;
        }

        private void DetachRendererInput()
        {
            if (rendererHook != IntPtr.Zero) UnhookWindowsHookEx(rendererHook);
            rendererHook = IntPtr.Zero;
            rendererThread = 0;
            rendererInputSurface = IntPtr.Zero;
        }

        private IntPtr ForwardRendererMessage(int code, IntPtr operation, IntPtr data)
        {
            IntPtr surface = rendererInputSurface;
            if (code >= 0 && operation.ToInt64() == RemoveMessage && surface != IntPtr.Zero)
            {
                int message = Marshal.ReadInt32(data, MessageIdOffset);
                if (message == MouseWheelMessage || message == RightButtonUpMessage || message == KeyDownMessage)
                {
                    IntPtr source = Marshal.ReadIntPtr(data);
                    IntPtr word = Marshal.ReadIntPtr(data, WordParamOffset);
                    if (IsChild(surface, source) &&
                        (message != KeyDownMessage || IsForwardedKey(word.ToInt32())) &&
                        PostMessage(surface, message, word, Marshal.ReadIntPtr(data, LongParamOffset)))
                        Marshal.WriteInt32(data, MessageIdOffset, 0);
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, operation, data);
        }

        private static bool IsForwardedKey(int key)
        {
            // Keep all keyboard actions confined to VLC's video surface or FrameDeck itself.
            switch ((Keys)key)
            {
                case Keys.A:
                case Keys.B:
                case Keys.V:
                case Keys.I:
                case Keys.Escape:
                    return (GetKeyState(0x10) & 0x8000) == 0 && (GetKeyState(0x11) & 0x8000) == 0 &&
                        (GetKeyState(0x12) & 0x8000) == 0;
                default:
                    return false;
            }
        }

        [DllImport("user32.dll")] private static extern short GetKeyState(int key);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW")] private static extern IntPtr SetWindowsHookEx(int hook, QueueHook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr operation, IntPtr data);
        [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessage(IntPtr window, int message, IntPtr word, IntPtr parameter);
    }
}
