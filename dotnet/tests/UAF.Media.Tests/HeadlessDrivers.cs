using System.Runtime.CompilerServices;
using UAF.Media.Sdl;

namespace UAF.Media.Tests;

/// <summary>
/// Forces SDL's dummy video and audio drivers before any test can touch SDL.
/// </summary>
/// <remarks>
/// <para>
/// A module initialiser, so it runs when the test assembly loads and cannot be ordered after something
/// that initialises SDL. The whole suite has to pass on a machine with no display, because CI has none;
/// leaving that to a fixture means a test added later, outside the SDL collection, could quietly try to
/// open a window and only fail in CI.
/// </para>
/// <para>
/// It calls SDL's hint API rather than setting <c>SDL_VIDEODRIVER</c>, because
/// <c>Environment.SetEnvironmentVariable</c> does not reach a native library's <c>getenv</c> on macOS or
/// Linux — .NET keeps its own copy of the environment there. It does work on Windows, which is what makes
/// the mistake worth guarding against: headless would appear to work on one platform out of three. See
/// <see cref="SdlPlatform.ForceDummyDrivers"/>.
/// </para>
/// <para>
/// Setting the same drivers from outside the process, as the CI workflow and the spike do, remains
/// correct and takes effect first.
/// </para>
/// </remarks>
internal static class HeadlessDrivers
{
    [ModuleInitializer]
    public static void ForceDummyDrivers() => SdlPlatform.ForceDummyDrivers();
}
