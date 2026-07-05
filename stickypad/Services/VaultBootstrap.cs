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
            // 볼트 폴더가 활성 목록과 일치하도록 — 목록에 보이는 노트만 옮긴다.
            // 휴지통 노트와 내용 없는 빈 자리표시 노트는 제외(볼트 폴더에 목록에 없는
            // .md 가 잔뜩 생기던 문제 방지). 원본 notes.db 는 그대로 보존되므로
            // 휴지통은 LiteDB 모드로 되돌리면 다시 볼 수 있다.
            var toSeed = db.GetAllAsync().GetAwaiter().GetResult()
                .Where(n => !string.IsNullOrWhiteSpace(n.PlainText))
                .ToList();
            if (toSeed.Count == 0) return;

            new VaultStore(vaultFolder).Save(toSeed);
            Log.Information("Seeded vault {Folder} from {Count} active notes", vaultFolder, toSeed.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Vault seed failed for {Folder}", vaultFolder);
        }
    }
}
