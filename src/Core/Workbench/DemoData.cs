using System.Globalization;
using FileMergeAssistant.Localization;
using FileMergeAssistant.Plans;
using FileMergeAssistant.Sources;
using FileMergeAssistant.Stacks;

namespace FileMergeAssistant.Workbench;

/// <summary>
/// The simulated content of the GUI flow mock (docs/gui-flow-mock.md): eight sources, the scan queue, the stacks on
/// the workbench, earlier runs and a week-later summary. Every path is under <c>C:\Users\demo</c> or a demo drive.
/// </summary>
/// <remarks>
/// Folder, file, source and drive names are what the user's disk holds and stay as they are; what the app says about
/// them comes from the catalog (<c>demo.*</c>) in the display language of the moment each list is built.
/// </remarks>
internal static class DemoData
{
  public static readonly TimeSpan Offset = TimeSpan.FromHours(8);

  public static DateTimeOffset At(int year, int month, int day, int hour = 0, int minute = 0) =>
    new(year, month, day, hour, minute, 0, Offset);

  public static long MB(double megabytes) => (long)(megabytes * 1024 * 1024);

  public static long KB(double kilobytes) => (long)(kilobytes * 1024);

  public static DateTimeOffset Start => At(2026, 10, 23, 14, 32);

  private static string T(string key, params (string Name, object? Value)[] values) => AppText.Get(key, values);

  private static string Photos(int count) =>
    AppText.Plural("demo.stack.photos", count, ("count", count.ToString("N0", CultureInfo.InvariantCulture)));

  private static string Names(params string[] names) => string.Join(AppText.Get("detect.listSeparator"), names);

  private static string Drive(VolumeKind kind, string serial) => new VolumeInfo(string.Empty, serial, null, kind).Describe();

  public static List<SourceInfo> Sources() =>
  [
    new("dev", "開發中", @"C:\Users\demo\source", SourceState.Online, true, 3184, 58930, 0, At(2026, 10, 23, 9, 12), 2, T("demo.source.devDisk"))
    {
      Exclusions = ["*.tmp", "Thumbs.db", T("demo.source.addedRule", ("rule", @"legacy-erp\secrets"))],
    },
    new("proj", "舊專案", @"D:\Projects", SourceState.Online, false, 9762, 31400, 0, At(2026, 10, 22, 21, 40), 0, Drive(VolumeKind.Fixed, "5E90-11F2"),
      Remark: T("demo.source.archivePaused")),
    new("photo", "照片庫", @"D:\Photos", SourceState.Interrupted, false, 12480, 0, 0, At(2026, 10, 20, 23, 5), 0, Drive(VolumeKind.Fixed, "5E90-11F2"),
      Remark: T("demo.source.photoInterrupted")),
    new("wd", "WD 2TB 備份", @"E:\", SourceState.Offline, false, 21530, 0, 0, At(2026, 9, 28, 22, 14), 0, Drive(VolumeKind.Removable, "7C1E-09AB"),
      LastSeenAt: At(2026, 9, 28, 22, 14)),
    new("od", "OneDrive 文件", @"C:\Users\demo\OneDrive\文件", SourceState.Online, false, 2216, 0, 1204, At(2026, 10, 23, 8, 55), 0, T("demo.source.cloud")),
    new("dl", "下載", @"C:\Users\demo\Downloads", SourceState.NeedsUpdate, false, 1920, 0, 0, At(2026, 10, 22, 18, 2), 0, T("demo.source.ssd"),
      Remark: T("demo.source.downloadsGrew")),
    new("desk", "桌面", @"C:\Users\demo\Desktop", SourceState.Queued, false, 412, 0, 0, At(2026, 10, 21, 19, 30), 0, T("demo.source.ssd")),
    new("usb", "USB 隨身碟", @"F:\", SourceState.Scanning, false, 0, 0, 0, null, 0, T("demo.source.usbDisk", ("label", "KINGSTON 64 GB"), ("serial", "C0DE-4417")),
      Remark: T("demo.source.usbAdded")),
  ];

