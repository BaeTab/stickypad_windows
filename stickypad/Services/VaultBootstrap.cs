using System;
using System.IO;
using System.Linq;
using Serilog;

namespace StickyPad.Services;

/// 볼트 모드 최초 전환 시의 안전한 1회 이관. 볼트 폴더가 비어 있고(=.md 없음) 기존 LiteDB 에
/// 노트가 있으면 LiteDB 노트(활성+휴지통)를 볼트로 복사한다.
/// 이미 노트가 있는 볼트는 절대 덮어쓰지 않는다(데이터 안전).
public static class VaultBootstrap
{
    public static void EnsureSeeded(string vaultFolder, string dbPath)
    {
        try
        {
            if (Directory.Exists(vaultFolder) &&
                Directory.EnumerateFiles(vaultFolder, "*.md", SearchOption.TopDirectoryOnly).Any())
            {
                return; // 이미 노트가 있는 볼트 — 손대지 않는다.
            }
            if (!File.Exists(dbPath)) return;

            using var db = new NoteRepository(dbPath);
            var all = db.GetAllAsync().GetAwaiter().GetResult()
                .Concat(db.GetTrashedAsync().GetAwaiter().GetResult())
                .ToList();
            if (all.Count == 0) return;

            new VaultStore(vaultFolder).Save(all);
            Log.Information("Seeded vault {Folder} from {Count} LiteDB notes", vaultFolder, all.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Vault seed failed for {Folder}", vaultFolder);
        }
    }
}
