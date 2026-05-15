using ScrollerCapture;

// bin/Release/net8.0-windows -> project -> tools -> repo root
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var outPath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(repoRoot, "Assets", "ScrollerCapture.ico");

AppIconFactory.WriteIcoFile(outPath);
Console.WriteLine($"Wrote {outPath}");