  public static List<ScanJobInfo> Jobs() =>
  [
    new("job-usb", "usb", @"USB 隨身碟 F:\", ScanJobKind.FirstScan, ScanJobState.Running, ScanPhase.Confirm, At(2026, 10, 23, 14, 2))
    {
      Discovered = 9012,
      Candidates = 3100,
      Hashed = 2840,
      Succeeded = 2826,
      Skipped = 14,
      SkippedReason = T("demo.job.excluded"),
      CurrentPath = @"F:\DCIM\2021\0614\IMG_2291.MOV",
      CurrentSize = MB(1.8 * 1024),
      LastCheckpointAt = At(2026, 10, 23, 14, 31),
    },
    new("job-dl", "dl", "下載", ScanJobKind.Update, ScanJobState.Queued, ScanPhase.Discover, At(2026, 10, 23, 13, 50))
    {
      Reason = T("demo.job.downloadsGrew"),
    },
    new("job-desk", "desk", "桌面", ScanJobKind.Update, ScanJobState.Queued, ScanPhase.Discover, At(2026, 10, 23, 13, 51))
    {
      Reason = T("demo.job.lastScan"),
    },
    new("job-photo", "photo", @"照片庫 D:\Photos", ScanJobKind.Resume, ScanJobState.Interrupted, ScanPhase.Confirm, At(2026, 10, 20, 22, 10))
    {
      Saved = 12480,
      Reason = T("demo.job.photoInterrupted"),
    },
    new("job-archive", "proj", @"舊專案 D:\Projects\archive", ScanJobKind.Update, ScanJobState.Paused, ScanPhase.Confirm, At(2026, 10, 21, 20, 0))
    {
      Saved = 1920,
      Reason = T("demo.job.archivePaused"),
    },
    new("job-wd", "wd", @"WD 2TB 備份 E:\", ScanJobKind.Update, ScanJobState.WaitingForSource, ScanPhase.Discover, At(2026, 10, 22, 9, 0))
    {
      Reason = T("scan.job.offline"),
    },
    new("job-dev", "dev", "開發中", ScanJobKind.Update, ScanJobState.Completed, ScanPhase.Done, At(2026, 10, 23, 9, 0))
    {
      Succeeded = 3184,
      Skipped = 12,
      SkippedReason = T("demo.job.excluded"),
      Errors = 2,
      ErrorSummary = T("demo.job.devErrors", ("path", @"C:\Users\demo\source\legacy-erp\certs\")),
      FinishedAt = At(2026, 10, 23, 9, 12),
    },
  ];

  public static List<Stack> Stacks()
  {
    var stacks = new List<Stack>
    {
      Invoice(),
      Kyoto(),
      Quote(),
      Installer(),
      OldSite(),
      Thesis(),
      Dotfiles(),
      Unity(),
      Camera(),
    };
    stacks.AddRange(Filler());
    return stacks;
  }

  private static StackCopy Copy(string stackId, string sourceId, string sourceName, string path, string tag, long bytes, long regenerableBytes,
    long files, DateTimeOffset? modified, bool isProtected = false, bool offline = false) =>
    new($"{stackId}/{tag}", sourceId, sourceName, path, tag, isProtected, offline, bytes, regenerableBytes, files, modified);

  private static FileVersion V(string content, DateTimeOffset modified, long bytes = 4096) => new(content, modified, bytes);

  private static StackRow Row(string path, params FileVersion?[] cells) => new(path, cells);

  private static Stack Invoice()
  {
    const string id = "invoice";
    return new Stack(id, T("demo.stack.invoiceTitle"), StackKind.Folder,
      [
        Copy(id, "dev", "開發中", @"C:\Users\demo\source\invoice-tool", "source", MB(38), MB(1130), 1290, At(2026, 10, 22, 11, 40), isProtected: true) with
        {
          Git = new GitStatus("main", "a91f3c2", 2),
          SuggestReason = T("demo.stack.invoiceKeeper"),
        },
        Copy(id, "proj", "舊專案", @"D:\Projects\invoice-tool-backup-20240311", "backup", MB(36), MB(980), 1287, At(2024, 3, 11)) with
        {
          Git = new GitStatus("main", "5c0d11e", 0, BehindKeeper: 6, AheadOfKeeper: 0),
        },
        Copy(id, "wd", "WD 2TB 備份", @"E:\備份\2023\invoice-tool", "wd", MB(31), 0, 1286, At(2023, 8, 30), offline: true) with
        {
          IndexedAt = At(2026, 9, 28, 22, 14),
          Notes = [T("demo.stack.noGit")],
        },
        Copy(id, "desk", "桌面", @"C:\Users\demo\Desktop\invoice-tool - 複製", "desktop", MB(39), MB(1130), 1291, At(2026, 10, 22, 9, 5)) with
        {
          Git = new GitStatus("main", "a91f3c2", 1),
        },
      ],
      [
        Row("src/templates/receipt.html", V("receipt-a", At(2026, 10, 15)), V("receipt-b", At(2024, 3, 11)), V("receipt-c", At(2023, 8, 30)), V("receipt-d", At(2026, 10, 22), 6144)),
        Row("src/export/pdf.ts", V("pdf-a", At(2026, 10, 20)), V("pdf-b", At(2024, 3, 11)), V("pdf-c", At(2023, 8, 30)), V("pdf-a", At(2026, 10, 20))),
        Row("config.json", V("config-a", At(2026, 10, 18)), V("config-b", At(2024, 3, 2)), null, V("config-a", At(2026, 10, 18))),
        Row("docs/old-spec.md", null, V("spec-a", At(2023, 5, 2)), V("spec-a", At(2023, 5, 2)), null),
        Row("notes.md", null, null, null, V("notes-a", At(2026, 10, 21))),
        Row(".env.local", null, null, null, V("env-a", At(2026, 10, 12), 512)) with
        {
          Warning = T("demo.stack.secretWarning"),
        },
      ],
      SuggestedKeeper: 0,
      SuggestedAction: StackAction.Merge)
    {
      SameFileCount = 1284,
      Regenerable = new RegenerableSummary(Names("node_modules", "dist"), [MB(1130), MB(980), 0, MB(1130)]),
      Evidence = T("demo.stack.invoiceEvidence"),
      UpdatedAt = At(2026, 10, 22, 11, 40),
    };
  }

  private static Stack Kyoto()
  {
    const string id = "kyoto";
    return new Stack(id, T("demo.stack.kyotoTitle"), StackKind.Folder,
      [
        Copy(id, "photo", "照片庫", @"D:\Photos\2019\京都", "photos", MB(2150), 0, 436, At(2019, 11, 3)) with
        {
          Notes = [Photos(436)],
          SuggestReason = T("demo.stack.kyotoKeeper"),
        },
        Copy(id, "desk", "桌面", @"C:\Users\demo\Desktop\京都挑選", "desktop", MB(210), 0, 48, At(2020, 1, 14)) with
        {
          Notes = [Photos(48), T("demo.stack.renamed", ("name", "清水寺-01.jpg"))],
        },
      ],
      [
        new StackRow(Photos(48), [V("kyoto-48", At(2019, 11, 3)), V("kyoto-48", At(2019, 11, 3))], 48,
          T("demo.stack.renamedRow", ("before", "IMG_4410.jpg"), ("after", "清水寺-01.jpg"))),
        new StackRow(Photos(388), [V("kyoto-388", At(2019, 11, 3)), null], 388, T("demo.stack.kyotoOthers")),
      ],
      SuggestedKeeper: 0,
      SuggestedAction: StackAction.KeepKeeperOnly)
    {
      Evidence = T("demo.stack.kyotoEvidence"),
      Note = T("demo.stack.kyotoNote", ("source", "桌面")),
      UpdatedAt = At(2026, 10, 20, 23, 0),
    };
  }

  private static Stack Quote()
  {
    const string id = "quote";
    var modified = At(2026, 9, 30, 16, 20);
    return new Stack(id, "報價單 2024Q3.xlsx", StackKind.File,
      [
        Copy(id, "od", "OneDrive 文件", @"C:\Users\demo\OneDrive\文件\報價\報價單 2024Q3.xlsx", "onedrive", KB(48), 0, 1, modified) with
        {
          Notes = [T("demo.stack.synced")],
          SuggestReason = T("demo.stack.quoteKeeper"),
        },
        Copy(id, "dl", "下載", @"C:\Users\demo\Downloads\報價單 2024Q3 (1).xlsx", "dl1", KB(48), 0, 1, modified),
        Copy(id, "dl", "下載", @"C:\Users\demo\Downloads\報價單 2024Q3 (2).xlsx", "dl2", KB(48), 0, 1, modified),
        Copy(id, "desk", "桌面", @"C:\Users\demo\Desktop\報價單 2024Q3.xlsx", "desktop", KB(48), 0, 1, modified),
      ],
      [],
      SuggestedKeeper: 0,
      SuggestedAction: StackAction.KeepKeeperOnly)
    {
      SameLabel = T("detect.sameLabel.file"),
      Evidence = T("demo.stack.evidenceSized", ("size", "48 KB")),
      RelatedOutside = [new RelatedOutside(@"C:\Users\demo\Desktop\報價單 2024Q3 final.xlsx", At(2026, 10, 2))],
      UpdatedAt = At(2026, 10, 22, 18, 2),
    };
  }

  private static Stack Installer()
  {
    const string id = "msi";
    var modified = At(2024, 2, 9, 10, 0);
    return new Stack(id, "node-v20.11.0-x64.msi", StackKind.File,
      [
        Copy(id, "dl", "下載", @"C:\Users\demo\Downloads\node-v20.11.0-x64.msi", "dl", MB(26), 0, 1, modified) with
        {
          SuggestReason = T("demo.stack.installerKeeper"),
        },
        Copy(id, "dl", "下載", @"C:\Users\demo\Downloads\node-v20.11.0-x64 (1).msi", "dl1", MB(26), 0, 1, modified),
        Copy(id, "dl", "下載", @"C:\Users\demo\Downloads\node-v20.11.0-x64 (2).msi", "dl2", MB(26), 0, 1, modified),
      ],
      [],
      SuggestedKeeper: 0,
      SuggestedAction: StackAction.KeepKeeperOnly)
    {
      SameLabel = T("detect.sameLabel.file"),
      Evidence = T("demo.stack.evidenceSized", ("size", "26 MB")),
      UpdatedAt = At(2026, 10, 22, 18, 2),
    };
  }

  private static Stack OldSite()
  {
    const string id = "oldsite";
    return new Stack(id, "old-site-wordpress", StackKind.Folder,
      [
        Copy(id, "proj", "舊專案", @"D:\Projects\old-site-wordpress", "projects", MB(2355), 0, 4210, At(2021, 4, 18)) with
        {
          SuggestReason = T("demo.stack.oldSiteKeeper"),
        },
        Copy(id, "wd", "WD 2TB 備份", @"E:\old-site-wordpress", "wd", MB(2355), 0, 4210, At(2021, 4, 18), offline: true) with
        {
          IndexedAt = At(2026, 9, 28, 22, 14),
        },
      ],
      [],
      SuggestedKeeper: 0,
      SuggestedAction: StackAction.KeepKeeperOnly)
    {
      SameFileCount = 4210,
      Block = StackBlock.SourceOffline,
      Evidence = T("demo.stack.oldSiteEvidence", ("drive", @"E:\")),
      UpdatedAt = At(2026, 9, 28, 22, 14),
    };
  }

  private static Stack Thesis()
  {
    const string id = "thesis";
    return new Stack(id, "thesis-latex", StackKind.Folder,
      [
        Copy(id, "od", "OneDrive 文件", @"C:\Users\demo\OneDrive\文件\thesis-latex", "onedrive", MB(15), 0, 214, At(2026, 10, 20)) with
        {
          Git = new GitStatus("main", "e41b0a9", 0),
          SuggestReason = T("demo.stack.oneDriveKeeper"),
        },
        Copy(id, "proj", "舊專案", @"D:\Projects\thesis-latex-old", "projects", MB(15), 0, 214, At(2026, 10, 19)) with
        {
          Git = new GitStatus("draft", "77ac5d0", 1),
        },
        Copy(id, "usb", "USB 隨身碟", @"F:\school\thesis-latex", "usb", MB(16), 0, 215, At(2026, 10, 22)),
      ],
      [
        Row("main.tex", V("main-a", At(2026, 10, 20)), V("main-b", At(2025, 6, 2)), V("main-c", At(2026, 10, 22))),
        Row("refs.bib", V("refs-a", At(2026, 10, 18)), V("refs-b", At(2026, 10, 19)), V("refs-c", At(2026, 10, 1))),
        Row("chapters/ch5-draft.tex", null, null, V("ch5-a", At(2026, 10, 21))),
      ],
      SuggestedKeeper: 0,
      SuggestedAction: StackAction.KeepAll)
    {
      SameFileCount = 212,
      Evidence = T("demo.stack.thesisEvidence"),
      Note = T("demo.stack.thesisNote"),
      UpdatedAt = At(2026, 10, 22, 16, 0),
    };
  }

  private static Stack Dotfiles()
  {
    const string id = "dotfiles";
    var old = At(2025, 6, 2);
    return new Stack(id, "dotfiles", StackKind.Folder,
      [
        Copy(id, "dev", "開發中", @"C:\Users\demo\source\dotfiles", "source", KB(310), 0, 64, At(2026, 10, 19), isProtected: true) with
        {
          Git = new GitStatus("main", "0f3e9aa", 0),
          SuggestReason = T("demo.stack.dotfilesKeeper"),
        },
        Copy(id, "proj", "舊專案", @"D:\Projects\dotfiles", "projects", MB(2), 0, 69, old) with
        {
          Git = new GitStatus("main", "c2d4410", 0, BehindKeeper: 11, AheadOfKeeper: 2),
        },
      ],
      [
        Row(".config/nvim/init.lua", null, V("nvim", old)),
        Row(".tmux.conf", null, V("tmux", old)),
        Row(".zshrc.local", null, V("zshrc", old)),
        Row("scripts/backup.sh", null, V("backup", old)),
        Row("scripts/sync.ps1", null, V("sync", old)),
      ],
      SuggestedKeeper: 0,
      SuggestedAction: StackAction.Merge)
    {
      SameFileCount = 64,
      Evidence = T("demo.stack.dotfilesEvidence"),
      UpdatedAt = At(2026, 10, 19, 22, 0),
    };
  }

  private static Stack Unity()
  {
    const string id = "unity";
    var rows = Enumerable.Range(1, 18)
      .Select(i => Row($"Assets/Scripts/Gameplay/System{i:00}.cs",
        V($"unity-a{i}", At(2025, 2, 10)), V($"unity-b{i}", At(2024, 11, 2))))
      .ToList();
    return new Stack(id, "unity-prototype", StackKind.Folder,
      [
        Copy(id, "proj", "舊專案", @"D:\Projects\unity-prototype", "projects", MB(512), MB(5.9 * 1024), 2310, At(2025, 2, 10)) with
        {
          SuggestReason = T("demo.stack.unityKeeper"),
        },
        Copy(id, "wd", "WD 2TB 備份", @"E:\unity-prototype", "wd", MB(498), MB(5.9 * 1024), 2302, At(2024, 11, 2), offline: true) with
        {
          IndexedAt = At(2026, 9, 28, 22, 14),
        },
      ],
      rows,
      SuggestedKeeper: 0,
      SuggestedAction: StackAction.KeepKeeperOnly)
    {
      SameFileCount = 2284,
      Block = StackBlock.SourceOffline,
      Regenerable = new RegenerableSummary("Library", [MB(5.9 * 1024), MB(5.9 * 1024)]),
      Evidence = T("demo.stack.unityEvidence"),
      UpdatedAt = At(2026, 9, 28, 22, 14),
    };
  }

  private static Stack Camera()
  {
    const string id = "camera";
    return new Stack(id, "camera-roll-2021", StackKind.Folder,
      [
        Copy(id, "photo", "照片庫", @"D:\Photos\2021\相機", "photos", MB(3.4 * 1024), 0, 1460, At(2021, 12, 30)) with
        {
          SuggestReason = T("demo.stack.cameraKeeper", ("source", "照片庫")),
        },
        Copy(id, "usb", "USB 隨身碟", @"F:\DCIM\2021", "usb", MB(3.1 * 1024), 0, 1200, At(2021, 12, 30)),
      ],
      [],
      SuggestedKeeper: 0,
      SuggestedAction: StackAction.KeepKeeperOnly)
    {
      SameFileCount = 1200,
      SameLabel = T("demo.stack.cameraCompared"),
      Block = StackBlock.StillScanning,
      Evidence = T("demo.stack.cameraEvidence", ("drive", @"F:\")),
      UpdatedAt = At(2026, 10, 23, 14, 30),
    };
  }

  /// <summary>Simple stacks so the list has the mock's size: 38 more open stacks, 12 converged and 4 kept all.</summary>
  private static IEnumerable<Stack> Filler()
  {
    var files = new (string Name, double Mb, string[] Places)[]
    {
      ("VSCodeUserSetup-x64-1.94.2.exe", 95, ["dl", "dl", "desk"]),
      ("Docker Desktop Installer.exe", 560, ["dl", "dl"]),
      ("合約掃描-2025.pdf", 3.2, ["dl", "desk", "od"]),
      ("resume-2025.docx", 0.08, ["od", "desk"]),
      ("IMG_2041.HEIC", 2.8, ["photo", "dl"]),
      ("年度預算.xlsx", 0.4, ["od", "dl", "desk"]),
      ("virtio-win-0.1.262.iso", 710, ["dl", "proj"]),
      ("發票-2026-09.pdf", 0.2, ["dl", "od"]),
      ("logo-final.ai", 12, ["desk", "od"]),
      ("presentation-v3.pptx", 48, ["od", "desk", "dl"]),
      ("dataset-sample.csv", 140, ["proj", "dl"]),
      ("headshot.jpg", 4.1, ["photo", "desk"]),
      ("firmware-1.2.bin", 16, ["dl", "proj"]),
      ("會議記錄-0912.docx", 0.06, ["od", "dl"]),
      ("screen-recording-0301.mp4", 420, ["desk", "dl"]),
      ("python-3.12.4-amd64.exe", 25, ["dl", "dl"]),
      ("passport-scan.pdf", 1.6, ["od", "desk"]),
      ("demo-video.mov", 380, ["desk", "photo"]),
      ("backup-notes.txt", 0.01, ["proj", "desk"]),
      ("ChromeSetup.exe", 1.3, ["dl", "dl", "dl"]),
      ("map-tiles.zip", 240, ["proj", "dl"]),
      ("租約.pdf", 2.2, ["od", "dl"]),
      ("wallpaper-4k.png", 9, ["photo", "desk", "dl"]),
      ("font-awesome-6.zip", 32, ["dl", "proj"]),
    };
    var folders = new (string Name, double Mb, double RegenMb, string[] Places)[]
    {
      ("react-playground", 4, 410, ["dev", "proj"]),
      ("fonts-noto-tc", 120, 0, ["proj", "dl"]),
      ("design-assets", 340, 0, ["desk", "od"]),
      ("scripts-misc", 1, 0, ["dev", "desk"]),
      ("blog-hugo", 22, 180, ["dev", "proj"]),
      ("tax-2024", 36, 0, ["od", "desk"]),
      ("go-microservice", 6, 95, ["dev", "proj"]),
      ("wedding-video-raw", 8200, 0, ["photo", "proj"]),
      ("arduino-sketches", 2, 0, ["proj", "desk"]),
      ("ml-notebooks", 210, 60, ["dev", "proj", "desk"]),
      ("mobile-app-flutter", 55, 640, ["dev", "proj"]),
      ("receipts-scans", 80, 0, ["od", "dl"]),
      ("vacation-2023-picks", 520, 0, ["photo", "desk"]),
      ("old-website-assets", 150, 0, ["proj", "desk"]),
    };
    var names = new Dictionary<string, (string Name, string Root)>(StringComparer.Ordinal)
    {
      ["dev"] = ("開發中", @"C:\Users\demo\source"),
      ["proj"] = ("舊專案", @"D:\Projects"),
      ["photo"] = ("照片庫", @"D:\Photos"),
      ["od"] = ("OneDrive 文件", @"C:\Users\demo\OneDrive\文件"),
      ["dl"] = ("下載", @"C:\Users\demo\Downloads"),
      ["desk"] = ("桌面", @"C:\Users\demo\Desktop"),
    };

    var number = 0;
    foreach (var (name, mb, places) in files)
    {
      var id = $"file{++number:00}";
      var modified = At(2026, 1 + number % 9, 1 + number % 27, 10, 0);
      var copies = places.Select((place, i) =>
      {
        var (sourceName, root) = names[place];
        var fileName = i == 0 || places.Take(i).All(p => p != place)
          ? name
          : $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}";
        var copy = Copy(id, place, sourceName, Path.Combine(root, fileName), $"{place}{i}", MB(mb), 0, 1, modified);
        return i == 0 ? copy with { SuggestReason = T("demo.stack.fileKeeper") } : copy;
      }).ToList();
      yield return new Stack(id, name, StackKind.File, copies, [], 0, StackAction.KeepKeeperOnly)
      {
        SameLabel = T("detect.sameLabel.file"),
        Evidence = T("demo.stack.evidenceVerified"),
        UpdatedAt = modified,
        Status = number <= 6 ? StackStatus.Converged : StackStatus.Open,
      };
    }

    number = 0;
    foreach (var (name, mb, regenMb, places) in folders)
    {
      var id = $"folder{++number:00}";
      var modified = At(2025, 1 + number % 12, 1 + number % 27, 21, 0);
      var copies = places.Select((place, i) =>
      {
        var (sourceName, root) = names[place];
        // Copy folders as Windows names them on the demo user's (Chinese) desktop; sample data, not app text.
        var folder = i == 0 ? name : $"{name}{(place == "desk" ? " - 複製" : "-backup")}";
        var copy = Copy(id, place, sourceName, Path.Combine(root, folder), $"{place}{i}", MB(mb), MB(regenMb), 120 + number * 37, modified,
          isProtected: place == "dev");
        return i == 0 ? copy with { SuggestReason = T("demo.stack.folderKeeper") } : copy;
      }).ToList();
      var status = number switch
      {
        <= 6 => StackStatus.Converged,
        <= 10 => StackStatus.KeptAll,
        _ => StackStatus.Open,
      };
      yield return new Stack(id, name, StackKind.Folder, copies, [], 0, StackAction.KeepKeeperOnly)
      {
        SameFileCount = 120 + number * 37,
        Regenerable = regenMb > 0 ? new RegenerableSummary(Names("node_modules", "build"), copies.Select(c => c.RegenerableBytes).ToList()) : null,
        Evidence = T("demo.stack.evidenceStructure"),
        UpdatedAt = modified,
        Status = status,
      };
    }
  }

  public static List<ExecutionBatch> History()
  {
    static BatchItem Recycled(string batch, int n, string target, long bytes, long files = 1) =>
      new($"{batch}#{n}", "history", PlanItemKind.Recycle, target, BatchItemState.Done)
      {
        Bytes = bytes,
        FileCount = files,
        RecycleVolume = Path.GetPathRoot(target),
      };

    var october21 = new ExecutionBatch("batch-1021", At(2026, 10, 21, 20, 10),
    [
      Recycled("batch-1021", 1, @"D:\Projects\dotfiles\.gitconfig", KB(2)),
      Recycled("batch-1021", 2, @"D:\Projects\scripts-old", MB(3), 41),
      Recycled("batch-1021", 3, @"C:\Users\demo\Downloads\Git-2.46.0-64-bit (1).exe", MB(64)),
      Recycled("batch-1021", 4, @"C:\Users\demo\Downloads\Git-2.46.0-64-bit (2).exe", MB(64)),
      Recycled("batch-1021", 5, @"D:\Projects\api-mock-backup", MB(98), 310),
      Recycled("batch-1021", 6, @"C:\Users\demo\Desktop\筆記 - 複製.md", KB(14)) with { State = BatchItemState.Restored },
    ]);
    var october12 = new ExecutionBatch("batch-1012", At(2026, 10, 12, 22, 3),
      Enumerable.Range(1, 14).Select(i => Recycled("batch-1012", i, $@"C:\Users\demo\Downloads\photo-export-{i:00}.zip", MB(11))).ToList());
    var october2 = new ExecutionBatch("batch-1002", At(2026, 10, 2, 9, 45),
      Enumerable.Range(1, 21).Select(i => Recycled("batch-1002", i, $@"D:\Photos\import-dup\IMG_{3100 + i}.JPG", MB(4))).ToList())
    {
      EmptiedAt = At(2026, 10, 9, 21, 0),
    };
    return [october21, october12, october2];
  }

  public static List<RestoreConflict> RestoreConflicts() =>
  [
    new("conflict-gitconfig", "batch-1021", "batch-1021#1", @"D:\Projects\dotfiles\.gitconfig",
      At(2026, 10, 21, 20, 10), KB(2), At(2025, 6, 2), At(2026, 10, 22, 8, 40), KB(3)),
  ];

  public static List<RecycleArea> RecycleAreas() => [new(@"C:\", MB(410)), new(@"D:\", MB(230))];

  public static SinceLastVisit Since() => new(
    At(2026, 10, 23, 14, 36),
    At(2026, 10, 30, 9, 15),
    47,
    [
      new OpenStackDelta(T("demo.since.settled"), -3),
      new OpenStackDelta(T("demo.since.skipped"), 1),
      new OpenStackDelta(T("demo.since.found", ("drive", @"F:\")), 2),
      new OpenStackDelta(T("demo.since.dissolved"), -1),
    ],
    [
      new ChangeNotice("drive", NoticeKind.DriveReconnected, true, T("demo.notice.driveTitle", ("name", "WD 2TB 備份"), ("drive", @"G:\")),
        T("demo.notice.driveText", ("serial", "7C1E-09AB")))
      { SourceId = "wd" },
      new ChangeNotice("invoice", NoticeKind.NewCopy, true, T("demo.notice.copyTitle"),
        T("demo.notice.copyText", ("path", @"C:\Users\demo\Downloads\invoice-tool-main (1)")))
      { StackId = "invoice" },
      new ChangeNotice("thesis", NoticeKind.KeptAllChanged, true, T("demo.notice.keptTitle"), T("demo.notice.keptText"))
      { StackId = "thesis" },
      new ChangeNotice("camera", NoticeKind.StackReady, true, T("demo.notice.readyTitle"),
        T("demo.notice.readyText", ("drive", @"F:\"), ("first", @"D:\Photos\2021\相機"), ("second", @"F:\DCIM\2021")))
      { StackId = "camera" },
      new ChangeNotice("unity", NoticeKind.CopyLost, false, T("demo.notice.lostTitle"),
        T("demo.notice.lostText", ("path", @"D:\Projects\unity-prototype"), ("drive", @"D:\"), ("source", "WD 2TB 備份"))),
      new ChangeNotice("downloads", NoticeKind.SourceGrew, false, T("demo.notice.grewTitle", ("source", "下載")), T("demo.notice.grewText")),
      new ChangeNotice("recycle", NoticeKind.RecycleReminder, false, T("demo.notice.recycleTitle"), T("demo.notice.recycleText")),
    ]);

  public static List<SuggestedLocation> Locations() =>
  [
    new(@"C:\Users\demo\source", LocationSuggester.GitProjects(23), true),
    new(@"D:\Projects", T("scan.volume.fixed"), true),
    new(@"D:\Photos", T("demo.location.photos"), true),
    new(@"E:\", T("scan.suggest.removableLabel", ("label", "WD 2TB 備份")), true),
    new(@"C:\Users\demo\OneDrive\文件", T("scan.suggest.oneDrive"), false),
    new(@"C:\Users\demo\Downloads", T("scan.suggest.downloads"), false),
    new(@"C:\Users\demo\Desktop", T("scan.suggest.desktop"), false),
  ];
}
