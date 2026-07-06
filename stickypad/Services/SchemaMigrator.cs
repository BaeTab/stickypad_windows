using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using Serilog;

namespace StickyPad.Services;

/// DB 스키마 버전을 관리하고, 앱이 기대하는 최신 버전까지 순차 마이그레이션한다.
/// 버전은 LiteDB 의 내장 <see cref="LiteDatabase.UserVersion"/> 에 저장한다.
///
/// 2.0(볼트·동기화)로 가면서 Note 모델/저장 방식이 바뀔 때, 여기 마이그레이션을 추가하고
/// <see cref="CurrentVersion"/> 을 +1 하면 기존 사용자 DB 가 자동으로 최신 형태로 올라온다.
public static class SchemaMigrator
{
    /// 앱이 기대하는 현재 DB 스키마 버전. 스키마를 바꿀 때마다 +1 하고 아래 Migrations 에 항목 추가.
    public const int CurrentVersion = 1;

    /// (From, Apply): From 버전에서 From+1 버전으로 올리는 변환. 순서/누락 없이 채워야 한다.
    private static readonly IReadOnlyList<(int From, Action<LiteDatabase> Apply)> Migrations =
        Array.Empty<(int, Action<LiteDatabase>)>();

    public static void Migrate(LiteDatabase db) => Migrate(db, CurrentVersion, Migrations);

    /// 실제 스키마 마이그레이션이 필요할 때에 한해, DB 를 열기 전에 파일을 스냅샷한다.
    /// DB 버전이 이미 최신이면(=일반 사용자) 아무것도 하지 않아 부담이 없다.
    /// 마이그레이션이 잘못돼도 <c>notes.db.v{버전}.bak</c> 로 되돌릴 수 있게 한다.
    public static void BackupBeforeMigration(string dbPath) => BackupBeforeMigration(dbPath, CurrentVersion);

    internal static void BackupBeforeMigration(string dbPath, int currentVersion)
    {
        try
        {
            if (!File.Exists(dbPath)) return;

            int version;
            using (var probe = new LiteDatabase($"Filename={dbPath};Connection=shared"))
                version = probe.UserVersion;
            if (version < 1) version = 1;            // 0 = 버전 미표시 DB = baseline 1
            if (version >= currentVersion) return;   // 올릴 것이 없음 → 스냅샷 불필요

            var backup = $"{dbPath}.v{version}.bak";
            if (File.Exists(backup)) return;       // 이 버전 스냅샷이 이미 있음
            File.Copy(dbPath, backup);
            Log.Information("Backed up DB before schema migration (v{Version}) -> {Backup}", version, backup);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Pre-migration DB backup skipped");
        }
    }

    /// 테스트 가능한 코어. from==DB의 현재 버전에서 시작해 target 까지 한 단계씩 적용한다.
    internal static void Migrate(
        LiteDatabase db, int targetVersion,
        IReadOnlyList<(int From, Action<LiteDatabase> Apply)> migrations)
    {
        var version = db.UserVersion;

        // UserVersion 0 = 스키마 버전이 아직 표시되지 않은 DB(신규이거나 1.x). 현재 Note 형태가
        // 곧 v1 이므로 마이그레이션 없이 baseline(1)로 간주한다.
        if (version < 1) version = 1;

        // DB 가 앱보다 최신(다운그레이드) — 손대지 않고 그대로 둔다.
        if (version > targetVersion)
        {
            Log.Warning("DB schema v{Version} is newer than app v{Target}; leaving as-is", version, targetVersion);
            return;
        }

        while (version < targetVersion)
        {
            var migration = migrations.FirstOrDefault(m => m.From == version);
            if (migration.Apply is null)
                throw new InvalidOperationException($"No migration registered from schema v{version}.");

            Log.Information("Migrating DB schema v{From} -> v{To}", version, version + 1);
            migration.Apply(db);
            version++;
        }

        if (db.UserVersion != version) db.UserVersion = version;
    }
}
