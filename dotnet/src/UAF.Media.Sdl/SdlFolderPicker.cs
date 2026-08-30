using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL;
using static SDL.SDL3;

namespace UAF.Media.Sdl;

/// <summary>
/// A native folder picker — the cross-platform successor to the original engine's
/// <c>XBrowseForFolder</c> call in <c>DESIGN_SELECT_MENU_DATA</c> (<c>RunEvent.cpp</c>), which is how
/// the shipped game asked for a design folder. The automatic scan beside it was commented out, so a
/// folder dialog <i>is</i> the original behaviour, not a stand-in.
/// </summary>
/// <remarks>
/// <para>
/// <b>The dialog is asynchronous.</b> <c>SDL_ShowOpenFolderDialog</c> returns immediately and the
/// callback fires later, on the thread that pumps SDL's events — so <see cref="PickFolder"/> blocks
/// pumping events until the user chooses or cancels.
/// </para>
/// <para>
/// <b>It needs the video subsystem up</b>, because the native dialog runs inside SDL's event loop.
/// <c>SDL_Init</c> is idempotent, so calling it again when the game later opens its window is free.
/// </para>
/// </remarks>
public static unsafe class SdlFolderPicker
{
    // The result is written by the callback (which runs while PickFolder pumps events) and read back
    // after the loop returns. Static rather than a callback argument because the callback is a bare
    // function pointer with an unmanaged signature — there is no object to carry state in.
    private static string? selectedPath;
    private static bool completed;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Callback(nint userdata, byte** filelist, int filter)
    {
        // filelist is a null-terminated array of UTF-8 paths; allowMany is false, so the first
        // entry is the choice and a null first entry means the user cancelled.
        selectedPath = filelist is not null && filelist[0] is not null
            ? PtrToStringUTF8(filelist[0], free: false)
            : null;
        completed = true;
    }

    /// <summary>
    /// Shows the folder dialog and returns the chosen folder, or null when the user cancelled.
    /// </summary>
    public static string? PickFolder()
    {
        selectedPath = null;
        completed = false;

        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO))
        {
            throw new InvalidOperationException($"SDL_Init failed: {SDL_GetError()}");
        }

        SDL_ShowOpenFolderDialog(
            (delegate* unmanaged[Cdecl]<nint, byte**, int, void>)&Callback,
            0, null, (byte*)null, false);

        while (!completed)
        {
            SDL_PumpEvents();
            Thread.Sleep(10);
        }

        return selectedPath;
    }
}
