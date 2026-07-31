using UAF.Media;
using UAF.Media.Sdl;
using UAFcore;

// The first executable in this port. Everything it does is a thin wiring of pieces that are
// individually tested: LoadedDesign reads the design, Game holds the state and draws into a
// managed Surface, and only this file knows SDL exists.

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: UAFcore <design-directory>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  The directory containing Data/ and Resources/, e.g.");
    Console.Error.WriteLine("  reference/SomethingWild.dsn");
    return 2;
}

// --dump <path> renders one frame and exits without opening a window. It exists so the executable
// itself can be smoke-tested on a machine with no display, rather than only its library pieces.
string? dumpPath = null;
int dumpAt = Array.IndexOf(args, "--dump");
if (dumpAt >= 0 && dumpAt + 1 < args.Length)
{
    dumpPath = args[dumpAt + 1];
}

string root = args[0];
if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"no such directory: {root}");
    return 2;
}

using var rasterizer = new SdlFontRasterizer();
if (!rasterizer.IsAvailable)
{
    // Text is optional by construction; the design still loads and draws without it.
    Console.Error.WriteLine($"warning: no text ({rasterizer.UnavailableReason})");
}

using var design = LoadedDesign.Open(root, new SdlImageDecoder(), rasterizer);
var game = new Game(design);

Console.WriteLine($"{design.Name}, version {design.Globals.Version.Value:0.00}");
Console.WriteLine($"  {design.Globals.Characters.Count} characters, " +
                  $"{design.LevelFiles.Count} levels, " +
                  $"font {design.Globals.Font}");
Console.WriteLine("  arrows move and turn, escape quits");

if (dumpPath is not null)
{
    // Walk a few steps first, so the dump exercises the state machine rather than only the
    // initial draw.
    // Walk until an event fires, so the dump shows the executor doing something rather than an
    // empty corridor.
    for (int i = 0; i < 200 && game.CurrentEvent is null; i++)
    {
        game.Update(InputEvent.KeyDown(i % 3 == 2 ? VirtualKey.Right : VirtualKey.Up));
    }

    var frame = game.Render();
    var raw = new byte[frame.Pixels.Length * 4];
    for (int i = 0; i < frame.Pixels.Length; i++)
    {
        uint pixel = frame.Pixels[i];
        raw[i * 4] = (byte)(pixel >> 16);
        raw[(i * 4) + 1] = (byte)(pixel >> 8);
        raw[(i * 4) + 2] = (byte)pixel;
        raw[(i * 4) + 3] = 0xFF;
    }

    File.WriteAllBytes(dumpPath, raw);
    File.WriteAllText(dumpPath + ".dim", $"{frame.Width}x{frame.Height}");
    Console.WriteLine($"wrote {frame.Width}x{frame.Height} frame to {dumpPath}");
    return 0;
}

using var presenter = new SdlPresenter(640, 480, $"UAFcore — {design.Name}");
var input = new SdlInputSource(640, 480);

presenter.Present(game.Render());

while (game.Running)
{
    input.Pump();

    bool dirty = false;
    while (input.TryPoll(out var next))
    {
        dirty |= game.Update(next);
    }

    // Running is checked as well as dirty: Escape changes state that is not drawn, so repainting
    // on the way out just presents a duplicate frame.
    if (dirty && game.Running)
    {
        presenter.Present(game.Render());
    }

    // The original ran an uncapped loop on a dedicated thread; this is a placeholder until the
    // engine has real timing to schedule against.
    Thread.Sleep(16);
}

Console.WriteLine($"{game.Steps} steps, ended at ({game.X}, {game.Y}) facing {game.Facing}");
return 0;
