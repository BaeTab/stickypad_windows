using System;

namespace StickyPad.Services;

public interface ITrayService : IDisposable
{
    void Initialize();

    /// 트레이 풍선 알림(best-effort) — 트레이 초기화에 실패했으면 조용히 무시된다.
    void ShowNotification(string title, string message);
}
